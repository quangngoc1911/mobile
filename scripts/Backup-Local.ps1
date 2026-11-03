param(
    [switch] $WritesStopped,
    [string] $SourceDatabase = 'flowershop',
    [string] $StoragePath
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/Use-LocalConfig.ps1"
if (-not $WritesStopped) { throw 'Cần dừng mọi thao tác ghi vào API và storage, sau đó chạy lại với -WritesStopped để xác nhận.' }
$root = Split-Path -Parent $PSScriptRoot
if ($SourceDatabase -notmatch '^[a-z_][a-z0-9_]{0,62}$') { throw 'Tên database không hợp lệ.' }
# Có thể chọn database và storage thử riêng để diễn tập mà không đụng dữ liệu đang dùng.
$storagePath = if ($StoragePath) { [System.IO.Path]::GetFullPath($StoragePath) } else { $env:Storage__RootPath }
if (-not (Test-Path -LiteralPath $storagePath -PathType Container)) { throw 'Không tìm thấy thư mục storage local; không tạo thư mục rỗng để sao lưu.' }
$output = Join-Path $root ("artifacts/backups/{0}-{1}" -f (Get-Date -Format 'yyyyMMdd-HHmmss'), [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $output | Out-Null
$databaseFile = Join-Path $output 'flowershop.dump'
$storageArchive = Join-Path $output 'storage.tar'
$temporaryContainer = "flowershop-backup-$([Guid]::NewGuid().ToString('N'))"
# pg_dump được ghi bên trong container, sau đó docker cp giữ nguyên định dạng nhị phân.
docker compose exec -T postgres pg_dump -U flowershop -d $SourceDatabase --format=custom -f "/tmp/$temporaryContainer.dump"
if ($LASTEXITCODE -ne 0) { throw 'pg_dump thất bại.' }
try {
    $containerId = docker compose ps -q postgres
    if (-not $containerId) { throw 'Không tìm thấy PostgreSQL của dự án.' }
    docker cp "${containerId}:/tmp/$temporaryContainer.dump" $databaseFile
    if ($LASTEXITCODE -ne 0) { throw 'Không thể sao chép database dump.' }
}
finally { docker compose exec -T postgres rm -f "/tmp/$temporaryContainer.dump" }
tar -cf $storageArchive -C $storagePath .
if ($LASTEXITCODE -ne 0) { throw 'Không thể sao lưu thư mục ảnh.' }
Write-Output "Đã tạo backup cục bộ tại $output. Cần mã hóa và sao chép sang nơi độc lập trước khi coi là backup vận hành."
