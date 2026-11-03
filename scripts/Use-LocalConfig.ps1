# Dot-source script này trong cùng PowerShell; không in bí mật ra màn hình.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$secretFile = Join-Path (Split-Path -Parent $PSScriptRoot) '.env'
if (-not (Test-Path -LiteralPath $secretFile)) { throw 'Chưa có .env; chạy scripts/Initialize-LocalSecrets.ps1 trước.' }
$settings = @{}
foreach ($line in Get-Content -LiteralPath $secretFile) {
    if ($line -match '^([A-Z_]+)=(.*)$') { $settings[$Matches[1]] = $Matches[2] }
}
if (-not $settings['FLOWERSHOP_DB_PASSWORD'] -or -not $settings['FLOWERSHOP_DB_PORT'] -or -not $settings['FLOWERSHOP_JWT_KEY']) { throw '.env thiếu cấu hình local bắt buộc.' }
$env:FLOWERSHOP_CONNECTION_STRING = "Host=127.0.0.1;Port=$($settings['FLOWERSHOP_DB_PORT']);Database=flowershop;Username=flowershop;Password=$($settings['FLOWERSHOP_DB_PASSWORD'])"
$env:Jwt__SigningKey = $settings['FLOWERSHOP_JWT_KEY']
$env:Storage__RootPath = Join-Path (Split-Path -Parent $PSScriptRoot) 'data/storage'
