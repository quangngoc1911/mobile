using FlowerShop.Api.Modules.Common;
using FlowerShop.Domain.Entities;
using FlowerShop.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FlowerShop.Api.Modules.Bouquets;

public sealed record MaterialLine(Guid ProductId, decimal Quantity);
public sealed record AssembleBouquetRequest(Guid BranchId, Guid? OrderItemId, string Name, decimal Price, string? Note, List<MaterialLine> Materials);

[Route("api/bouquets")]
public sealed class BouquetsController(FlowerShopDbContext db, BusinessCommands commands) : SecuredController(db)
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid branchId, CancellationToken ct)
    {
        await CheckBranchAsync(branchId, ct);
        return Ok(await Db.Bouquets.Include(x => x.Materials).Where(x => x.BranchId == branchId).OrderByDescending(x => x.CreatedAt).Take(500).ToListAsync(ct));
    }

    [HttpPost("assemble")]
    public async Task<IActionResult> Assemble(AssembleBouquetRequest request, [FromHeader(Name = "Idempotency-Key")] string? key, CancellationToken ct)
    {
        await CheckBranchAsync(request.BranchId, ct);
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 500 || request.Materials is null || request.Materials.Count is < 1 or > 100 || request.Materials.Select(x => x.ProductId).Distinct().Count() != request.Materials.Count) throw ApiError.BadRequest("Cần tên bó và 1–100 nguyên liệu khác nhau.");
        BusinessCommands.Money(request.Price);
        foreach (var item in request.Materials) BusinessCommands.Quantity(item.Quantity);
        var id = await commands.RunAsync(ActorId, request.BranchId, "AssembleBouquet", key, request, async () =>
        {
            SalesOrderItem? orderItem = null;
            if (request.OrderItemId is not null)
            {
                orderItem = await Db.OrderItems.SingleOrDefaultAsync(x => x.Id == request.OrderItemId, ct) ?? throw ApiError.NotFound();
                var order = await Db.Orders.SingleAsync(x => x.Id == orderItem.SalesOrderId, ct);
                if (order.BranchId != request.BranchId || order.Status != "Confirmed" || !orderItem.IsCustomBouquet || await Db.Bouquets.AnyAsync(x => x.OrderItemId == orderItem.Id, ct))
                    throw ApiError.Conflict("Dòng đơn không thể gắn thêm bó thiết kế.");
            }
            var ids = request.Materials.Select(x => x.ProductId).ToArray();
            if (await Db.Products.CountAsync(x => ids.Contains(x.Id) && x.IsActive && x.Kind != "Bouquet", ct) != ids.Length) throw ApiError.BadRequest("Nguyên liệu không tồn tại hoặc không hợp lệ.");
            var product = new Product { Sku = $"SKU-{Guid.NewGuid():N}"[..16].ToUpperInvariant(), Name = request.Name.Trim(), Kind = "Bouquet", Unit = "bó", Price = orderItem?.UnitPrice ?? request.Price };
            var bouquet = new Bouquet { BranchId = request.BranchId, ProductId = product.Id, OrderItemId = orderItem?.Id, Status = orderItem is null ? "Available" : "ReadyForOrder", CreatedBy = ActorId };
            if (orderItem is not null) orderItem.SkuSnapshot = product.Sku;
            foreach (var item in request.Materials)
            {
                bouquet.Materials.Add(new BouquetMaterial { BouquetId = bouquet.Id, ProductId = item.ProductId, Quantity = item.Quantity });
                await commands.ChangeStock(request.BranchId, item.ProductId, -item.Quantity, "BouquetConsumption", bouquet.Id, ActorId, request.Note, ct);
            }
            Db.Products.Add(product);
            await commands.ChangeStock(request.BranchId, product.Id, 1, "BouquetOutput", bouquet.Id, ActorId, request.Note, ct);
            Db.Bouquets.Add(bouquet); Audit("AssembleBouquet", "Bouquet", bouquet.Id, request.BranchId, orderItem is null ? null : $"orderItem={orderItem.Id}"); return bouquet.Id;
        }, ct);
        var result = await Db.Bouquets.Include(x => x.Materials).SingleAsync(x => x.Id == id, ct);
        var sku = await Db.Products.Where(x => x.Id == result.ProductId).Select(x => x.Sku).SingleAsync(ct);
        return Ok(new { bouquet = result, sku });
    }
}
