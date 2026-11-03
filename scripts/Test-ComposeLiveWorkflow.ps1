[CmdletBinding()]
param(
    [string]$AdminUsername
)

# Kiểm thử này cố ý ghi dữ liệu vào PostgreSQL Compose hiện tại để kiểm tra
# đúng chuỗi API thật. Không dùng mock và không tự dọn các bản ghi vừa tạo.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$workspaceRoot = Split-Path -Parent $PSScriptRoot
$envFile = Join-Path $workspaceRoot '.env'

function Read-DotEnvValue([string]$name) {
    if (-not (Test-Path -LiteralPath $envFile)) { return $null }
    $pattern = '^' + [regex]::Escape($name) + '=(.*)$'
    foreach ($line in Get-Content -LiteralPath $envFile) {
        if ($line -match $pattern) {
            return $Matches[1].Trim().Trim('"').Trim("'")
        }
    }
    return $null
}

$webPort = if ($env:FLOWERSHOP_MOBILE_WEB_PORT) { $env:FLOWERSHOP_MOBILE_WEB_PORT } else { Read-DotEnvValue 'FLOWERSHOP_MOBILE_WEB_PORT' }
if ([string]::IsNullOrWhiteSpace($webPort)) { $webPort = '8085' }
$publishedPort = & docker compose --project-directory $workspaceRoot port mobile-web 8080 2>$null
if ($LASTEXITCODE -ne 0 -or $publishedPort -ne "127.0.0.1:$webPort") {
    throw 'Cổng mobile-web không khớp Compose đang chạy. Hãy chạy script tại thư mục gốc dự án và kiểm tra docker compose ps.'
}
$ApiBaseUrl = "http://127.0.0.1:$webPort/backend"
$AdminPassword = if ($env:FLOWERSHOP_LOCAL_ADMIN_PASSWORD) { $env:FLOWERSHOP_LOCAL_ADMIN_PASSWORD } else { Read-DotEnvValue 'FLOWERSHOP_LOCAL_ADMIN_PASSWORD' }
if ([string]::IsNullOrWhiteSpace($AdminPassword)) {
    throw 'Không tìm thấy FLOWERSHOP_LOCAL_ADMIN_PASSWORD. Không đưa mật khẩu vào tham số command line; hãy đặt biến môi trường hoặc .env local.'
}

function Invoke-DbQuery([string]$query) {
    # psql chạy trong container nên không cần đọc hoặc in mật khẩu PostgreSQL.
    $output = $query | & docker compose --project-directory $workspaceRoot exec -T postgres psql -v ON_ERROR_STOP=1 -U flowershop -d flowershop -At -F '|' 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Truy vấn PostgreSQL thất bại (exit code $LASTEXITCODE)."
    }
    return @($output | Where-Object { $_ -is [string] -and $_.Trim() -and $_ -notmatch '^psql:\s+warning:' })
}

function SqlLiteral([string]$value) {
    return "'" + $value.Replace("'", "''") + "'"
}

function Invoke-Api {
    param(
        [Parameter(Mandatory)][ValidateSet('GET', 'POST', 'PUT', 'DELETE')][string]$Method,
        [Parameter(Mandatory)][string]$Path,
        [object]$Body,
        [hashtable]$Headers = @{},
        [int[]]$ExpectedStatus = @(200)
    )

    $requestHeaders = @{}
    foreach ($key in $Headers.Keys) { $requestHeaders[$key] = [string]$Headers[$key] }
    $json = $null
    if ($null -ne $Body) { $json = $Body | ConvertTo-Json -Depth 20 -Compress }
    try {
        $response = Invoke-WebRequest -Uri "$ApiBaseUrl$Path" -Method $Method -Headers $requestHeaders -ContentType 'application/json' -Body $json -SkipHttpErrorCheck
    }
    catch {
        throw ("Không gọi được API {0} {1}: {2}" -f $Method, $Path, $_.Exception.Message)
    }
    $status = [int]$response.StatusCode
    if ($ExpectedStatus -notcontains $status) {
        # Không đưa request body (có thể chứa mật khẩu) vào lỗi hoặc log.
        throw "API $Method $Path trả HTTP $status; kiểm thử dừng để không ghi tiếp dữ liệu không xác định."
    }
    if ([string]::IsNullOrWhiteSpace($response.Content)) { return $null }
    try { return $response.Content | ConvertFrom-Json -Depth 20 }
    catch { throw "API $Method $Path trả nội dung không phải JSON hợp lệ (HTTP $status)." }
}

