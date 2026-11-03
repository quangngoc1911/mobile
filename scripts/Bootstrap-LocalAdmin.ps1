Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$workspace = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'Use-LocalConfig.ps1')

# Bootstrap chỉ tạo tài khoản đầu tiên khi bảng Users còn trống; không ghi mật khẩu vào file hay log.
$username = Read-Host 'Tên đăng nhập quản trị đầu tiên'
$fullName = Read-Host 'Họ tên'
$securePassword = Read-Host 'Mật khẩu (ít nhất 12 ký tự)' -AsSecureString
$plainPassword = [System.Net.NetworkCredential]::new('', $securePassword).Password

try {
    if ($plainPassword.Length -lt 12) { throw 'Mật khẩu phải có ít nhất 12 ký tự.' }

    $env:FLOWERSHOP_BOOTSTRAP_USERNAME = $username
    $env:FLOWERSHOP_BOOTSTRAP_FULLNAME = $fullName
    $env:FLOWERSHOP_BOOTSTRAP_PASSWORD = $plainPassword
    & dotnet run --project (Join-Path $workspace 'src/FlowerShop.Api') -- --bootstrap
    if ($LASTEXITCODE -ne 0) { throw "Bootstrap thất bại với mã thoát $LASTEXITCODE." }
}
finally {
    Remove-Item Env:\FLOWERSHOP_BOOTSTRAP_USERNAME, Env:\FLOWERSHOP_BOOTSTRAP_FULLNAME, Env:\FLOWERSHOP_BOOTSTRAP_PASSWORD -ErrorAction SilentlyContinue
    $securePassword.Dispose()
    $plainPassword = $null
}
