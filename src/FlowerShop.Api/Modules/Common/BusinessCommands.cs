using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FlowerShop.Domain.Entities;
using FlowerShop.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FlowerShop.Api.Modules.Common;

public sealed class BusinessCommands(FlowerShopDbContext db)
{
    public async Task<Guid> RunAsync(Guid actorId, Guid branchId, string operation, string? key, object request, Func<Task<Guid>> action, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 100 || key != key.Trim()) throw ApiError.BadRequest("Cần header Idempotency-Key từ 1 đến 100 ký tự, không có khoảng trắng ở hai đầu.");
        var hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request)));
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Các thao tác tại một cửa hàng được tuần tự hóa để bảo toàn tồn và số đơn.
        var keyLock = $"command:{branchId}:{operation}:{key}";
        var branchLock = $"branch:{branchId}";
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({keyLock}, 0))", ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({branchLock}, 0))", ct);
        var receipt = await db.CommandReceipts.SingleOrDefaultAsync(x => x.BranchId == branchId && x.Operation == operation && x.Key == key, ct);
        if (receipt is not null)
        {
            if (receipt.RequestHash != hash || receipt.ActorId != actorId) throw ApiError.Conflict("Idempotency-Key đã được dùng bởi yêu cầu khác.");
            await transaction.CommitAsync(ct);
            return receipt.ResultId;
        }
        var result = await action();
        db.CommandReceipts.Add(new CommandReceipt { ActorId = actorId, BranchId = branchId, Operation = operation, Key = key, RequestHash = hash, ResultId = result });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return result;
    }

    public async Task ChangeStock(Guid branchId, Guid productId, decimal delta, string type, Guid referenceId, Guid actorId, string? note, CancellationToken ct)
    {
        if (delta == 0 || decimal.Round(delta, 3) != delta) throw ApiError.BadRequest("Số lượng phải khác 0 và có tối đa 3 chữ số thập phân.");
        var balance = await db.Balances.SingleOrDefaultAsync(x => x.BranchId == branchId && x.ProductId == productId, ct);
        if (balance is null)
        {
            balance = db.Balances.Local.SingleOrDefault(x => x.BranchId == branchId && x.ProductId == productId);
            if (balance is null) { balance = new InventoryBalance { BranchId = branchId, ProductId = productId }; db.Balances.Add(balance); }
        }
        if (balance.Quantity + delta < 0) throw ApiError.Conflict("Số lượng tồn không đủ; vui lòng kiểm tra lại tồn kho.");
        balance.Quantity += delta;
        db.InventoryTransactions.Add(new InventoryTransaction { BranchId = branchId, ProductId = productId, Change = delta, Type = type, ReferenceId = referenceId, Note = note, ActorId = actorId });
    }

    public static void Quantity(decimal value)
    {
        if (value <= 0 || value >= 1_000_000_000_000_000m || decimal.Round(value, 3) != value) throw ApiError.BadRequest("Số lượng phải dương và có tối đa 3 chữ số thập phân.");
    }
    public static void Money(decimal value)
    {
        if (value < 0 || value >= 10_000_000_000_000_000m || decimal.Round(value, 2) != value) throw ApiError.BadRequest("Số tiền không hợp lệ hoặc có quá 2 chữ số thập phân.");
    }
}