function Require-Equal($actual, $expected, [string]$message) {
    if ($actual -ne $expected) { throw "$message (thực tế: '$actual', mong đợi: '$expected')." }
}

function Require-True([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
}

Write-Host "Kiểm tra API health tại $ApiBaseUrl ..."
$health = Invoke-Api -Method GET -Path '/health'
Require-Equal ([string]$health.status) 'ok' 'Health API không ở trạng thái ok.'

if ([string]::IsNullOrWhiteSpace($AdminUsername)) {
    $adminRows = @(Invoke-DbQuery @'
SELECT u."Username"
FROM "Users" u
JOIN "UserRoles" admin_role ON admin_role."UserId" = u."Id" AND admin_role."Role" = 'Admin'
JOIN "UserRoles" owner_role ON owner_role."UserId" = u."Id" AND owner_role."Role" = 'Owner'
WHERE u."IsActive" = TRUE
ORDER BY u."CreatedAt", u."Username";
'@)
    if ($adminRows.Count -ne 1) {
        throw "Cần truyền -AdminUsername khi database có $($adminRows.Count) tài khoản Admin+Owner đang hoạt động."
    }
    $AdminUsername = [string]$adminRows[0]
}

# Mã chạy thử được sinh mới mỗi lần; API tự sinh UUID, SKU và mã đơn.
$runId = ([guid]::NewGuid().ToString('N')).Substring(0, 8).ToUpperInvariant()
$branchCode = "T$runId"
$branchName = "LIVE E2E $runId"
$productName = "LIVE material $runId"
$bouquetName = "LIVE bouquet $runId"
$supplierName = "LIVE supplier $runId"
$customerName = "LIVE customer $runId"
$note = "LIVE_COMPOSE_E2E_$runId"

Write-Host "Mã lượt thử: $runId; cửa hàng sẽ tạo: $branchCode."
Write-Host "Đăng nhập vai trò Owner bằng tài khoản '$AdminUsername' (không in mật khẩu)."
$login = Invoke-Api -Method POST -Path '/api/auth/login' -Body @{ username = $AdminUsername; password = $AdminPassword; activeRole = 'Owner' }
$token = [string]$login.tokens.accessToken
Require-True (-not [string]::IsNullOrWhiteSpace($token)) 'API không trả access token.'
$authHeaders = @{ Authorization = "Bearer $token" }

try {
    $branch = Invoke-Api -Method POST -Path '/api/branches' -Headers $authHeaders -ExpectedStatus @(201) -Body @{
        code = $branchCode
        name = $branchName
        address = "LIVE test $runId"
    }
    $branchId = [string]$branch.id
    $parsedBranchId = [guid]::Empty
    Require-True ([guid]::TryParse($branchId, [ref]$parsedBranchId)) 'API không trả branch id hợp lệ.'

    $product = Invoke-Api -Method POST -Path '/api/products' -Headers $authHeaders -ExpectedStatus @(201) -Body @{
        name = $productName
        kind = 'Material'
        unit = 'cành'
        variant = $runId
        categoryId = $null
        price = 12000
        lowStockThreshold = 1
    }
    $materialProductId = [string]$product.id
    $materialSku = [string]$product.sku
    Require-True (-not [string]::IsNullOrWhiteSpace($materialProductId) -and -not [string]::IsNullOrWhiteSpace($materialSku)) 'API không trả sản phẩm hoặc SKU.'

    $receiptQuantity = [decimal]3
    $materialQuantity = [decimal]1
    $receiptHeaders = @{ Authorization = "Bearer $token"; 'Idempotency-Key' = [guid]::NewGuid().ToString() }
    $receiptBody = @{
        branchId = $branchId
        supplierName = $supplierName
        note = $note
        items = @(@{ productId = $materialProductId; quantity = $receiptQuantity; unitCost = 5000 })
    }
    $receipt = Invoke-Api -Method POST -Path '/api/inventory/receipts' -Headers $receiptHeaders -ExpectedStatus @(200) -Body $receiptBody
    $receiptAgain = Invoke-Api -Method POST -Path '/api/inventory/receipts' -Headers $receiptHeaders -ExpectedStatus @(200) -Body $receiptBody
    Require-Equal ([string]$receiptAgain.id) ([string]$receipt.id) 'Gửi lại phiếu nhập không trả cùng chứng từ.'
    Require-Equal ([string]$receipt.branchId) $branchId 'Phiếu nhập không thuộc cửa hàng vừa tạo.'

    $bouquet = Invoke-Api -Method POST -Path '/api/bouquets/assemble' -Headers (@{ Authorization = "Bearer $token"; 'Idempotency-Key' = [guid]::NewGuid().ToString() }) -ExpectedStatus @(200) -Body @{
        branchId = $branchId
        orderItemId = $null
        name = $bouquetName
        price = 30000
        note = $note
        materials = @(@{ productId = $materialProductId; quantity = $materialQuantity })
    }
    $bouquetId = [string]$bouquet.bouquet.id
    $bouquetProductId = [string]$bouquet.bouquet.productId
    $bouquetSku = [string]$bouquet.sku
    Require-True (-not [string]::IsNullOrWhiteSpace($bouquetId) -and -not [string]::IsNullOrWhiteSpace($bouquetProductId)) 'API không trả bó thành phẩm hợp lệ.'

    $orderHeaders = @{ Authorization = "Bearer $token"; 'Idempotency-Key' = [guid]::NewGuid().ToString() }
    $orderBody = @{
        branchId = $branchId
        customerName = $customerName
        customerPhone = $null
        customerAddress = $null
        note = $note
        scheduledAt = $null
        shippingFee = 0
        items = @(@{ productId = $bouquetProductId; name = $null; unit = $null; quantity = 1; unitPrice = $null; discount = 0; discountReason = $null; note = $note; isCustomBouquet = $false })
    }
    $order = Invoke-Api -Method POST -Path '/api/orders' -Headers $orderHeaders -ExpectedStatus @(201) -Body $orderBody
    $orderAgain = Invoke-Api -Method POST -Path '/api/orders' -Headers $orderHeaders -ExpectedStatus @(201) -Body $orderBody
    Require-Equal ([string]$orderAgain.id) ([string]$order.id) 'Gửi lại yêu cầu tạo đơn không trả cùng đơn.'
    $orderId = [string]$order.id
    $orderCode = [string]$order.code
    $orderTotal = [decimal]$order.total
    Require-True (-not [string]::IsNullOrWhiteSpace($orderId) -and $orderTotal -gt 0) 'API không trả đơn hàng hợp lệ.'
    Write-Host "Đơn thử đã tạo: $orderCode."

    $firstPayment = [decimal]([math]::Round([double]($orderTotal / 3), 2, [MidpointRounding]::ToEven))
    if ($firstPayment -le 0 -or $firstPayment -ge $orderTotal) { throw 'Không tạo được hai khoản thanh toán dương từ tổng đơn.' }
    $secondPayment = $orderTotal - $firstPayment
    foreach ($payment in @(@{ method = 'Cash'; amount = $firstPayment; note = $note }, @{ method = 'Transfer'; amount = $secondPayment; note = $note })) {
        $paymentHeaders = @{ Authorization = "Bearer $token"; 'Idempotency-Key' = [guid]::NewGuid().ToString() }
        $paymentResult = Invoke-Api -Method POST -Path "/api/orders/$orderId/payments" -Headers $paymentHeaders -ExpectedStatus @(200) -Body $payment
        $paymentAgain = Invoke-Api -Method POST -Path "/api/orders/$orderId/payments" -Headers $paymentHeaders -ExpectedStatus @(200) -Body $payment
        Require-Equal ([string]$paymentAgain.id) ([string]$paymentResult.id) 'Gửi lại khoản thu không trả cùng chứng từ.'
    }

    $deliveryHeaders = @{ Authorization = "Bearer $token"; 'Idempotency-Key' = [guid]::NewGuid().ToString() }
    $delivered = Invoke-Api -Method POST -Path "/api/orders/$orderId/deliver" -Headers $deliveryHeaders -ExpectedStatus @(200)
    $deliveredAgain = Invoke-Api -Method POST -Path "/api/orders/$orderId/deliver" -Headers $deliveryHeaders -ExpectedStatus @(200)
    Require-Equal ([string]$delivered.status) 'Delivered' 'Đơn không chuyển sang Delivered.'
    Require-Equal ([string]$deliveredAgain.id) $orderId 'Gửi lại xác nhận giao không trả cùng đơn.'

    $balances = @(Invoke-Api -Method GET -Path "/api/inventory/balances?branchId=$branchId" -Headers $authHeaders)
    $materialBalance = $balances | Where-Object { [string]$_.id -eq $materialProductId } | Select-Object -First 1
    $bouquetBalance = $balances | Where-Object { [string]$_.id -eq $bouquetProductId } | Select-Object -First 1
    Require-True ($null -ne $materialBalance -and $null -ne $bouquetBalance) 'API không trả đủ tồn nguyên liệu và thành phẩm.'
    Require-Equal ([decimal]$materialBalance.quantity) ($receiptQuantity - $materialQuantity) 'Tồn nguyên liệu sau làm bó không đúng.'
    Require-Equal ([decimal]$bouquetBalance.quantity) 0 'Tồn bó thành phẩm sau giao không đúng.'

    $orderDetail = Invoke-Api -Method GET -Path "/api/orders/$orderId" -Headers $authHeaders
    Require-Equal ([string]$orderDetail.order.status) 'Delivered' 'Chi tiết đơn không ở trạng thái Delivered.'
    Require-Equal ([decimal]$orderDetail.paid) $orderTotal 'Tổng tiền đã thu không khớp tổng đơn.'
    Require-Equal ([decimal]$orderDetail.due) 0 'Đơn thử vẫn còn số tiền phải thu.'
    Require-Equal ([string]$orderDetail.order.items[0].skuSnapshot) $bouquetSku 'Snapshot SKU của đơn không giữ SKU bó.'
    Require-Equal @($orderDetail.payments).Count 2 'Đơn thử không có đúng hai khoản thanh toán.'

    # Khoảng thử bao trùm cả trường hợp test chạy qua nửa đêm giờ Việt Nam.
    $from = [DateTimeOffset]::UtcNow.AddHours(-12).ToString('o')
    $to = [DateTimeOffset]::UtcNow.AddHours(12).ToString('o')
    $fromEncoded = [uri]::EscapeDataString($from)
    $toEncoded = [uri]::EscapeDataString($to)
    $report = Invoke-Api -Method GET -Path "/api/reports/summary?branchId=$branchId&from=$fromEncoded&to=$toEncoded" -Headers $authHeaders
    Require-Equal ([decimal]$report.revenue) $orderTotal 'Doanh thu báo cáo không khớp đơn đã giao.'
    Require-Equal ([int]$report.deliveredOrders) 1 'Báo cáo không có đúng một đơn giao ở cửa hàng thử.'
    Require-Equal ([decimal]$report.payments.total) $orderTotal 'Tổng thu báo cáo không khớp các khoản thanh toán.'
    Require-Equal ([decimal]$report.payments.cash) $firstPayment 'Báo cáo tiền mặt không khớp khoản thu.'
    Require-Equal ([decimal]$report.payments.transfer) $secondPayment 'Báo cáo chuyển khoản không khớp khoản thu.'

    # Đọc lại các bảng PostgreSQL để chứng minh dữ liệu không chỉ tồn tại ở response API.
    $branchSql = SqlLiteral $branchId
    $materialSql = SqlLiteral $materialProductId
    $bouquetProductSql = SqlLiteral $bouquetProductId
    $bouquetIdSql = SqlLiteral $bouquetId
    $orderSql = SqlLiteral $orderId
    $codeSql = SqlLiteral $branchCode
    $skuSql = SqlLiteral $materialSku
    $bouquetSkuSql = SqlLiteral $bouquetSku
    $actorSql = SqlLiteral ([string]$login.user.id)
    $sqlChecks = @(
        @{ Name = 'branch'; Query = ('SELECT COUNT(*) FROM "Branches" WHERE "Id" = {0} AND "Code" = {1};' -f $branchSql, $codeSql); Expected = 1 },
        @{ Name = 'material'; Query = ('SELECT COUNT(*) FROM "Products" WHERE "Id" = {0} AND "Sku" = {1};' -f $materialSql, $skuSql); Expected = 1 },
        @{ Name = 'bouquet'; Query = ('SELECT COUNT(*) FROM "Bouquets" b JOIN "Products" p ON p."Id" = b."ProductId" WHERE b."Id" = {0} AND p."Sku" = {1} AND b."Status" = ''Sold'';' -f $bouquetIdSql, $bouquetSkuSql); Expected = 1 },
        @{ Name = 'material-balance'; Query = ('SELECT "Quantity" FROM "Balances" WHERE "BranchId" = {0} AND "ProductId" = {1};' -f $branchSql, $materialSql); Expected = ($receiptQuantity - $materialQuantity) },
        @{ Name = 'bouquet-balance'; Query = ('SELECT "Quantity" FROM "Balances" WHERE "BranchId" = {0} AND "ProductId" = {1};' -f $branchSql, $bouquetProductSql); Expected = 0 },
        @{ Name = 'order'; Query = ('SELECT COUNT(*) FROM "Orders" WHERE "Id" = {0} AND "Status" = ''Delivered'';' -f $orderSql); Expected = 1 },
        @{ Name = 'payments'; Query = ('SELECT COUNT(*) FROM "Payments" WHERE "OrderId" = {0};' -f $orderSql); Expected = 2 },
        @{ Name = 'payments-total'; Query = ('SELECT COALESCE(SUM("Amount"),0) FROM "Payments" WHERE "OrderId" = {0};' -f $orderSql); Expected = $orderTotal },
        @{ Name = 'commands'; Query = ('SELECT COUNT(*) FROM "CommandReceipts" WHERE "BranchId" = {0};' -f $branchSql); Expected = 6 },
        @{ Name = 'inventory-history'; Query = ('SELECT COUNT(*) FROM "InventoryTransactions" WHERE "BranchId" = {0} AND "ProductId" IN ({1}, {2}) AND "Type" IN (''Receipt'',''BouquetConsumption'',''BouquetOutput'',''Sale'');' -f $branchSql, $materialSql, $bouquetProductSql); Expected = 4 },
        @{ Name = 'audit'; Query = ('SELECT COUNT(DISTINCT "Action") FROM "AuditLogs" WHERE "BranchId" = {0} AND "ActorId" = {1} AND "Action" IN (''CreateBranch'',''ReceiveStock'',''AssembleBouquet'',''CreateOrder'',''Payment'',''DeliverOrder'');' -f $branchSql, $actorSql); Expected = 6 }
    )
    foreach ($check in $sqlChecks) {
        $actualRows = @(Invoke-DbQuery $check.Query)
        Require-True ($actualRows.Count -eq 1) "Truy vấn DB '$($check.Name)' không trả đúng một dòng."
        Require-Equal ([decimal]$actualRows[0]) ([decimal]$check.Expected) "Đối chiếu PostgreSQL '$($check.Name)' thất bại."
    }

    Write-Host ''
    Write-Host 'PASS: API thật và PostgreSQL Compose đã chạy toàn bộ chuỗi.' -ForegroundColor Green
    Write-Host "  Run:          $runId"
    Write-Host "  Cửa hàng:     $branchCode ($branchId)"
    Write-Host "  Nguyên liệu:  $materialSku ($materialProductId)"
    Write-Host "  Bó SKU:       $bouquetSku ($bouquetId)"
    Write-Host "  Đơn:          $orderCode ($orderId)"
    Write-Host 'Dữ liệu kiểm thử được giữ lại trong database; không xóa volume hoặc bản ghi.'
}
finally {
    # Thu hồi phiên thử; không ảnh hưởng tài khoản, cửa hàng hay dữ liệu giao dịch.
    if (-not [string]::IsNullOrWhiteSpace($token)) {
        try { Invoke-Api -Method POST -Path '/api/auth/logout' -Headers @{ Authorization = "Bearer $token" } -ExpectedStatus @(204) | Out-Null } catch { }
    }
}
