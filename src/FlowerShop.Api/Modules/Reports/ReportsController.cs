using FlowerShop.Api.Modules.Common;
using FlowerShop.Domain.Entities;
using FlowerShop.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FlowerShop.Api.Modules.Reports;

[Route("api/reports")]
public sealed class ReportsController(FlowerShopDbContext db) : SecuredController(db)
{
    [HttpGet("summary")]
    public async Task<IActionResult> Summary([FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, [FromQuery] Guid? branchId, CancellationToken ct)
    {
        RequireRole(Roles.Owner, Roles.Store);
        var vietnamOffset = TimeSpan.FromHours(7);
        var today = DateTimeOffset.UtcNow.ToOffset(vietnamOffset).Date;
        var start = from?.ToUniversalTime() ?? new DateTimeOffset(today, vietnamOffset).ToUniversalTime();
        var end = to?.ToUniversalTime() ?? DateTimeOffset.UtcNow;
        if (end <= start || end - start > TimeSpan.FromDays(366)) throw ApiError.BadRequest("Khoảng thời gian phải dương và không quá 366 ngày.");
        var allowedBranch = await ResolveBranch(branchId, ct);
        // Đơn bị hủy sau giao vẫn giữ doanh thu để đúng giới hạn đã công bố của MVP.
        var delivered = Db.Orders.Where(x => x.DeliveredAt >= start && x.DeliveredAt < end);
        var created = Db.Orders.Where(x => x.CreatedAt >= start && x.CreatedAt < end);
        if (allowedBranch is not null) { delivered = delivered.Where(x => x.BranchId == allowedBranch); created = created.Where(x => x.BranchId == allowedBranch); }
        var payments = Db.Payments.Where(x => x.CreatedAt >= start && x.CreatedAt < end);
        var refunds = Db.Refunds.Where(x => x.CreatedAt >= start && x.CreatedAt < end);
        if (allowedBranch is not null)
        {
            payments = payments.Where(x => Db.Orders.Where(o => o.BranchId == allowedBranch).Select(o => o.Id).Contains(x.OrderId));
            refunds = refunds.Where(x => Db.Orders.Where(o => o.BranchId == allowedBranch).Select(o => o.Id).Contains(x.OrderId));
        }
        var deliveredOrders = await delivered.Select(x => new { x.Id, x.BranchId, x.Code, x.Total, x.DeliveredAt }).ToListAsync(ct);
        var paymentRows = await payments.ToListAsync(ct);
        var refundRows = await refunds.ToListAsync(ct);
        var orderRows = await created.GroupBy(x => x.Status).Select(g => new { status = g.Key, count = g.Count(), total = g.Sum(x => x.Total) }).ToListAsync(ct);
        var discount = await (allowedBranch is null ? Db.OrderItems : Db.OrderItems.Where(i => Db.Orders.Where(o => o.BranchId == allowedBranch).Select(o => o.Id).Contains(i.SalesOrderId))).Where(i => i.Discount > 0 && Db.Orders.Where(o => o.CreatedAt >= start && o.CreatedAt < end).Select(o => o.Id).Contains(i.SalesOrderId)).SumAsync(x => (decimal?)x.Discount, ct) ?? 0;
        var balances = await (from branch in Db.Branches.Where(x => x.IsActive && (allowedBranch == null || x.Id == allowedBranch))
                              from p in Db.Products.Where(x => x.IsActive && (x.Kind != "Bouquet" || Db.Bouquets.Any(q => q.ProductId == x.Id && q.BranchId == branch.Id && q.Status == "Available")))
                              join b in Db.Balances on new { BranchId = branch.Id, ProductId = p.Id } equals new { b.BranchId, b.ProductId } into grouped
                              from b in grouped.DefaultIfEmpty()
                              select new { p.Id, p.Sku, p.Name, BranchId = branch.Id, Quantity = b == null ? 0 : b.Quantity, p.LowStockThreshold }).Where(x => x.Quantity <= x.LowStockThreshold).ToListAsync(ct);
        var waste = await (from transaction in Db.InventoryTransactions
                           join product in Db.Products on transaction.ProductId equals product.Id
                           where (transaction.Type == "Damage" || transaction.Type == "Waste") && transaction.CreatedAt >= start && transaction.CreatedAt < end && (allowedBranch == null || transaction.BranchId == allowedBranch)
                           group transaction by new { transaction.BranchId, product.Id, product.Sku, product.Name, product.Unit } into grouped
                           select new { grouped.Key.BranchId, productId = grouped.Key.Id, grouped.Key.Sku, grouped.Key.Name, grouped.Key.Unit, quantity = -grouped.Sum(x => x.Change) }).ToListAsync(ct);
        return Ok(new
        {
            from = start, to = end,
            revenue = deliveredOrders.Sum(x => x.Total),
            deliveredOrders = deliveredOrders.Count,
            payments = new { cash = paymentRows.Where(x => x.Method == "Cash").Sum(x => x.Amount), transfer = paymentRows.Where(x => x.Method == "Transfer").Sum(x => x.Amount), total = paymentRows.Sum(x => x.Amount) },
            refunds = new { cash = refundRows.Where(x => x.Method == "Cash").Sum(x => x.Amount), transfer = refundRows.Where(x => x.Method == "Transfer").Sum(x => x.Amount), total = refundRows.Sum(x => x.Amount) },
            discounts = discount, orders = orderRows, lowStock = balances, waste,
            limitations = new[] { "Doanh thu tính theo đơn đã giao.", "Khoản hoàn hiển thị theo ngày hoàn.", "Hủy đơn sau khi đã giao chưa tự điều chỉnh doanh thu trong MVP; Owner cần đối chiếu thủ công." }
        });
    }

    private async Task<Guid?> ResolveBranch(Guid? requested, CancellationToken ct)
    {
        if (ActiveRole == Roles.Store)
        {
            var own = await Db.Users.Where(x => x.Id == ActorId).Select(x => x.BranchId).SingleAsync(ct);
            if (requested is not null && requested != own) throw ApiError.Forbidden();
            return own;
        }
        if (requested is not null && !await Db.Branches.AnyAsync(x => x.Id == requested, ct)) throw ApiError.NotFound();
        return requested;
    }
}
