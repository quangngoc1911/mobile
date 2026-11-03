using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using FlowerShop.Infrastructure.Storage;
using FlowerShop.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace FlowerShop.Tests;

// Tạo database có tên ngẫu nhiên của riêng bài kiểm thử; không dùng dữ liệu nghiệp vụ local.
public sealed class PostgresWorkflowTests
{
    [Fact]
    public async Task Business_flow_is_transactional_scoped_and_replay_safe()
    {
        var baseConnection = Environment.GetEnvironmentVariable("FLOWERSHOP_CONNECTION_STRING");
        Assert.False(string.IsNullOrWhiteSpace(baseConnection), "Chạy . ./scripts/Use-LocalConfig.ps1 trước dotnet test.");
        var admin = new NpgsqlConnectionStringBuilder(baseConnection) { Database = "postgres" };
        var databaseName = "flowershop_test_" + Guid.NewGuid().ToString("N");
        var testDb = new NpgsqlConnectionStringBuilder(baseConnection) { Database = databaseName };
        var oldConnection = Environment.GetEnvironmentVariable("FLOWERSHOP_CONNECTION_STRING");
        var oldJwt = Environment.GetEnvironmentVariable("Jwt__SigningKey");
        var oldStorage = Environment.GetEnvironmentVariable("Storage__RootPath");
        var testStorage = Path.Combine(Path.GetTempPath(), "flowershop-test-" + Guid.NewGuid().ToString("N"));
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await using (var connection = new NpgsqlConnection(admin.ConnectionString))
        {
            await connection.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", connection);
            await create.ExecuteNonQueryAsync();
        }
        try
        {
            Environment.SetEnvironmentVariable("FLOWERSHOP_CONNECTION_STRING", testDb.ConnectionString);
            Environment.SetEnvironmentVariable("Jwt__SigningKey", Convert.ToHexString(RandomNumberGenerator.GetBytes(48)));
            Environment.SetEnvironmentVariable("Storage__RootPath", testStorage);
            using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
            using (var scope = factory.Services.CreateScope())
            {
                await scope.ServiceProvider.GetRequiredService<FlowerShopDbContext>().Database.MigrateAsync();
            }
            await DatabaseInitializer.InitializeFirstAccountAsync(factory.Services, "owner", password, "Owner Test");
            using var owner = factory.CreateClient();
            var adminToken = await Login(owner, "owner", password, "Admin");
            var ownerToken = await Login(owner, "owner", password, "Owner");
            using var adminClient = factory.CreateClient();
            Authenticate(adminClient, adminToken);
            Authenticate(owner, ownerToken);
            var adminInfo = await Get(adminClient, "/api/auth/me");
            var ownerId = adminInfo.GetProperty("user").GetProperty("id").GetGuid();
            await Expect(await PutRaw(adminClient, $"/api/accounts/{ownerId}/active", new { isActive = false }), HttpStatusCode.Conflict);
            await Expect(await PutRaw(adminClient, $"/api/accounts/{ownerId}/roles", new { roles = new[] { "Owner" } }), HttpStatusCode.Conflict);
            // Một refresh token chỉ được sử dụng một lần, kể cả khi hai yêu cầu đến đồng thời.
            using (var sessionClient = factory.CreateClient())
            {
                var login = await Post(sessionClient, "/api/auth/login", new { username = "owner", password, activeRole = "Owner" }, HttpStatusCode.OK);
                var sessionId = login.GetProperty("sessionId").GetGuid();
                var oldRefresh = login.GetProperty("tokens").GetProperty("refreshToken").GetString()!;
                var refreshBody = new { sessionId, refreshToken = oldRefresh, activeRole = "Owner" };
                var refreshed = await Task.WhenAll(PostRaw(sessionClient, "/api/auth/refresh", refreshBody), PostRaw(sessionClient, "/api/auth/refresh", refreshBody));
                Assert.Single(refreshed, x => x.StatusCode == HttpStatusCode.OK);
                Assert.Single(refreshed, x => x.StatusCode == HttpStatusCode.Unauthorized);
                var rotated = await Json(refreshed.Single(x => x.StatusCode == HttpStatusCode.OK));
                var tokens = rotated.GetProperty("tokens");
                var newRefresh = tokens.GetProperty("refreshToken").GetString()!;
                Assert.NotEqual(oldRefresh, newRefresh);
                await Expect(await PostRaw(sessionClient, "/api/auth/refresh", refreshBody), HttpStatusCode.Unauthorized);
                Authenticate(sessionClient, tokens.GetProperty("accessToken").GetString()!);
                var switched = await Post(sessionClient, "/api/auth/switch-role", new { activeRole = "Admin" }, HttpStatusCode.OK);
                Authenticate(sessionClient, switched.GetProperty("accessToken").GetString()!);
                Assert.Equal("Admin", (await Get(sessionClient, "/api/auth/me")).GetProperty("activeRole").GetString());
                await Expect(await PostRaw(sessionClient, "/api/auth/logout", new { }), HttpStatusCode.NoContent);
                await Expect(await sessionClient.GetAsync("/api/auth/me"), HttpStatusCode.Unauthorized);
                await Expect(await PostRaw(sessionClient, "/api/auth/refresh", new { sessionId, refreshToken = newRefresh, activeRole = "Owner" }), HttpStatusCode.Unauthorized);
            }
            var apiDocument = await Get(owner, "/openapi/v1.json");
            Assert.True(apiDocument.GetProperty("components").GetProperty("securitySchemes").TryGetProperty("Bearer", out _));
            Assert.False(apiDocument.GetProperty("paths").GetProperty("/api/auth/login").GetProperty("post").TryGetProperty("security", out _));
            Assert.True(apiDocument.GetProperty("paths").GetProperty("/api/branches").GetProperty("get").TryGetProperty("security", out _));
            await Expect(await owner.GetAsync("/scalar/v1"), HttpStatusCode.OK);
            using (var production = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseEnvironment("Production")))
            using (var prodClient = production.CreateClient())
            {
                await Expect(await prodClient.GetAsync("/scalar/v1"), HttpStatusCode.NotFound);
                await Expect(await prodClient.GetAsync("/openapi/v1.json"), HttpStatusCode.NotFound);
            }

            var a = await Post(owner, "/api/branches", new { code = "A", name = "Cửa hàng A" }, HttpStatusCode.Created);
            var b = await Post(owner, "/api/branches", new { code = "B", name = "Cửa hàng B" }, HttpStatusCode.Created);
            var branchA = Id(a); var branchB = Id(b);
            Assert.Equal(branchA, Id(await Get(owner, $"/api/branches/{branchA}")));
            var staffPassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var staff = await Post(owner, "/api/accounts", new { username = "staff", password = staffPassword, fullName = "Nhân viên", branchId = branchA, roles = new[] { "Store" } }, HttpStatusCode.Created);
            var staffId = Id(staff);
            Assert.Equal(staffId, Id(await Get(owner, $"/api/accounts/{staffId}")));
            await Expect(await owner.GetAsync($"/api/accounts/{ownerId}"), HttpStatusCode.Forbidden);
            await Post(owner, "/api/accounts", new { username = "staff-b", password = staffPassword, fullName = "Nhân viên B", branchId = branchB, roles = new[] { "Store" } }, HttpStatusCode.Created);
            await Post(owner, "/api/accounts", new { username = "staff-a2", password = staffPassword, fullName = "Nhân viên A2", branchId = branchA, roles = new[] { "Store" } }, HttpStatusCode.Created);
            using var store = factory.CreateClient();
            Authenticate(store, await Login(store, "staff", staffPassword, "Store"));
            using var storeB = factory.CreateClient();
            Authenticate(storeB, await Login(storeB, "staff-b", staffPassword, "Store"));
            using var storeA2 = factory.CreateClient();
            Authenticate(storeA2, await Login(storeA2, "staff-a2", staffPassword, "Store"));
            var forbidden = await store.GetAsync($"/api/inventory/balances?branchId={branchB}");
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
            await Expect(await store.GetAsync($"/api/branches/{branchB}"), HttpStatusCode.Forbidden);
            var forbiddenAdmin = await adminClient.GetAsync($"/api/inventory/balances?branchId={branchA}");
            Assert.Equal(HttpStatusCode.Forbidden, forbiddenAdmin.StatusCode);

            var product = await Post(owner, "/api/products", new { name = "Hồng đỏ", kind = "Material", unit = "cành", price = 10000m, lowStockThreshold = 2m }, HttpStatusCode.Created);
            var productId = Id(product);
            Assert.Equal(productId, Id(await Get(store, $"/api/products/{productId}")));
            var category = await Post(owner, "/api/products/categories", new { name = "Hoa" }, HttpStatusCode.Created);
            Assert.Equal(Id(category), Id(await Get(store, $"/api/products/categories/{Id(category)}")));
            var receiptBody = new { branchId = branchA, supplierName = "Nhà cung cấp", items = new[] { new { productId, quantity = 10m, unitCost = 5000m } } };
            var receiptKey = Guid.NewGuid().ToString();
            var receiveResponses = await Task.WhenAll(PostRaw(store, "/api/inventory/receipts", receiptBody, receiptKey), PostRaw(store, "/api/inventory/receipts", receiptBody, receiptKey));
            foreach (var response in receiveResponses) await Expect(response, HttpStatusCode.OK);
            var firstReceipt = await Json(receiveResponses[0]); var secondReceipt = await Json(receiveResponses[1]);
            Assert.Equal(Id(firstReceipt), Id(secondReceipt));
            Assert.Equal(10m, await Balance(store, branchA, productId));
            await Expect(await PostRaw(storeA2, "/api/inventory/receipts", receiptBody, receiptKey), HttpStatusCode.Conflict);
            await Expect(await PostRaw(store, "/api/inventory/receipts", new { branchId = branchA, items = new[] { new { productId, quantity = 11m, unitCost = 5000m } } }, receiptKey), HttpStatusCode.Conflict);

            var orderRequest = new { branchId = branchA, scheduledAt = "2026-12-01T10:30:00+07:00", shippingFee = 0m, items = new[] { new { productId = (Guid?)null, name = "Bó theo yêu cầu", quantity = 1m, unitPrice = 100000m, discount = 0m, discountPercent = 10m, discountReason = "Khách quen", isCustomBouquet = true } } };
            var orderKey = Guid.NewGuid().ToString();
            var order = await PostKey(store, "/api/orders", orderRequest, orderKey, HttpStatusCode.Created);
            var orderId = Id(order);
            Assert.Equal(90000m, order.GetProperty("total").GetDecimal());
            var listedOrder = (await Get(store, $"/api/orders?branchId={branchA}")).EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == orderId);
            Assert.Equal("Confirmed", listedOrder.GetProperty("status").GetString());
            Assert.False(listedOrder.TryGetProperty("total", out _));
            Assert.Equal(90000m, (await Get(store, $"/api/orders/{orderId}")).GetProperty("order").GetProperty("total").GetDecimal());
            Assert.Equal(DateTimeOffset.Parse("2026-12-01T03:30:00Z"), order.GetProperty("scheduledAt").GetDateTimeOffset());
            Assert.Equal(10m, order.GetProperty("items")[0].GetProperty("discountPercent").GetDecimal());
            Assert.Equal(orderId, Id(await PostKey(store, "/api/orders", orderRequest, orderKey, HttpStatusCode.Created)));
            var orderItemId = order.GetProperty("items")[0].GetProperty("id").GetGuid();
            await Expect(await PostRaw(store, $"/api/orders/{orderId}/deliver", new { }, Guid.NewGuid().ToString()), HttpStatusCode.Conflict);
            var paper = await Post(owner, "/api/products", new { name = "Giấy gói", kind = "Material", unit = "tờ", price = 1000m, lowStockThreshold = 2m }, HttpStatusCode.Created);
            var paperId = Id(paper);
            await Expect(await PostRaw(store, "/api/bouquets/assemble", new { branchId = branchA, orderItemId, name = "Thiếu nguyên liệu", price = 100000m, materials = new[] { new { productId, quantity = 2m }, new { productId = paperId, quantity = 1m } } }, Guid.NewGuid().ToString()), HttpStatusCode.Conflict);
            Assert.Equal(10m, await Balance(store, branchA, productId));
            var bouquetRequest = new { branchId = branchA, orderItemId, name = "Bó theo yêu cầu", price = 100000m, materials = new[] { new { productId, quantity = 3m } } };
            var bouquet = await PostKey(store, "/api/bouquets/assemble", bouquetRequest, Guid.NewGuid().ToString(), HttpStatusCode.OK);
            var bouquetProductId = bouquet.GetProperty("bouquet").GetProperty("productId").GetGuid();
            Assert.Equal(7m, await Balance(store, branchA, productId));
            var publicProducts = await Get(store, $"/api/products?branchId={branchA}");
            Assert.DoesNotContain(publicProducts.EnumerateArray(), p => p.GetProperty("id").GetGuid() == bouquetProductId);
            await Expect(await store.GetAsync($"/api/products/{bouquetProductId}"), HttpStatusCode.NotFound);
            var otherBranchBalances = await Get(storeB, $"/api/inventory/balances?branchId={branchB}");
            Assert.DoesNotContain(otherBranchBalances.EnumerateArray(), p => p.GetProperty("id").GetGuid() == bouquetProductId);
            var photoBytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+pFR0AAAAASUVORK5CYII=");
            using (var imageBody = ImageBody("image/png", photoBytes, orderId))
            {
                var uploaded = await store.PostAsync("/api/media", imageBody);
                await Expect(uploaded, HttpStatusCode.Created);
                var mediaId = Id(await Json(uploaded));
                await Expect(await store.GetAsync($"/api/media/{mediaId}"), HttpStatusCode.OK);
                await Expect(await storeB.GetAsync($"/api/media/{mediaId}"), HttpStatusCode.Forbidden);
                using var scope = factory.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<FlowerShopDbContext>();
                var file = await db.Media.SingleAsync(x => x.Id == mediaId);
                Assert.Equal(Convert.ToHexString(SHA256.HashData(photoBytes)), file.Checksum);
                await using var stored = await scope.ServiceProvider.GetRequiredService<IFileStorage>().OpenReadAsync(file.StorageKey, default);
                Assert.NotNull(stored);
            }
            using (var invalidImage = ImageBody("image/png", new byte[32], orderId))
                await Expect(await store.PostAsync("/api/media", invalidImage), HttpStatusCode.BadRequest);
            using (var fakePng = ImageBody("image/png", new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, orderId))
                await Expect(await store.PostAsync("/api/media", fakePng), HttpStatusCode.BadRequest);
            using (var fakeHeic = ImageBody("image/heic", new byte[] { 0, 0, 0, 24, 0x66, 0x74, 0x79, 0x70, 0x68, 0x65, 0x69, 0x63, 0, 0, 0, 0, 0x68, 0x65, 0x69, 0x63, 0x6d, 0x69, 0x66, 0x31 }, orderId))
                await Expect(await store.PostAsync("/api/media", fakeHeic), HttpStatusCode.BadRequest);
            // Fixture HEIC nhỏ được tạo và giải mã trước khi đưa vào test; đây là tệp HEIC thật, không phải header giả.
            var jpegBytes = Convert.FromBase64String("/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAP//////////////////////////////////////////////////////////////////////////////////////2wBDAf//////////////////////////////////////////////////////////////////////////////////////wAARCAABAAEDASIAAhEBAxEB/8QAFQABAQAAAAAAAAAAAAAAAAAAAAX/xAAUEAEAAAAAAAAAAAAAAAAAAAAA/8QAFQEBAQAAAAAAAAAAAAAAAAAAAAX/xAAUEQEAAAAAAAAAAAAAAAAAAAAA/9oADAMBAAIRAxEAPwCwAB//2Q==");
            var heicPath = Path.Combine(AppContext.BaseDirectory, "TestAssets", "valid.heic");
            Assert.True(File.Exists(heicPath), "Thiếu fixture HEIC thật trong output kiểm thử.");
            var heicBytes = await File.ReadAllBytesAsync(heicPath);
            var bouquetIdForPhoto = bouquet.GetProperty("bouquet").GetProperty("id").GetGuid();
            foreach (var sample in new[] { (Type: "image/jpeg", Bytes: jpegBytes, Target: "Product", Id: productId), (Type: "image/heic", Bytes: heicBytes, Target: "Bouquet", Id: bouquetIdForPhoto) })
            {
                using var body = ImageBody(sample.Type, sample.Bytes, sample.Id, sample.Target);
                var response = await store.PostAsync("/api/media", body);
                await Expect(response, HttpStatusCode.Created);
                var photoId = Id(await Json(response));
                var download = await store.GetAsync($"/api/media/{photoId}");
                await Expect(download, HttpStatusCode.OK);
                Assert.Equal(sample.Bytes, await download.Content.ReadAsByteArrayAsync());
                if (sample.Target == "Bouquet") await Expect(await storeB.GetAsync($"/api/media/{photoId}"), HttpStatusCode.Forbidden);
                Assert.Contains((await Get(store, $"/api/media/for/{sample.Target}/{sample.Id}")).EnumerateArray(), x => x.GetProperty("id").GetGuid() == photoId);
            }
            var maximumImage = new byte[10 * 1024 * 1024];
            photoBytes.CopyTo(maximumImage, 0);
            using (var maximum = ImageBody("image/png", maximumImage, orderId))
                await Expect(await store.PostAsync("/api/media", maximum), HttpStatusCode.Created);
            var oversizedImage = new byte[10 * 1024 * 1024 + 1];
            photoBytes.CopyTo(oversizedImage, 0);
            using (var oversized = ImageBody("image/png", oversizedImage, orderId))
                await Expect(await store.PostAsync("/api/media", oversized), HttpStatusCode.BadRequest);
            using (var wrongMime = ImageBody("image/gif", photoBytes, orderId))
                await Expect(await store.PostAsync("/api/media", wrongMime), HttpStatusCode.BadRequest);
            var paymentKey = Guid.NewGuid().ToString();
            var paymentBody = new { method = "Cash", amount = 50000m };
            var paymentResponses = await Task.WhenAll(PostRaw(store, $"/api/orders/{orderId}/payments", paymentBody, paymentKey), PostRaw(store, $"/api/orders/{orderId}/payments", paymentBody, paymentKey));
            foreach (var response in paymentResponses) await Expect(response, HttpStatusCode.OK);
            Assert.Equal(Id(await Json(paymentResponses[0])), Id(await Json(paymentResponses[1])));
            await PostKey(store, $"/api/orders/{orderId}/payments", new { method = "Transfer", amount = 40000m }, Guid.NewGuid().ToString(), HttpStatusCode.OK);
            var deliverKey = Guid.NewGuid().ToString();
            await PostKey(store, $"/api/orders/{orderId}/deliver", new { }, deliverKey, HttpStatusCode.OK);
            await PostKey(store, $"/api/orders/{orderId}/deliver", new { }, deliverKey, HttpStatusCode.OK);
            Assert.Equal(0m, await Balance(store, branchA, bouquetProductId));
            var summary = await Get(owner, "/api/reports/summary");
            Assert.Equal(90000m, summary.GetProperty("revenue").GetDecimal());
            Assert.Equal(50000m, summary.GetProperty("payments").GetProperty("cash").GetDecimal());
            await PostKey(owner, $"/api/orders/{orderId}/refunds", new { method = "Cash", amount = 10000m, note = "Hoàn thực tế" }, Guid.NewGuid().ToString(), HttpStatusCode.OK);
            await PostKey(owner, $"/api/orders/{orderId}/cancel", new { reason = "Khách hủy", bouquets = Array.Empty<object>() }, Guid.NewGuid().ToString(), HttpStatusCode.NoContent);
            summary = await Get(owner, "/api/reports/summary");
            Assert.Equal(90000m, summary.GetProperty("revenue").GetDecimal());
            Assert.Equal(10000m, summary.GetProperty("refunds").GetProperty("total").GetDecimal());
            await PostKey(owner, $"/api/orders/{orderId}/returns", new { productId = bouquetProductId, quantity = 1m, reason = "Còn bán được" }, Guid.NewGuid().ToString(), HttpStatusCode.OK);
            Assert.Equal(1m, await Balance(owner, branchA, bouquetProductId));
            await Expect(await PostRaw(owner, $"/api/orders/{orderId}/returns", new { productId = bouquetProductId, quantity = 1m, reason = "Lặp" }, Guid.NewGuid().ToString()), HttpStatusCode.Conflict);

