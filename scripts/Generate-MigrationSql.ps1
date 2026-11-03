Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$workspace = Split-Path -Parent $PSScriptRoot
Push-Location $workspace
try {
    $outputDirectory = Join-Path $workspace 'artifacts/migrations'
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
    $outputFile = Join-Path $outputDirectory ("migration-{0}.sql" -f (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
    # Script được xuất để xem xét; lệnh này không áp dụng thay đổi vào database.
    dotnet ef migrations script --idempotent --project src/FlowerShop.Infrastructure --startup-project src/FlowerShop.Infrastructure --output $outputFile
    if ($LASTEXITCODE -ne 0) { throw 'Không thể sinh migration SQL.' }
    Write-Output "Đã tạo: $outputFile"
}
finally { Pop-Location }
