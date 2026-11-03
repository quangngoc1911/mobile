# Hướng dẫn tự cài và chạy ứng dụng quản lý cửa hàng hoa

Tài liệu này dành cho người tự cài dự án trên **máy Windows mới**, không cần agent. Làm lần lượt các bước từ 1 đến 7 để chạy PostgreSQL, API có Scalar và giao diện thử trên trình duyệt bằng Docker Compose. Những lệnh bên dưới chạy trong **PowerShell 7 trên máy Windows**, không nhập vào terminal của container.

Giao diện trong Compose là **Expo Web** để thử nghiệp vụ từ máy tính. Ứng dụng gọi API thật; API đọc và ghi PostgreSQL thật. Nó không phải APK Android, không phải giả lập điện thoại và chưa chứng minh ứng dụng hoạt động trên Android thật. Cấu hình Compose này chỉ dùng thử local, không dùng làm môi trường production.

> **Trước khi clone:** tại thời điểm viết hướng dẫn, thư mục gốc trên máy xây dựng chưa được khởi tạo Git và `mobile/` có thư mục `.git` riêng. Chưa có URL repository để xác minh bản clone. Người tạo repository cần bảo đảm toàn bộ mã ở `mobile/`, `src/`, `scripts/`, `tests/` và các file cấu hình đi cùng repository. Tài liệu này không tự tạo, commit hoặc push repository.

## 1. Cài công cụ trên máy mới