            // Chuỗi bó làm sẵn: nhập nguyên liệu -> làm bó -> tạo đơn -> thu -> giao.
            var readyBouquet = await PostKey(store, "/api/bouquets/assemble", new { branchId = branchA, name = "Bó làm sẵn", price = 40000m, materials = new[] { new { productId, quantity = 1m } } }, Guid.NewGuid().ToString(), HttpStatusCode.OK);
            var readyProductId = readyBouquet.GetProperty("bouquet").GetProperty("productId").GetGuid();
            var readyOrder = Id(await PostKey(store, "/api/orders", new { branchId = branchA, shippingFee = 0m, items = new[] { new { productId = (Guid?)readyProductId, quantity = 1m, discount = 0m, isCustomBouquet = false } } }, Guid.NewGuid().ToString(), HttpStatusCode.Created));
            await PostKey(store, $"/api/orders/{readyOrder}/payments", new { method = "Cash", amount = 40000m }, Guid.NewGuid().ToString(), HttpStatusCode.OK);
            await PostKey(store, $"/api/orders/{readyOrder}/deliver", new { }, Guid.NewGuid().ToString(), HttpStatusCode.OK);
            Assert.Equal(0m, await Balance(store, branchA, readyProductId));
            summary = await Get(owner, "/api/reports/summary");
            Assert.Equal(130000m, summary.GetProperty("revenue").GetDecimal());
            var history = await Get(owner, $"/api/inventory/history?branchId={branchA}&productId={productId}");
            Assert.Contains(history.EnumerateArray(), x => x.GetProperty("type").GetString() == "BouquetConsumption" && x.GetProperty("actorId").GetGuid() == staffId);

