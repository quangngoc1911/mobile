param([Parameter(Mandatory)] [string] $BackupDirectory)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$backupRoot = [System.IO.Path]::GetFullPath((Join-Path $root 'artifacts/backups'))
$backupPath = [System.IO.Path]::GetFullPath($BackupDirectory)
if (-not $backupPath.StartsWith($backupRoot + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Chỉ kiểm tra bản sao thuộc artifacts/backups của dự án.' }
$dumpFile = Join-Path $backupPath 'flowershop.dump'
$archiveFile = Join-Path $backupPath 'storage.tar'
if (-not (Test-Path -LiteralPath $dumpFile) -or -not (Test-Path -LiteralPath $archiveFile)) { throw 'Thiếu database dump hoặc archive ảnh.' }
$suffix = [Guid]::NewGuid().ToString('N')
$restoreDatabase = "flowershop_restore_$suffix"
$remoteDump = "/tmp/flowershop-restore-$suffix.dump"
$restoreDirectory = Join-Path $root "artifacts/restore-tests/$suffix"
$containerId = docker compose ps -q postgres
if (-not $containerId) { throw 'Không tìm thấy container PostgreSQL của dự án.' }
$createdDatabase = $false
try {
    New-Item -ItemType Directory -Path $restoreDirectory -Force | Out-Null
    docker cp $dumpFile "${containerId}:$remoteDump"
    if ($LASTEXITCODE -ne 0) { throw 'Không thể chuyển dump vào container kiểm thử.' }
    docker compose exec -T postgres createdb -U flowershop $restoreDatabase
    if ($LASTEXITCODE -ne 0) { throw 'Không tạo được database kiểm thử.' }
    $createdDatabase = $true
    docker compose exec -T postgres pg_restore -U flowershop -d $restoreDatabase --exit-on-error --no-owner $remoteDump
    if ($LASTEXITCODE -ne 0) { throw 'Khôi phục database thất bại.' }
    # Đối chiếu với metadata trong chính dump; database nguồn có thể đã thay đổi sau lúc backup.
    $query = 'SELECT COALESCE(json_agg(json_build_object(''storageKey'', "StorageKey", ''checksum'', "Checksum", ''length'', "Length"))::text, ''[]'') FROM "Media"'
    $mediaJson = docker compose exec -T postgres psql -X -U flowershop -d $restoreDatabase -At -c $query
    if ($LASTEXITCODE -ne 0) { throw 'Không đọc được metadata ảnh từ database đã khôi phục.' }
    $mediaRows = @(ConvertFrom-Json -InputObject ($mediaJson -join "`n"))
    $entries = @(tar -tf $archiveFile)
    if ($LASTEXITCODE -ne 0) { throw 'Không đọc được danh sách archive ảnh.' }
    foreach ($entry in $entries) {
        $normalized = $entry.Replace('\', '/')
        if ($normalized.StartsWith('/') -or $normalized -match '^[A-Za-z]:' -or $normalized -match '(^|/)\.\.(/|$)') { throw 'Archive ảnh chứa đường dẫn không an toàn.' }
    }
    tar -xf $archiveFile -C $restoreDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Khôi phục archive ảnh thất bại.' }
    $files = @(Get-ChildItem -LiteralPath $restoreDirectory -Recurse -File)
    $restoredRoot = [System.IO.Path]::GetFullPath($restoreDirectory)
    foreach ($media in $mediaRows) {
        $key = [string]$media.storageKey
        $file = [System.IO.Path]::GetFullPath((Join-Path $restoredRoot $key))
        if (-not $file.StartsWith($restoredRoot + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'StorageKey trỏ ra ngoài thư mục khôi phục.' }
        if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Thiếu file ảnh theo metadata: $key" }
        if ((Get-Item -LiteralPath $file).Length -ne [long]$media.length -or (Get-FileHash -Algorithm SHA256 -LiteralPath $file).Hash -ne [string]$media.checksum) { throw "Kích thước hoặc checksum ảnh không khớp metadata: $key" }
    }
    Write-Output "PASS: pg_restore hoàn tất vào database kiểm thử riêng; $($mediaRows.Count) metadata ảnh được đối chiếu với file khôi phục; archive có $($files.Count) file."
    if ($mediaRows.Count -eq 0) { Write-Output 'NOT VERIFIED: database backup không có ảnh nên chưa chứng minh khôi phục ảnh thực tế.' }
    if ($files.Count -gt $mediaRows.Count) { Write-Warning 'Archive có file không được metadata tham chiếu; cần kiểm tra trước khi dùng backup.' }
}
finally {
    if ($createdDatabase -and $restoreDatabase -match '^flowershop_restore_[0-9a-f]{32}$') { docker compose exec -T postgres dropdb -U flowershop --if-exists --force $restoreDatabase }
    docker compose exec -T postgres rm -f $remoteDump
}
