#!/bin/bash
# Runs docker/db-init.sql against the mssql service, retrying for about a minute while SQL Server finishes opening
# its databases. The script is idempotent, so running it again is harmless. A failed login is permanent (wrong
# sa password) and is not retried. Passwords reach sqlcmd only through environment variables, never through its
# command line, and are never printed.
set -u

max_attempts=30
delay_seconds=2

# sqlcmd reads the sa password from SQLCMDPASSWORD and resolves $(AppPassword) in the script from the environment.
export SQLCMDPASSWORD="${MSSQL_SA_PASSWORD:?MSSQL_SA_PASSWORD is not set}"
export AppPassword="${APP_DB_PASSWORD:?APP_DB_PASSWORD is not set}"

for attempt in $(seq 1 "$max_attempts"); do
    if output=$(/opt/mssql-tools18/bin/sqlcmd -S mssql -U sa -C -b -i /db-init/db-init.sql 2>&1); then
        echo "$output"
        exit 0
    fi

    if grep -q "Login failed" <<<"$output"; then
        echo "$output"
        echo "db-init: sa ile oturum açılamadı; .env'deki MSSQL_SA_PASSWORD değerini kontrol edin." >&2
        exit 1
    fi

    echo "db-init: deneme $attempt/$max_attempts başarısız, SQL Server henüz hazır değil; $delay_seconds sn sonra yeniden denenecek."
    if [ "$attempt" -lt "$max_attempts" ]; then
        sleep "$delay_seconds"
    fi
done

echo "$output"
echo "db-init: $max_attempts denemede tamamlanamadı." >&2
exit 1