            var direct = new { branchId = branchA, shippingFee = 0m, items = new[] { new { productId = (Guid?)productId, quantity = 6m, discount = 0m, isCustomBouquet = false } } };
            var orderA = Id(await PostKey(store, "/api/orders", direct, Guid.NewGuid().ToString(), HttpStatusCode.Created));
            var orderB = Id(await PostKey(store, "/api/orders", direct, Guid.NewGuid().ToString(), HttpStatusCode.Created));
            await Expect(await PutRaw(owner, $"/api/products/{productId}", new { name = "Tên mới", unit = "cành", price = 12000m, lowStockThreshold = 2m, isActive = true }), HttpStatusCode.NoContent);
            var snapshot = await Get(owner, $"/api/orders/{orderA}");
            Assert.Equal("Hồng đỏ", snapshot.GetProperty("order").GetProperty("items")[0].GetProperty("nameSnapshot").GetString());
            Assert.Equal(10000m, snapshot.GetProperty("order").GetProperty("items")[0].GetProperty("unitPrice").GetDecimal());
            var deliveries = await Task.WhenAll(PostRaw(store, $"/api/orders/{orderA}/deliver", new { }, Guid.NewGuid().ToString()), PostRaw(store, $"/api/orders/{orderB}/deliver", new { }, Guid.NewGuid().ToString()));
            Assert.Equal(1, deliveries.Count(x => x.StatusCode == HttpStatusCode.OK));
            Assert.Equal(1, deliveries.Count(x => x.StatusCode == HttpStatusCode.Conflict));
            Assert.Equal(0m, await Balance(store, branchA, productId));

