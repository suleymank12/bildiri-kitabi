# Creates .env from .env.example and fills the three password placeholders with random values.
# An existing .env is never changed. Works from any directory; the repository root is found from this file.
# Runs on Windows PowerShell 5.1 and PowerShell 7.
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$envFile = Join-Path $root '.env'
$exampleFile = Join-Path $root '.env.example'

if (Test-Path -LiteralPath $envFile) {
    Write-Output '.env zaten var, değiştirilmedi.'
    exit 0
}

if (-not (Test-Path -LiteralPath $exampleFile -PathType Leaf)) {
    [Console]::Error.WriteLine(".env.example bulunamadı: $exampleFile")
    exit 1
}

# 64 characters: a random byte modulo 64 picks every character with the same probability.
$alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_'
$rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()

# 24 characters, retried until upper case, lower case and a digit are all present,
# so the SQL Server password policy is always met.
function New-Password {
    $bytes = New-Object byte[] 24
    while ($true) {
        $rng.GetBytes($bytes)
        $chars = foreach ($b in $bytes) { $alphabet[$b % 64] }
        $candidate = -join $chars
        if ($candidate -cmatch '[A-Z]' -and $candidate -cmatch '[a-z]' -and $candidate -match '[0-9]') {
            return $candidate
        }
    }
}

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$content = [System.IO.File]::ReadAllText($exampleFile, $utf8NoBom)

foreach ($key in 'MSSQL_SA_PASSWORD', 'APP_DB_PASSWORD', 'RABBITMQ_PASSWORD') {
    $pattern = "(?m)^$key=<[^>\r\n]*>(?=\r?$)"
    if ($content -notmatch $pattern) {
        [Console]::Error.WriteLine(".env.example beklenen yer tutucuları içermiyor ($key); .env oluşturulmadı.")
        exit 1
    }
    $content = [regex]::Replace($content, $pattern, "$key=$(New-Password)")
}
$rng.Dispose()

# CreateNew: a .env created in the meantime is not replaced.
try {
    $stream = [System.IO.File]::Open($envFile, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::Write)
}
catch {
    if (Test-Path -LiteralPath $envFile) {
        Write-Output '.env zaten var, değiştirilmedi.'
        exit 0
    }
    throw
}
try {
    $bytes = $utf8NoBom.GetBytes($content)
    $stream.Write($bytes, 0, $bytes.Length)
}
finally {
    $stream.Dispose()
}

Write-Output '.env oluşturuldu. Parolalar bu dosyada; RabbitMQ yönetim arayüzü için RABBITMQ_PASSWORD satırına bakabilirsiniz.'
