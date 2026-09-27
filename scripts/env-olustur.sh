#!/bin/sh
# Creates .env from .env.example and fills the three password placeholders with random values.
# An existing .env is never changed. Works from any directory; the repository root is found from this file.
set -eu

root=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
env_file="$root/.env"
example_file="$root/.env.example"

if [ -e "$env_file" ]; then
    echo ".env zaten var, değiştirilmedi."
    exit 0
fi

if [ ! -f "$example_file" ]; then
    echo ".env.example bulunamadı: $example_file" >&2
    exit 1
fi

# 24 characters from A-Z a-z 0-9 - _ read from /dev/urandom; retried until upper case, lower case and a digit
# are all present, so the SQL Server password policy is always met.
new_password() {
    while :; do
        candidate=$(LC_ALL=C tr -dc 'A-Za-z0-9_-' < /dev/urandom | dd bs=24 count=1 2>/dev/null)
        [ ${#candidate} -eq 24 ] || continue
        case $candidate in *[A-Z]*) ;; *) continue ;; esac
        case $candidate in *[a-z]*) ;; *) continue ;; esac
        case $candidate in *[0-9]*) ;; *) continue ;; esac
        printf '%s' "$candidate"
        return 0
    done
}

sa_password=$(new_password)
app_password=$(new_password)
rabbitmq_password=$(new_password)

umask 077
tmp_file="$env_file.tmp.$$"
trap 'rm -f "$tmp_file"' EXIT

sed \
    -e "s|^MSSQL_SA_PASSWORD=<[^>]*>\$|MSSQL_SA_PASSWORD=$sa_password|" \
    -e "s|^APP_DB_PASSWORD=<[^>]*>\$|APP_DB_PASSWORD=$app_password|" \
    -e "s|^RABBITMQ_PASSWORD=<[^>]*>\$|RABBITMQ_PASSWORD=$rabbitmq_password|" \
    "$example_file" > "$tmp_file"

if grep -q '^[A-Z_]*=<' "$tmp_file"; then
    echo ".env.example beklenen yer tutucuları içermiyor; .env oluşturulmadı." >&2
    exit 1
fi

# noclobber: a .env created in the meantime is not replaced.
set -C
if ! cat "$tmp_file" 2>/dev/null > "$env_file"; then
    echo ".env zaten var, değiştirilmedi."
    exit 0
fi

echo ".env oluşturuldu. Parolalar bu dosyada; RabbitMQ yönetim arayüzü için RABBITMQ_PASSWORD satırına bakabilirsiniz."