            // Gây lỗi trong database thử nghiệm sau khi bắt đầu thao tác, rồi kiểm tra rollback toàn bộ.
            var rollbackProduct = Id(await Post(owner, "/api/products", new { name = "Nguyên liệu kiểm tra rollback", kind = "Material", unit = "cành", price = 1000m, lowStockThreshold = 1m }, HttpStatusCode.Created));
            using (var setup = factory.Services.CreateScope())
            {
                var db = setup.ServiceProvider.GetRequiredService<FlowerShopDbContext>();
                await db.Database.ExecuteSqlRawAsync("""
                    CREATE FUNCTION flowershop_test_fail_receipt() RETURNS trigger LANGUAGE plpgsql AS $$
                    BEGIN
                        IF NEW."Note" = 'FLOWERSHOP_TEST_FAIL' THEN RAISE EXCEPTION 'Injected test failure'; END IF;
                        RETURN NEW;
                    END $$;
                    CREATE TRIGGER flowershop_test_fail_receipt BEFORE INSERT ON "InventoryTransactions"
                    FOR EACH ROW EXECUTE FUNCTION flowershop_test_fail_receipt();
                    """);
            }
            var failKey = Guid.NewGuid().ToString();
            var failBody = new { branchId = branchA, note = "FLOWERSHOP_TEST_FAIL", items = new[] { new { productId = rollbackProduct, quantity = 5m, unitCost = 100m } } };
            await Expect(await PostRaw(store, "/api/inventory/receipts", failBody, failKey), HttpStatusCode.InternalServerError);
            Assert.Equal(0m, await Balance(store, branchA, rollbackProduct));
            using (var verify = factory.Services.CreateScope())
            {
                var db = verify.ServiceProvider.GetRequiredService<FlowerShopDbContext>();
                Assert.False(await db.CommandReceipts.AnyAsync(x => x.Key == failKey));
                Assert.False(await db.InventoryTransactions.AnyAsync(x => x.ProductId == rollbackProduct));
                Assert.False(await db.GoodsReceiptItems.AnyAsync(x => x.ProductId == rollbackProduct));
                await db.Database.ExecuteSqlRawAsync("DROP TRIGGER flowershop_test_fail_receipt ON \"InventoryTransactions\"; DROP FUNCTION flowershop_test_fail_receipt();");
            }
            await PostKey(store, "/api/inventory/receipts", failBody, failKey, HttpStatusCode.OK);
            Assert.Equal(5m, await Balance(store, branchA, rollbackProduct));

