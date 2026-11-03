Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$workspace = Split-Path -Parent $PSScriptRoot
$secretFile = Join-Path $workspace '.env'
if (Test-Path -LiteralPath $secretFile) { throw '.env đã tồn tại; không ghi đè mật khẩu hoặc dữ liệu hiện có.' }
# Mật khẩu được sinh cục bộ; tệp bị .gitignore loại trừ.
$randomBytes = [System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32)
$dbPassword = [Convert]::ToHexString($randomBytes)
$jwtSecret = [Convert]::ToHexString([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
@("FLOWERSHOP_DB_PASSWORD=$dbPassword", 'FLOWERSHOP_DB_PORT=25432', "FLOWERSHOP_JWT_KEY=$jwtSecret") | Set-Content -LiteralPath $secretFile -Encoding utf8NoBOM
Write-Output 'Đã tạo .env cho PostgreSQL local. Giữ tệp này ở chế độ riêng tư.'