1. Cài [Git for Windows](https://git-scm.com/download/win) để clone repository.
2. Cài [Docker Desktop cho Windows](https://docs.docker.com/desktop/setup/install/windows-install/), đáp ứng yêu cầu ảo hóa/WSL 2 theo hướng dẫn của Docker. Mở Docker Desktop và chờ engine chạy xong.
3. Cài [PowerShell 7](https://learn.microsoft.com/powershell/scripting/install/installing-powershell-on-windows) và mở ứng dụng **PowerShell 7** hoặc Windows Terminal với profile **PowerShell**, không dùng Windows PowerShell 5.1 cho hướng dẫn này.
4. Cài **.NET SDK 10**, không chỉ cài Runtime, từ [trang tải .NET 10](https://dotnet.microsoft.com/download/dotnet/10.0). SDK trên máy host cần cho migration và tạo tài khoản đầu tiên.
5. Chỉ khi muốn chạy kiểm thử giao diện hoặc sửa mobile trên host: cài [Node.js](https://nodejs.org/) có `npm` và Chrome hoặc Edge. Máy xây dựng đã dùng Node.js 24; Docker tự dùng Node 24 để build web nên không cần Node trên host chỉ để mở web.

Sau khi cài, đóng terminal cũ và mở lại **PowerShell 7**, chạy:

```powershell
$PSVersionTable.PSVersion
git --version
docker --version
docker compose version
docker info --format '{{.ServerVersion}}'
dotnet --list-sdks
```

**Kết quả mong đợi:** PowerShell có major version `7`; các lệnh còn lại in phiên bản và danh sách SDK có dòng `10.0.xxx`. Nếu `docker info` báo không kết nối được daemon, mở Docker Desktop và chờ nó chạy. Nếu chạy kiểm thử UI, kiểm tra thêm `node --version` và `npm --version`.

Lần đầu build cần Internet để tải image Docker và package NuGet/npm. Nếu công ty dùng proxy hoặc chặn registry, cần cấu hình quyền truy cập mạng tương ứng trước khi tiếp tục.

## 2. Clone và kiểm tra nội dung

Trên trang repository, sao chép URL clone mà bạn được cấp quyền. Trong PowerShell 7, thay `DAN_URL_REPOSITORY_VAO_DAY` bằng URL đó:

```powershell
New-Item -ItemType Directory -Force -Path "$env:USERPROFILE\source" | Out-Null
Set-Location "$env:USERPROFILE\source"
git clone 'DAN_URL_REPOSITORY_VAO_DAY' Quan-ly-cua-hang-hoa
Set-Location "$env:USERPROFILE\source\Quan-ly-cua-hang-hoa"
```

Nếu bạn chọn thư mục khác, chuyển tới **thư mục gốc bản clone**: nơi có đồng thời `docker-compose.yml`, `FlowerShop.slnx` và `README.md`. Các lệnh sau trong tài liệu đều giả định terminal đang ở thư mục đó, trừ khi nói rõ phải vào `mobile/`.

Kiểm tra nhanh các thành phần tối thiểu:

```powershell
@(
    'docker-compose.yml',
    'FlowerShop.slnx',
    'scripts/Initialize-LocalSecrets.ps1',
    'scripts/Use-LocalConfig.ps1',
    'scripts/Generate-MigrationSql.ps1',
    'scripts/Bootstrap-LocalAdmin.ps1',
    'scripts/Test-ComposeLiveWorkflow.ps1',
    'src/FlowerShop.Api/Dockerfile',
    'src/FlowerShop.Infrastructure/FlowerShop.Infrastructure.csproj',
    'src/FlowerShop.Infrastructure/Persistence/Migrations',
    'tests/FlowerShop.Tests/FlowerShop.Tests.csproj',
    'mobile/Dockerfile.web',
    'mobile/nginx.web.conf',
    'mobile/package.json',
    'mobile/package-lock.json',
    'mobile/src',
    'mobile/tests/live-ui.test.mjs'
) | ForEach-Object {
    '{0,-70} {1}' -f $_, (Test-Path -LiteralPath $_)
}
```

**Kết quả mong đợi:** tất cả dòng đều là `True`. Nếu có `False`, dừng tại đây và kiểm tra nội dung repository/submodule với người cung cấp mã; không thể build đầy đủ từ bản clone thiếu file.

## 3. Tạo cấu hình bí mật cho máy này

Mỗi máy tạo `.env` riêng ở thư mục gốc. Bản clone không chứa `.env`, mật khẩu, database, ảnh và các file build trong `artifacts/` vì chúng bị loại khỏi source. Tài khoản trên máy cũ cũng **không tự xuất hiện trên máy mới**.

Nếu PowerShell chặn script do execution policy, cho phép script local trong **phiên PowerShell hiện tại**, rồi tiếp tục:

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy RemoteSigned
```

Nếu policy của tổ chức ghi đè và vẫn chặn script, kiểm tra `Get-ExecutionPolicy -List` và xử lý theo chính sách của máy.

Từ thư mục gốc bản clone, chạy **một lần**:

```powershell
.\scripts\Initialize-LocalSecrets.ps1
```

**Kết quả mong đợi:** thấy thông báo đã tạo `.env`. Script tự sinh mật khẩu PostgreSQL và khóa JWT ngẫu nhiên. Nếu `.env` đã tồn tại, script dừng và không ghi đè; kiểm tra vì sao file đã tồn tại trước khi tiếp tục. Không gửi, commit hoặc chép nội dung `.env` vào log.

Cấu hình mặc định:

| Thành phần | Địa chỉ trên chính máy Windows | Mục đích |
| --- | --- | --- |
| PostgreSQL | `127.0.0.1:25432` | Database Compose |
| API | `http://127.0.0.1:5284` | Backend và Scalar Development |
| Mobile Web | `http://127.0.0.1:8085` | Giao diện thử trên trình duyệt |

Các cổng chỉ bind `127.0.0.1`: điện thoại hoặc máy khác trong LAN không truy cập được trực tiếp. Không cần sửa `.env.example` hoặc tự nhập mật khẩu PostgreSQL vào source. Xem mục **Xử lý lỗi** nếu một cổng đã được dùng.

> Giữ nguyên `.env` cùng dữ liệu của máy. Đổi `FLOWERSHOP_DB_PASSWORD` trong file không tự đổi mật khẩu bên trong PostgreSQL đã khởi tạo; đổi khóa JWT làm các token cũ không còn hợp lệ. Không xóa volume hoặc tạo lại `.env` để xử lý lỗi đăng nhập.

## 4. Khởi động PostgreSQL và áp dụng migration

### 4.1. Kiểm tra project Compose và khởi động database

Các lệnh sau không in nội dung `.env`:

```powershell
docker compose config --quiet
docker compose config --format json | ConvertFrom-Json | ForEach-Object { $_.volumes.'flowershop-postgres'.name }
docker compose ps -a
docker volume ls --format '{{.Name}}'
```

`config --quiet` thành công thì không in gì và không báo lỗi. Dòng thứ hai chỉ in tên volume Docker thực tế; so tên đó với danh sách ở dòng cuối. Nếu volume hoặc container của cùng project đã tồn tại, có thể máy đã có dữ liệu cũ: xác định chủ sở hữu, `.env` và dữ liệu trước khi chạy tiếp. Tên project/volume mặc định phụ thuộc tên thư mục Compose; hai bản clone có thể trùng tên project. Không xóa volume để làm mới.

Nếu database thuộc project này và được phép sử dụng, chạy:

```powershell
New-Item -ItemType Directory -Force -Path .\data\storage | Out-Null
docker compose up -d --wait postgres
docker compose ps
```

**Kết quả mong đợi:** service `postgres` có trạng thái `Up` và `healthy`. Database được lưu trong Docker volume; ảnh được lưu dưới `data/storage/` trên máy host.

### 4.2. Cài công cụ EF Core

EF Core CLI cần để sinh SQL và cập nhật schema. Đoạn sau cài phiên bản `10.0.12` vào thư mục công cụ riêng của dự án dưới tài khoản Windows; nó không cài package vào source. Chạy trong PowerShell 7:

```powershell
$flowerTools = Join-Path $env:LOCALAPPDATA 'FlowerShop\tools'
if (-not (Test-Path -LiteralPath (Join-Path $flowerTools 'dotnet-ef.exe'))) {
    dotnet tool install dotnet-ef --tool-path $flowerTools --version 10.0.12
}
$env:PATH = "$flowerTools;$env:PATH"
dotnet ef --version
```

**Kết quả mong đợi:** `Entity Framework Core .NET Command-line Tools` và phiên bản `10.0.12`. Nếu đã có phiên bản khác trong chính thư mục này, kiểm tra trước khi thay; có thể cập nhật bằng `dotnet tool update dotnet-ef --tool-path $flowerTools --version 10.0.12`. Sau khi mở PowerShell mới, chạy lại dòng gán `$flowerTools` và dòng thêm vào `PATH` trước các lệnh `dotnet ef`. Nếu cài tool lỗi, kiểm tra SDK 10 và kết nối NuGet.

### 4.3. Xem SQL rồi cập nhật schema

Các lệnh ở mục này chạy từ **thư mục gốc bản clone**. Chỉ áp dụng migration khi đã xác nhận đúng database và đã có bản sao lưu phù hợp nếu database không trống.

```powershell
. .\scripts\Use-LocalConfig.ps1
dotnet restore FlowerShop.slnx
if ($LASTEXITCODE -ne 0) { throw 'Không thể tải package NuGet; kiểm tra kết nối và chạy lại dotnet restore.' }
.\scripts\Generate-MigrationSql.ps1
```

Lưu ý có dấu chấm và dấu cách ở đầu dòng `. .\scripts\Use-LocalConfig.ps1`: lệnh này nạp cấu hình kết nối vào **phiên PowerShell hiện tại**. `dotnet restore` tải package cần thiết cho bản clone mới; nếu lỗi hãy dừng và kiểm tra kết nối NuGet. Script tiếp theo chỉ sinh SQL để xem, chưa thay đổi database. Nó in đường dẫn file SQL mới dưới `artifacts/migrations/`. Mở file vừa tạo bằng Notepad:

```powershell
$migrationFile = Get-ChildItem .\artifacts\migrations -Filter '*.sql' |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
notepad $migrationFile.FullName
```

Sau khi xem SQL và xác nhận áp dụng vào đúng database:

```powershell
dotnet ef database update --project src/FlowerShop.Infrastructure --startup-project src/FlowerShop.Infrastructure
dotnet ef migrations list --project src/FlowerShop.Infrastructure --startup-project src/FlowerShop.Infrastructure
```

**Kết quả mong đợi:** lệnh update hoàn tất không lỗi; danh sách migration hiện ra và không có migration nào ghi `(Pending)`. Bản mã hiện tại có ba migration. Nếu mở PowerShell mới, trước hai lệnh này cần vào lại thư mục gốc, thêm EF tool vào `PATH` và chạy lại `. .\scripts\Use-LocalConfig.ps1`.

## 5. Tạo tài khoản quản trị đầu tiên

Sau khi migration thành công, chạy tại thư mục gốc:

```powershell
.\scripts\Bootstrap-LocalAdmin.ps1
```

Khi được hỏi, tự nhập:

1. **Tên đăng nhập quản trị đầu tiên**: tên bạn chọn, ví dụ `admin`.
2. **Họ tên**: tên hiển thị của người quản trị.
3. **Mật khẩu**: tự chọn ít nhất 12 ký tự. Khi nhập, màn hình không hiện ký tự; nhập xong nhấn Enter.

**Kết quả mong đợi:** thấy `Đã tạo tài khoản quản trị đầu tiên.` Tài khoản này có cả vai trò `Admin` và `Owner`; cần chọn vai trò khi đăng nhập. Đây không phải mật khẩu mặc định; hãy lưu mật khẩu trong công cụ quản lý mật khẩu của bạn. Script không ghi mật khẩu vào `.env`.

Bootstrap chỉ chạy khi bảng người dùng còn trống; nếu báo database đã có tài khoản, dừng và xác định database đang dùng. Không chạy lại bootstrap để đặt lại mật khẩu. Tài khoản quản trị có quyền phù hợp có thể đặt lại mật khẩu cho tài khoản được phép quản lý trong ứng dụng.

## 6. Build và mở ứng dụng

Ở thư mục gốc, sau khi đã áp dụng migration và tạo tài khoản:

```powershell
docker compose up -d --build api mobile-web
docker compose ps
```

Lần build đầu có thể mất thời gian vì tải image và package. **Kết quả mong đợi:** ba service `postgres`, `api`, `mobile-web` ở trạng thái `Up`; riêng PostgreSQL báo `healthy`. Trạng thái `Up` chưa đủ chứng minh nghiệp vụ hoạt động, vì vậy chạy tiếp:

```powershell
(Invoke-WebRequest 'http://127.0.0.1:5284/health').Content
(Invoke-WebRequest 'http://127.0.0.1:8085/backend/health').Content
```

Cả hai cần trả `{"status":"ok"}`. Ngay sau khi Compose báo `Up`, API có thể vẫn đang khởi động; nếu gặp lỗi kết nối, chờ vài giây rồi chạy lại. Nếu vẫn lỗi, xem log theo mục 9. Địa chỉ thứ hai kiểm tra đường đi từ web proxy tới API và database; health không thay thế kiểm thử nghiệp vụ hay kiểm tra migration.

Mở trên **máy đang chạy Docker**:

- Giao diện: [http://127.0.0.1:8085](http://127.0.0.1:8085). Chọn `Owner`, đăng nhập bằng tài khoản vừa tạo.
- Scalar: [http://127.0.0.1:5284/scalar/v1](http://127.0.0.1:5284/scalar/v1).
- OpenAPI: [http://127.0.0.1:5284/openapi/v1.json](http://127.0.0.1:5284/openapi/v1.json).

Sau đăng nhập, có thể vào **Tài khoản → Cửa hàng** để tạo cửa hàng, sau đó tạo sản phẩm, nhập kho và tạo đơn. Database mới ban đầu trống: sản phẩm, cửa hàng và đơn thử từ máy xây dựng không tự chuyển sang.

**Dữ liệu dùng chung:** tất cả thao tác lưu trên web ghi vào database Compose của máy này. Web chỉ dùng dữ liệu thử; phiên dùng `sessionStorage`, còn giao dịch chưa xác nhận có thể ở `localStorage` không mã hóa. Khi đã thử xong nên đăng xuất. Ảnh bảo vệ bằng token trên web hiện chỉ hiển thị metadata.

### Thử API bằng Scalar

1. Mở Scalar → nhóm **Auth** → `POST /api/auth/login` → **Test Request**.
2. Nhập `username` và `password` đã tạo, `activeRole` là `Owner` hoặc `Admin`; gửi request.
3. Sao chép riêng `tokens.accessToken` từ kết quả. Vào **Introduction → Authentication → Select Auth Type → Bearer**, dán token vào ô **Bearer Token**.
4. Chọn endpoint cần thử và gửi request. Token gắn với vai trò đã chọn; dùng token phù hợp khi đổi vai trò. Không chia sẻ mật khẩu, token hoặc response đăng nhập.

Scalar/OpenAPI chỉ được bật khi API chạy môi trường Development như trong Compose local.

## 7. Kiểm tra dữ liệu thật và chạy kiểm thử

### 7.1. Kiểm tra nhanh trong PostgreSQL

Tại thư mục gốc, chạy hai câu truy vấn chỉ đọc sau. Kết quả phụ thuộc những gì bạn đã tạo; database mới chưa có đơn nên số đơn có thể bằng 0.

```powershell
'SELECT COUNT(*) AS so_tai_khoan FROM "Users";' |
    docker compose exec -T postgres psql -v ON_ERROR_STOP=1 -U flowershop -d flowershop
'SELECT COUNT(*) AS so_don_hang FROM "Orders";' |
    docker compose exec -T postgres psql -v ON_ERROR_STOP=1 -U flowershop -d flowershop
```

Sau bootstrap thành công, số tài khoản ít nhất là 1. Hai lệnh này không tạo hoặc xóa dữ liệu. Để kiểm tra đầy đủ đường đi UI/API/database, dùng kiểm thử live bên dưới.

### 7.2. Chuẩn bị kiểm thử UI

Hai kiểm thử live tạo thêm cửa hàng và giao dịch thử **trong database hiện tại**, giữ nguyên dữ liệu sau khi chạy, kể cả khi dừng giữa chừng. Chỉ chạy trên database thử. Mỗi lượt có mã cửa hàng và đơn riêng; không dùng response API giả.

Chỉ cần làm bước sau nếu chạy kiểm thử UI. Tại thư mục gốc, dùng Node.js 24 và `npm` trên host:

```powershell
node --version
npm --version
Push-Location .\mobile
try {
    npm ci
    if ($LASTEXITCODE -ne 0) { throw 'npm ci thất bại; kiểm tra Node.js và kết nối npm.' }
}
finally { Pop-Location }
```

`npm ci` cài đúng dependency theo `mobile/package-lock.json` và thay thế `mobile/node_modules` nếu thư mục đó đã có; trên bản clone mới đây là thư mục được tạo phục vụ kiểm thử. Test tự tìm Chrome hoặc Edge tại vị trí Windows thông dụng. Nếu trình duyệt cài ở nơi khác, đặt `$env:FLOWERSHOP_TEST_BROWSER` thành đường dẫn đầy đủ tới `chrome.exe` hoặc `msedge.exe` trước khi chạy.

### 7.3. Chạy hai kiểm thử live

Đảm bảo ba container đang chạy và bạn nhớ tên đăng nhập/mật khẩu của một tài khoản đang hoạt động có **cả Admin và Owner**. Tại thư mục gốc, chạy cả khối sau; mật khẩu được nhập ẩn, không đặt trực tiếp vào command hoặc file:

```powershell
$flowerTestUser = Read-Host 'Tên đăng nhập Admin và Owner'
$flowerTestPassword = Read-Host 'Mật khẩu của tài khoản này' -AsSecureString
try {
    $env:FLOWERSHOP_LOCAL_ADMIN_PASSWORD = [System.Net.NetworkCredential]::new('', $flowerTestPassword).Password
    $env:FLOWERSHOP_LIVE_USERNAME = $flowerTestUser

    .\scripts\Test-ComposeLiveWorkflow.ps1 -AdminUsername $flowerTestUser

    Push-Location .\mobile
    try {
        npm run test:live-ui
        if ($LASTEXITCODE -ne 0) { throw 'Kiểm thử UI live thất bại.' }
    }
    finally { Pop-Location }
}
finally {
    Remove-Item Env:\FLOWERSHOP_LOCAL_ADMIN_PASSWORD, Env:\FLOWERSHOP_LIVE_USERNAME -ErrorAction SilentlyContinue
    $flowerTestPassword.Dispose()
}
```

**Kết quả mong đợi:**

- Script API in `PASS: API thật và PostgreSQL Compose đã chạy toàn bộ chuỗi`, kèm mã cửa hàng/đơn/SKU. Nó kiểm tra nhập kho → làm bó → tạo đơn → hai khoản thu → giao → báo cáo, gửi lặp một số lệnh bằng cùng `Idempotency-Key` và đối chiếu PostgreSQL.
- Test UI in `PASS: UI → API Compose → PostgreSQL`, kèm mã cửa hàng/đơn/SKU. Nó thao tác trên trình duyệt ở kích thước điện thoại và đối chiếu lại tồn, đơn, thu tiền, lịch sử và audit trong PostgreSQL.
- Nếu một test báo `FAIL`, đọc bước lỗi được in, kiểm tra cửa hàng thử đã tạo trước khi chạy lại. Một lượt chạy mới tạo thêm dữ liệu mới, không tự dọn lượt cũ.

Biến môi trường trong khối chỉ được cấp cho phiên PowerShell và tiến trình con trong lúc chạy, sau đó bị xóa khỏi phiên. Nếu mật khẩu tài khoản thay đổi, dùng mật khẩu hiện tại khi được hỏi.

### 7.4. Kiểm thử tự động khác (tùy chọn)

Để chạy kiểm thử backend từ thư mục gốc, PostgreSQL Compose phải đang chạy:

```powershell
. .\scripts\Use-LocalConfig.ps1
dotnet test FlowerShop.slnx
```

Bộ test dùng PostgreSQL của project để tạo một database kiểm thử tên ngẫu nhiên, chạy migration và test, sau đó xóa database kiểm thử do nó tạo. Nó không dùng database nghiệp vụ `flowershop` làm database test. Không trỏ cấu hình kiểm thử tới PostgreSQL production; bài test vẫn tiêu thụ tài nguyên trên server đã kết nối.

Để kiểm tra mã mobile trên host sau khi đã `npm ci` tại mục 7.2:

```powershell
Push-Location .\mobile
try {
    npm run lint
    npm run typecheck
    npm run test:mobile
}
finally { Pop-Location }
```

## 8. Dùng lại sau khi khởi động lại máy

Mở Docker Desktop. Mở PowerShell 7, vào thư mục gốc bản clone và chạy:

```powershell
Set-Location "$env:USERPROFILE\source\Quan-ly-cua-hang-hoa"
docker compose up -d
docker compose ps
```

Nếu đã clone vào nơi khác, thay đường dẫn `Set-Location` tương ứng. Mở lại web và Scalar theo mục 6. Không chạy lại bootstrap mỗi lần mở máy. Khi không sử dụng, có thể dừng các service của project mà vẫn giữ dữ liệu:

```powershell
docker compose stop
```

Sau khi thay mã API hoặc mobile và muốn build lại image tương ứng:

```powershell
docker compose up -d --build --no-deps api
docker compose up -d --build --no-deps mobile-web
```

Các lệnh này có thể ngắt kết nối người đang dùng service tương ứng. Nếu có migration mới, sao lưu và xem xét migration trước theo mục 4.3; build image không tự áp dụng migration. **Không dùng `docker compose down --volumes`** khi cần giữ database.

## 9. Xử lý lỗi thường gặp

| Hiện tượng | Cách kiểm tra và xử lý |
| --- | --- |
| `docker` không tìm thấy hoặc không kết nối daemon | Kiểm tra Docker Desktop đã cài, đang mở và engine chạy; mở PowerShell mới rồi chạy lại `docker info --format '{{.ServerVersion}}'`. |
| `dotnet ef` không tìm thấy | Cài SDK 10 và EF tool ở mục 4.2; thêm lại thư mục tool vào `PATH` trong phiên PowerShell hiện tại. |
| `NETSDK1004` hoặc thiếu `project.assets.json` khi sinh migration | Tại thư mục gốc, chạy `dotnet restore FlowerShop.slnx`, kiểm tra restore thành công rồi lặp lại mục 4.3. |
| Script bị chặn | Kiểm tra execution policy và làm theo mục 3; không bỏ qua policy do tổ chức quản lý. |
| `.env đã tồn tại` | Không xóa hay ghi đè. Kiểm tra đây có phải cấu hình của database/volume đã dùng trước đó không. |
| Cổng 25432, 5284 hoặc 8085 đã được dùng | Chạy `Get-NetTCPConnection -State Listen -LocalPort 25432,5284,8085 -ErrorAction SilentlyContinue`, xác định tiến trình sở hữu trước. Có thể đổi cổng trong `.env` như hướng dẫn phía dưới. |
| `postgres` không healthy hoặc API không lên | Chạy `docker compose ps`, `docker compose logs --tail=100 postgres`, `docker compose logs --tail=100 api`; kiểm tra cổng, cấu hình và migration. Không chia sẻ log có secret hoặc dữ liệu khách. |
| Web mở nhưng báo không kết nối API | Mở `http://127.0.0.1:8085/backend/health`; xem `docker compose logs --tail=100 mobile-web` và log API. Nếu đổi cổng web, thay số cổng trong URL. |
| Báo thiếu bảng hoặc migration pending | Xác nhận đúng database, nạp lại cấu hình bằng `. .\scripts\Use-LocalConfig.ps1`, xem SQL rồi áp dụng migration theo mục 4.3. |
| Bootstrap báo database đã có tài khoản | Database không trống. Dừng, xác định tài khoản và quyền quản trị hiện có; bootstrap không đặt lại mật khẩu. |
| Đăng nhập thất bại | Kiểm tra tên, mật khẩu, vai trò đã chọn và API health. Tài khoản vừa bootstrap do chính bạn chọn; không có mật khẩu mặc định cho bản clone. |
| Kiểm thử UI không tìm thấy trình duyệt | Cài Chrome/Edge hoặc đặt `FLOWERSHOP_TEST_BROWSER` tới file trình duyệt, rồi chạy lại mục 7.3. |
| Test live báo thiếu mật khẩu | Chạy cả khối ở mục 7.3 và nhập lại mật khẩu hiện tại. `Initialize-LocalSecrets.ps1` không tạo biến mật khẩu đăng nhập. |

Đổi cổng khi cần: mở `.env` bằng Notepad trên máy local, giữ nguyên các secret, sửa `FLOWERSHOP_DB_PORT` hoặc thêm/sửa hai dòng cổng còn lại, ví dụ:

```text
FLOWERSHOP_DB_PORT=25433
FLOWERSHOP_API_PORT=5285
FLOWERSHOP_MOBILE_WEB_PORT=8086
```

Lưu file, sau đó chạy `docker compose up -d --wait postgres` và `docker compose up -d api mobile-web` từ thư mục gốc để Compose áp dụng cổng mới. Việc tạo lại service có thể làm gián đoạn người đang sử dụng. Dùng cổng mới trong URL; chạy lại `. .\scripts\Use-LocalConfig.ps1` trong phiên PowerShell trước lệnh migration. Đừng sửa `FLOWERSHOP_DB_PASSWORD` chỉ để đổi cổng. Biến môi trường cùng tên trong shell có thể được Compose ưu tiên hơn `.env`; khi gặp cấu hình không khớp, kiểm tra tên biến đang đặt bằng `Get-ChildItem Env:FLOWERSHOP_* | Select-Object Name` mà không in giá trị.

## 10. Sao lưu local và giới hạn vận hành

Database nằm trong Docker volume; file ảnh nằm trong `data/storage/`. Sao chép mã nguồn hoặc clone repository **không sao chép hai phần dữ liệu này**. Giữ file `.env` an toàn; không đưa file đó lên repository.

Để tạo bản sao local khi cần, thông báo cho người dùng và dừng **mọi nguồn ghi** vào API, PostgreSQL và thư mục ảnh. Ví dụ nếu không có tiến trình ghi nào khác, tại thư mục gốc:

```powershell
docker compose stop mobile-web api
.\scripts\Backup-Local.ps1 -WritesStopped
```

Script tạo database dump và archive ảnh dưới `artifacts/backups/`; tham số `-WritesStopped` là xác nhận của người vận hành, script không tự chứng minh mọi nguồn ghi đã dừng. Giữ PostgreSQL chạy trong lúc backup. Sau khi kiểm tra backup thành công, mở lại API và web bằng `docker compose up -d api mobile-web`. Nếu backup lỗi, kiểm tra kết quả trước khi mở lại thao tác ghi.

Để diễn tập kiểm tra một bản backup trong `artifacts/backups/`, dùng đường dẫn thư mục thực tế script vừa in:

```powershell
.\scripts\Test-LocalRestore.ps1 -BackupDirectory 'DAN_DUONG_DAN_THU_MUC_BACKUP_VAO_DAY'
```

Script khôi phục vào database kiểm thử mới có tên ngẫu nhiên, đối chiếu metadata/checksum ảnh rồi xóa **database kiểm thử do chính script tạo**; nó không thay database nguồn. Nếu backup không có ảnh, kết quả ảnh là `NOT VERIFIED`. Bản backup local không phải backup độc lập: trước khi dùng dữ liệu thật cần chọn nơi lưu ngoài máy/VPS, mã hóa và kiểm tra khôi phục thành công. Các file `deploy/` chỉ là chuẩn bị VPS; production và khôi phục production chưa được nghiệm thu.

## 11. Giới hạn hiện tại và kết quả trên máy xây dựng

- Cấu hình trên dành cho Windows + Docker Desktop và truy cập từ chính máy host. Chưa có hướng dẫn đã kiểm chứng để máy khác hoặc điện thoại kết nối qua LAN.
- Ngày 03/11/2026, quy trình tạo `.env`, `dotnet restore`, sinh và áp dụng migration, bootstrap, build API/web, khởi động ba service và kiểm tra hai đường dẫn health đã **PASS trên một bản sao mã sạch ở máy xây dựng**, dùng cổng riêng và PostgreSQL tạm. Cần chờ API khởi động xong trước khi gọi health. Việc clone từ repository thực và cài trên máy mới vẫn **NOT VERIFIED**; bản sao kiểm thử dùng cache package/image sẵn có trên máy xây dựng.
- Thư mục `mobile/` là ứng dụng Expo. APK thử từng được build trong `artifacts/` trên máy xây dựng, nhưng `artifacts/` không đi theo bản clone. Không coi Docker Expo Web, bundle hoặc APK build thành công là nghiệm thu Android; cài APK, camera/thư viện ảnh, SecureStore và kết nối API trên thiết bị thật vẫn **NOT VERIFIED**.
- Địa chỉ `EXPO_PUBLIC_API_URL` được đóng vào bundle mobile; giá trị `127.0.0.1` trên điện thoại chỉ chính điện thoại. Cấu hình API HTTPS/USB, tạo APK và nghiệm thu trên điện thoại cần làm riêng; không dùng secret trong biến này.
- Kiểm thử trên máy xây dựng ngày 03/11/2026: luồng API thật và PostgreSQL **PASS** với đơn `T3E25A369-001`; luồng giao diện web thật **PASS** với đơn `U6E7B6133-001`. Đây là dữ liệu của máy xây dựng, không phải dữ liệu mặc định sẽ có trên máy mới. Lint, typecheck và 7 kiểm thử client đã PASS tại máy đó; phải chạy lại trên máy mới để có kết quả của máy mới.
- Sáu cửa hàng thử dở trên máy xây dựng (`UA23C3F93`, `U9DA4484C`, `UDD2A3FB4`, `U0B249FAA`, `UE09F5594`, `U3A74B5E2`) được giữ lại, không tính là PASS.
- Backend đã có kiểm thử tích hợp đối với quyền, kho, đơn, tiền, transaction, đồng thời và ảnh; không đồng nghĩa mọi màn hình và vai trò đã được nghiệm thu trên thiết bị thật. Scalar từng được thử health và đăng nhập, chưa bấm thủ công toàn bộ endpoint.
- Doanh thu tính theo đơn đã giao; khoản hoàn theo ngày hoàn. Hủy đơn sau giao chưa tự điều chỉnh doanh thu trong MVP, cần đối chiếu thủ công. Hoa được theo dõi tồn tổng theo SKU/cửa hàng, chưa có lô/FIFO; nguyên liệu trừ khi làm bó xong, thành phẩm trừ khi giao.
- Các điểm nghiệp vụ chưa chốt: quy tắc làm tròn tiền nhiều hơn hai chữ số thập phân; hạn mức giảm giá của Store; có cho khóa/thu hồi Admin hoặc Owner cuối cùng không; phạm vi nghiệp vụ của Admin. Các quyết định production về backup, hostname API, mạng và Android cũng cần chốt riêng.