            // Sau hủy bó theo đơn: bán lại giữ tồn thành phẩm, hư hỏng trừ thành phẩm.
            foreach (var disposition in new[] { "Release", "Damage" })
            {
                var custom = await PostKey(store, "/api/orders", new { branchId = branchA, shippingFee = 0m, items = new[] { new { name = "Bó xử lý khi hủy", quantity = 1m, unitPrice = 5000m, discount = 0m, isCustomBouquet = true } } }, Guid.NewGuid().ToString(), HttpStatusCode.Created);
                var customId = Id(custom);
                var customItemId = custom.GetProperty("items")[0].GetProperty("id").GetGuid();
                var assembled = await PostKey(store, "/api/bouquets/assemble", new { branchId = branchA, orderItemId = customItemId, name = "Bó kiểm thử", price = 5000m, materials = new[] { new { productId = rollbackProduct, quantity = 1m } } }, Guid.NewGuid().ToString(), HttpStatusCode.OK);
                var bouquetId = assembled.GetProperty("bouquet").GetProperty("id").GetGuid();
                var finishedId = assembled.GetProperty("bouquet").GetProperty("productId").GetGuid();
                await Expect(await PostRaw(owner, $"/api/orders/{customId}/cancel", new { reason = "Hủy kiểm thử", bouquets = Array.Empty<object>() }, Guid.NewGuid().ToString()), HttpStatusCode.BadRequest);
                var cancelBody = new { reason = "Hủy kiểm thử", bouquets = new[] { new { bouquetId, action = disposition } } };
                var cancelKey = Guid.NewGuid().ToString();
                await PostKey(owner, $"/api/orders/{customId}/cancel", cancelBody, cancelKey, HttpStatusCode.NoContent);
                await PostKey(owner, $"/api/orders/{customId}/cancel", cancelBody, cancelKey, HttpStatusCode.NoContent);
                Assert.Equal(disposition == "Release" ? 1m : 0m, await Balance(store, branchA, finishedId));
                var available = await Get(store, $"/api/products?branchId={branchA}");
                Assert.Equal(disposition == "Release", available.EnumerateArray().Any(x => x.GetProperty("id").GetGuid() == finishedId));
            }
            Assert.Equal(3m, await Balance(store, branchA, rollbackProduct));
            var audit = await Get(owner, $"/api/audit?branchId={branchA}");
            Assert.Contains(audit.EnumerateArray(), x => x.GetProperty("action").GetString() == "CancelOrder" && x.GetProperty("actorId").GetGuid() == ownerId);

