using FlowerShop.Api.Modules.Common;
using FlowerShop.Domain.Entities;
using FlowerShop.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FlowerShop.Api.Modules.Inventory;

public sealed record ReceiptLine(Guid ProductId, decimal Quantity, decimal UnitCost);
public sealed record ReceiptRequest(Guid BranchId, string? SupplierName, string? Note, List<ReceiptLine> Items);
public sealed record StockChangeRequest(Guid BranchId, Guid ProductId, string Type, decimal Quantity, string Reason);

[Route("api/inventory")]
public sealed class InventoryController(FlowerShopDbContext db, BusinessCommands commands) : SecuredController(db)
{
    [HttpGet("balances")]
    public async Task<IActionResult> Balances([FromQuery] Guid branchId, CancellationToken ct)
    {
        await CheckBranchAsync(branchId, ct);
        return Ok(await (from p in Db.Products.Where(p => p.Kind != "Bouquet" || Db.Bouquets.Any(b => b.ProductId == p.Id && b.BranchId == branchId))
                         join b in Db.Balances.Where(x => x.BranchId == branchId) on p.Id equals b.ProductId into grouped
                         from balance in grouped.DefaultIfEmpty()
                         select new { p.Id, p.Sku, p.Name, p.Unit, p.Kind, p.LowStockThreshold, Quantity = balance == null ? 0 : balance.Quantity })
            .OrderBy(x => x.Name).ToListAsync(ct));
    }

    [HttpGet("history")]
    public async Task<IActionResult> History([FromQuery] Guid branchId, [FromQuery] Guid? productId, CancellationToken ct)
    {
        await CheckBranchAsync(branchId, ct);
        var query = Db.InventoryTransactions.Where(x => x.BranchId == branchId);
        if (productId is not null) query = query.Where(x => x.ProductId == productId);
        return Ok(await query.OrderByDescending(x => x.CreatedAt).Take(500).ToListAsync(ct));
    }

    [HttpPost("receipts")]
    public async Task<IActionResult> Receive(ReceiptRequest request, [FromHeader(Name = "Idempotency-Key")] string? key, CancellationToken ct)
    {
        await CheckBranchAsync(request.BranchId, ct);
        if (request.Items is null || request.Items.Count == 0 || request.Items.Count > 200 || request.Items.Select(x => x.ProductId).Distinct().Count() != request.Items.Count) throw ApiError.BadRequest("Phiếu nhập cần từ 1 đến 200 sản phẩm khác nhau.");
        foreach (var item in request.Items) { BusinessCommands.Quantity(item.Quantity); BusinessCommands.Money(item.UnitCost); }
        var id = await commands.RunAsync(ActorId, request.BranchId, "ReceiveStock", key, request, async () =>
        {
            var ids = request.Items.Select(x => x.ProductId).ToArray();
            var products = await Db.Products.Where(x => ids.Contains(x.Id) && x.IsActive && x.Kind != "Bouquet").CountAsync(ct);
            if (products != ids.Length) throw ApiError.BadRequest("Sản phẩm không hợp lệ hoặc không thể nhập thủ công bó thành phẩm.");
            var receipt = new GoodsReceipt { BranchId = request.BranchId, SupplierName = request.SupplierName?.Trim(), Note = request.Note?.Trim(), ActorId = ActorId };
            foreach (var item in request.Items)
            {
                receipt.Items.Add(new GoodsReceiptItem { GoodsReceiptId = receipt.Id, ProductId = item.ProductId, Quantity = item.Quantity, UnitCost = item.UnitCost });
                await commands.ChangeStock(request.BranchId, item.ProductId, item.Quantity, "Receipt", receipt.Id, ActorId, request.Note, ct);
            }
            Db.GoodsReceipts.Add(receipt); Audit("ReceiveStock", "GoodsReceipt", receipt.Id, request.BranchId);
            return receipt.Id;
        }, ct);
        return Ok(await Db.GoodsReceipts.Include(x => x.Items).SingleAsync(x => x.Id == id, ct));
    }

    [HttpPost("changes")]
    public async Task<IActionResult> Change(StockChangeRequest request, [FromHeader(Name = "Idempotency-Key")] string? key, CancellationToken ct)
    {
        await CheckBranchAsync(request.BranchId, ct);
        if (request.Type is not ("Out" or "Damage" or "Adjust" or "Stocktake") || string.IsNullOrWhiteSpace(request.Reason)) throw ApiError.BadRequest("Loại thao tác hoặc lý do không hợp lệ.");
        if (request.Type is "Out" or "Damage") BusinessCommands.Quantity(request.Quantity);
        if (request.Type == "Stocktake" && request.Quantity < 0) throw ApiError.BadRequest("Số lượng kiểm kê không được âm.");
        if (decimal.Round(request.Quantity, 3) != request.Quantity) throw ApiError.BadRequest("Số lượng có tối đa 3 chữ số thập phân.");
        var id = await commands.RunAsync(ActorId, request.BranchId, "ChangeStock", key, request, async () =>
        {
            var product = await Db.Products.SingleOrDefaultAsync(x => x.Id == request.ProductId, ct) ?? throw ApiError.NotFound();
            if (product.Kind == "Bouquet")
            {
                var bouquet = await Db.Bouquets.SingleOrDefaultAsync(x => x.ProductId == product.Id && x.BranchId == request.BranchId, ct);
                if (request.Type != "Damage" || request.Quantity != 1 || bouquet is null || bouquet.Status != "Available") throw ApiError.Conflict("Chỉ có thể ghi hư hỏng bó đang bán tại cửa hàng này.");
                bouquet.Status = "Damaged";
            }
            var current = await Db.Balances.Where(x => x.BranchId == request.BranchId && x.ProductId == request.ProductId).Select(x => (decimal?)x.Quantity).SingleOrDefaultAsync(ct) ?? 0;
            var delta = request.Type switch { "Out" or "Damage" => -request.Quantity, "Stocktake" => request.Quantity - current, _ => request.Quantity };
            var transactionId = Guid.NewGuid();
            if (delta != 0) await commands.ChangeStock(request.BranchId, request.ProductId, delta, request.Type, transactionId, ActorId, request.Reason.Trim(), ct);
            Audit(request.Type, "InventoryTransaction", transactionId, request.BranchId, $"product={request.ProductId}; before={current}; after={current + delta}; reason={request.Reason.Trim()}");
            return transactionId;
        }, ct);
        return Ok(new { id });
    }
}