            await Expect(await PutRaw(owner, $"/api/accounts/{staffId}/active", new { isActive = false }), HttpStatusCode.NoContent);
            await Expect(await store.GetAsync("/api/auth/me"), HttpStatusCode.Unauthorized);
            await Expect(await PostRaw(adminClient, "/api/auth/switch-role", new { activeRole = "Store" }), HttpStatusCode.Forbidden);
        }
        finally
        {
            Environment.SetEnvironmentVariable("FLOWERSHOP_CONNECTION_STRING", oldConnection);
            Environment.SetEnvironmentVariable("Jwt__SigningKey", oldJwt);
            Environment.SetEnvironmentVariable("Storage__RootPath", oldStorage);
            NpgsqlConnection.ClearAllPools();
            await using var connection = new NpgsqlConnection(admin.ConnectionString);
            await connection.OpenAsync();
            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)", connection);
            await drop.ExecuteNonQueryAsync();
            // Xóa đúng thư mục ảnh có tên ngẫu nhiên do bài kiểm thử vừa tạo.
            var tempRoot = Path.GetFullPath(Path.GetTempPath());
            var resolved = Path.GetFullPath(testStorage);
            if (resolved.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(resolved).StartsWith("flowershop-test-", StringComparison.Ordinal) && Directory.Exists(resolved)) Directory.Delete(resolved, true);
        }
    }

    private static void Authenticate(HttpClient client, string token) => client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    private static async Task<string> Login(HttpClient client, string username, string password, string role)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new { username, password, activeRole = role });
        await Expect(response, HttpStatusCode.OK);
        return (await Json(response)).GetProperty("tokens").GetProperty("accessToken").GetString()!;
    }
    private static Guid Id(JsonElement json) => json.GetProperty("id").GetGuid();
    private static async Task<JsonElement> Get(HttpClient client, string path)
    {
        var response = await client.GetAsync(path); await Expect(response, HttpStatusCode.OK); return await Json(response);
    }
    private static async Task<JsonElement> Post(HttpClient client, string path, object body, HttpStatusCode status)
    {
        var response = await client.PostAsJsonAsync(path, body); await Expect(response, status); return await Json(response);
    }
    private static async Task<JsonElement> PostKey(HttpClient client, string path, object body, string key, HttpStatusCode status)
    {
        var response = await PostRaw(client, path, body, key); await Expect(response, status);
        return status == HttpStatusCode.NoContent ? default : await Json(response);
    }
    private static async Task<HttpResponseMessage> PostRaw(HttpClient client, string path, object body, string? key = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        if (key is not null) request.Headers.Add("Idempotency-Key", key);
        return await client.SendAsync(request);
    }
    private static async Task<HttpResponseMessage> PutRaw(HttpClient client, string path, object body) => await client.PutAsJsonAsync(path, body);
    private static MultipartFormDataContent ImageBody(string contentType, byte[] bytes, Guid targetId, string targetType = "Order")
    {
        var content = new MultipartFormDataContent();
        var image = new ByteArrayContent(bytes);
        image.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(image, "file", "photo.png");
        content.Add(new StringContent(targetType), "targetType");
        content.Add(new StringContent(targetId.ToString()), "targetId");
        return content;
    }
    private static async Task<JsonElement> Json(HttpResponseMessage response) => (await response.Content.ReadFromJsonAsync<JsonElement>());
    private static async Task Expect(HttpResponseMessage response, HttpStatusCode expected)
    {
        if (response.StatusCode != expected)
        {
            var body = await response.Content.ReadAsStringAsync();
            Assert.Fail($"Expected {(int)expected}, got {(int)response.StatusCode}: {body}");
        }
    }
    private static async Task<decimal> Balance(HttpClient client, Guid branchId, Guid productId)
    {
        var rows = await Get(client, $"/api/inventory/balances?branchId={branchId}");
        return rows.EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == productId).GetProperty("quantity").GetDecimal();
    }
}
