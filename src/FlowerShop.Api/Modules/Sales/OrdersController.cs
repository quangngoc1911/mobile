using FlowerShop.Api.Modules.Common;
using FlowerShop.Domain.Entities;
using FlowerShop.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FlowerShop.Api.Modules.Sales;

public sealed record OrderLineRequest(Guid? ProductId, string? Name, string? Unit, decimal Quantity, decimal? UnitPrice, decimal Discount, string? DiscountReason, string? Note, bool IsCustomBouquet, decimal? DiscountPercent = null);
public sealed record CreateOrderRequest(Guid BranchId, string? CustomerName, string? CustomerPhone, string? CustomerAddress, string? Note, DateTimeOffset? ScheduledAt, decimal ShippingFee, List<OrderLineRequest> Items);
public sealed record AddPaymentRequest(string Method, decimal Amount, string? Note);
public sealed record RefundRequest(string Method, decimal Amount, string? Note);
public sealed record BouquetDisposition(Guid BouquetId, string Action);
public sealed record CancelRequest(string Reason, List<BouquetDisposition>? Bouquets);
public sealed record ReturnGoodsRequest(Guid ProductId, decimal Quantity, string Reason);

[Route("api/orders")]
public sealed class OrdersController(FlowerShopDbContext db, BusinessCommands commands) : SecuredController(db)
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid? branchId, [FromQuery] string? status, CancellationToken ct)
    {
        RequireRole(Roles.Owner, Roles.Store);
        var query = Db.Orders.Include(x => x.Items).AsQueryable();
        if (ActiveRole == Roles.Store)
        {
            var own = await Db.Users.Where(x => x.Id == ActorId).Select(x => x.BranchId).SingleAsync(ct);
            if (branchId is not null && branchId != own) throw ApiError.Forbidden();
            query = query.Where(x => x.BranchId == own);
        }
        else if (branchId is not null) query = query.Where(x => x.BranchId == branchId);
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(x => x.Status == status);
        // Danh sách chỉ trình bày trạng thái; số tiền được xem trong chi tiết đơn.
        return Ok(await query.OrderByDescending(x => x.CreatedAt).Take(500).Select(x => new { x.Id, x.Code, x.BranchId, x.Status, x.CustomerName, x.CustomerPhone, x.ScheduledAt, x.CreatedAt, itemCount = x.Items.Count }).ToListAsync(ct));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var order = await Db.Orders.Include(x => x.Items).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw ApiError.NotFound();
        await CheckBranchAsync(order.BranchId, ct);
        var payments = await Db.Payments.Where(x => x.OrderId == id).OrderBy(x => x.CreatedAt).ToListAsync(ct);
        var refunds = await Db.Refunds.Where(x => x.OrderId == id).OrderBy(x => x.CreatedAt).ToListAsync(ct);
        return Ok(new { order, payments, refunds, paid = payments.Sum(x => x.Amount), refunded = refunds.Sum(x => x.Amount), due = order.Total - payments.Sum(x => x.Amount) + refunds.Sum(x => x.Amount) });
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateOrderRequest request, [FromHeader(Name = "Idempotency-Key")] string? key, CancellationToken ct)
    {
        await CheckBranchAsync(request.BranchId, ct);
        ValidateOrder(request);
        var id = await commands.RunAsync(ActorId, request.BranchId, "CreateOrder", key, request, async () =>
        {
            var productIds = request.Items.Where(x => x.ProductId is not null).Select(x => x.ProductId!.Value).Distinct().ToArray();
            var products = await Db.Products.Where(x => productIds.Contains(x.Id) && x.IsActive).ToDictionaryAsync(x => x.Id, ct);
            if (products.Count != productIds.Length) throw ApiError.BadRequest("Có sản phẩm không tồn tại hoặc đã ngừng kinh doanh.");
            var branchPrices = await Db.BranchPrices.Where(x => x.BranchId == request.BranchId && productIds.Contains(x.ProductId)).ToDictionaryAsync(x => x.ProductId, x => x.Price, ct);
            // PostgreSQL lưu thời điểm UTC; vẫn giữ đúng thời điểm khách chọn với múi giờ +07.
            var order = new SalesOrder { BranchId = request.BranchId, CustomerName = request.CustomerName?.Trim(), CustomerPhone = request.CustomerPhone?.Trim(), CustomerAddress = request.CustomerAddress?.Trim(), Note = request.Note?.Trim(), ScheduledAt = request.ScheduledAt?.ToUniversalTime(), ShippingFee = request.ShippingFee, CreatedBy = ActorId };
            foreach (var line in request.Items)
            {
                var product = line.ProductId is null ? null : products[line.ProductId.Value];
                if (product is not null && (line.IsCustomBouquet || line.UnitPrice is not null)) throw ApiError.BadRequest("Sản phẩm có sẵn lấy giá từ bảng giá cửa hàng.");
                if (product is null && (!line.IsCustomBouquet || line.UnitPrice is null || line.Quantity != 1 || string.IsNullOrWhiteSpace(line.Name))) throw ApiError.BadRequest("Bó thiết kế cần tên, đơn giá và số lượng 1 cho từng dòng.");
                if (product?.Kind == "Bouquet")
                {
                    var bouquet = await Db.Bouquets.SingleOrDefaultAsync(x => x.ProductId == product.Id, ct);
                    if (bouquet is null || bouquet.BranchId != request.BranchId || bouquet.Status != "Available" || line.Quantity != 1) throw ApiError.Conflict("Bó thành phẩm không sẵn có tại cửa hàng này.");
                }
                var price = line.UnitPrice ?? branchPrices.GetValueOrDefault(product!.Id, product.Price);
                BusinessCommands.Money(price);
                if (price > 0 && line.Quantity > 9999999999999999.99m / price) throw ApiError.BadRequest("Thành tiền vượt giới hạn cho phép.");
                var subtotal = line.Quantity * price;
                var discount = line.DiscountPercent is null ? line.Discount : subtotal * line.DiscountPercent.Value / 100m;
                if (decimal.Round(discount, 2) != discount) throw ApiError.BadRequest("Tiền giảm có quá 2 chữ số thập phân; cần xác nhận quy tắc làm tròn.");
                if (discount > subtotal) throw ApiError.BadRequest("Giảm giá không được lớn hơn tiền dòng.");
                var name = product?.Name ?? line.Name?.Trim() ?? "Bó thiết kế";
                var unit = product?.Unit ?? line.Unit?.Trim() ?? "bó";
                order.Items.Add(new SalesOrderItem { SalesOrderId = order.Id, ProductId = product?.Id, NameSnapshot = name, SkuSnapshot = product?.Sku, Quantity = line.Quantity, UnitSnapshot = unit, UnitPrice = price, Discount = discount, DiscountPercent = line.DiscountPercent, DiscountReason = line.DiscountReason?.Trim(), Note = line.Note?.Trim(), IsCustomBouquet = line.IsCustomBouquet || product is null });
            }
            // Từ chối số tiền lẻ chưa có quy tắc làm tròn được xác nhận thay vì tự làm tròn.
            if (order.Items.Any(x => decimal.Round(x.Quantity * x.UnitPrice, 2) != x.Quantity * x.UnitPrice)) throw ApiError.BadRequest("Thành tiền có quá 2 chữ số thập phân; quy tắc làm tròn cần được xác nhận.");
            order.Total = order.Items.Sum(x => x.Quantity * x.UnitPrice - x.Discount) + order.ShippingFee;
            BusinessCommands.Money(order.Total);
            var number = await Db.Branches.Where(x => x.Id == request.BranchId).Select(x => x.NextOrderNumber).SingleAsync(ct);
            await Db.Branches.Where(x => x.Id == request.BranchId).ExecuteUpdateAsync(x => x.SetProperty(b => b.NextOrderNumber, number + 1), ct);
            var prefix = await Db.Branches.Where(x => x.Id == request.BranchId).Select(x => x.Code).SingleAsync(ct);
            order.Code = $"{prefix}-{number:000}";
            Db.Orders.Add(order); Audit("CreateOrder", "SalesOrder", order.Id, order.BranchId, order.Code);
            return order.Id;
        }, ct);
        return Created($"/api/orders/{id}", await Db.Orders.Include(x => x.Items).SingleAsync(x => x.Id == id, ct));
    }

    [HttpPost("{id:guid}/payments")]
    public async Task<IActionResult> Payment(Guid id, AddPaymentRequest request, [FromHeader(Name = "Idempotency-Key")] string? key, CancellationToken ct)
    {
        var branch = await Db.Orders.Where(x => x.Id == id).Select(x => (Guid?)x.BranchId).SingleOrDefaultAsync(ct) ?? throw ApiError.NotFound();
        await CheckBranchAsync(branch, ct);
        if (request.Method is not ("Cash" or "Transfer") || request.Amount <= 0) throw ApiError.BadRequest("Phương thức hoặc số tiền thanh toán không hợp lệ.");
        BusinessCommands.Money(request.Amount);
        var result = await commands.RunAsync(ActorId, branch, "Payment", key, new { id, request }, async () =>
        {
            var order = await Db.Orders.SingleAsync(x => x.Id == id, ct);
            if (order.Status == "Cancelled") throw ApiError.Conflict("Không ghi thu mới cho đơn đã hủy.");
            var paid = await Db.Payments.Where(x => x.OrderId == id).SumAsync(x => (decimal?)x.Amount, ct) ?? 0;
            var refunded = await Db.Refunds.Where(x => x.OrderId == id).SumAsync(x => (decimal?)x.Amount, ct) ?? 0;
            if (paid - refunded + request.Amount > order.Total) throw ApiError.Conflict("Khoản thanh toán vượt số tiền phải thu.");
            var payment = new Payment { OrderId = id, Method = request.Method, Amount = request.Amount, Note = request.Note?.Trim(), ActorId = ActorId };
            Db.Payments.Add(payment); Audit("Payment", "Payment", payment.Id, order.BranchId, $"order={id}; method={request.Method}"); return payment.Id;
        }, ct);
        return Ok(await Db.Payments.SingleAsync(x => x.Id == result, ct));
    }

    [HttpPost("{id:guid}/deliver")]
    public async Task<IActionResult> Deliver(Guid id, [FromHeader(Name = "Idempotency-Key")] string? key, CancellationToken ct)
    {
        var branch = await Db.Orders.Where(x => x.Id == id).Select(x => (Guid?)x.BranchId).SingleOrDefaultAsync(ct) ?? throw ApiError.NotFound();
        await CheckBranchAsync(branch, ct);
        await commands.RunAsync(ActorId, branch, "DeliverOrder", key, new { id }, async () =>
        {
            var order = await Db.Orders.Include(x => x.Items).SingleAsync(x => x.Id == id, ct);
            if (order.Status != "Confirmed") throw ApiError.Conflict("Chỉ đơn đã xác nhận mới được giao.");
            foreach (var line in order.Items)
            {
                var bouquet = line.IsCustomBouquet
                    ? await Db.Bouquets.SingleOrDefaultAsync(x => x.OrderItemId == line.Id && x.Status == "ReadyForOrder", ct)
                    : line.ProductId is null ? null : await Db.Bouquets.SingleOrDefaultAsync(x => x.ProductId == line.ProductId, ct);
                if (line.IsCustomBouquet && bouquet is null) throw ApiError.Conflict("Bó thiết kế chưa được xác nhận làm xong.");
                if (bouquet is not null)
                {
                    if (bouquet.BranchId != branch || (!line.IsCustomBouquet && bouquet.Status != "Available")) throw ApiError.Conflict("Bó thành phẩm không còn sẵn có.");
                    bouquet.Status = "Sold";
                }
                var productId = bouquet?.ProductId ?? line.ProductId ?? throw ApiError.Conflict("Dòng hàng không có sản phẩm hợp lệ.");
                await commands.ChangeStock(branch, productId, -line.Quantity, "Sale", order.Id, ActorId, order.Code, ct);
            }
            order.Status = "Delivered"; order.DeliveredAt = DateTimeOffset.UtcNow;
            Audit("DeliverOrder", "SalesOrder", order.Id, order.BranchId, order.Code); return order.Id;
        }, ct);
        return Ok(await Db.Orders.Include(x => x.Items).SingleAsync(x => x.Id == id, ct));
    }

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancelRequest request, [FromHeader(Name = "Idempotency-Key")] string? key, CancellationToken ct)
    {
        RequireRole(Roles.Owner);
        if (string.IsNullOrWhiteSpace(request.Reason)) throw ApiError.BadRequest("Cần nhập lý do hủy.");
        var branch = await Db.Orders.Where(x => x.Id == id).Select(x => (Guid?)x.BranchId).SingleOrDefaultAsync(ct) ?? throw ApiError.NotFound();
        await CheckBranchAsync(branch, ct);
        await commands.RunAsync(ActorId, branch, "CancelOrder", key, new { id, request }, async () =>
        {
            var order = await Db.Orders.Include(x => x.Items).SingleAsync(x => x.Id == id, ct);
            if (order.Status == "Cancelled") throw ApiError.Conflict("Đơn đã hủy.");
            var itemIds = order.Items.Select(x => x.Id).ToArray();
            var ready = await Db.Bouquets.Where(x => x.OrderItemId != null && itemIds.Contains(x.OrderItemId.Value) && x.Status == "ReadyForOrder").ToListAsync(ct);
            var dispositions = request.Bouquets ?? [];
            if (dispositions.Count != ready.Count || dispositions.Select(x => x.BouquetId).Distinct().Count() != ready.Count || ready.Any(b => !dispositions.Any(d => d.BouquetId == b.Id && (d.Action == "Release" || d.Action == "Damage")))) throw ApiError.BadRequest("Cần chọn bán lại hoặc hư hỏng cho từng bó theo đơn đã làm xong.");
            foreach (var bouquet in ready)
            {
                var disposition = dispositions.Single(x => x.BouquetId == bouquet.Id);
                if (disposition.Action == "Release") bouquet.Status = "Available";
                else { bouquet.Status = "Damaged"; await commands.ChangeStock(branch, bouquet.ProductId, -1, "Damage", id, ActorId, request.Reason, ct); }
            }
            order.Status = "Cancelled"; order.CancelledAt = DateTimeOffset.UtcNow; Audit("CancelOrder", "SalesOrder", id, branch, request.Reason.Trim()); return id;
        }, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/refunds")]
    public async Task<IActionResult> Refund(Guid id, RefundRequest request, [FromHeader(Name = "Idempotency-Key")] string? key, CancellationToken ct)
    {
        RequireRole(Roles.Owner);
        var branch = await Db.Orders.Where(x => x.Id == id).Select(x => (Guid?)x.BranchId).SingleOrDefaultAsync(ct) ?? throw ApiError.NotFound();
        await CheckBranchAsync(branch, ct);
        if (request.Method is not ("Cash" or "Transfer") || request.Amount <= 0) throw ApiError.BadRequest("Phương thức hoặc số tiền hoàn không hợp lệ.");
        BusinessCommands.Money(request.Amount);
        var refundId = await commands.RunAsync(ActorId, branch, "Refund", key, new { id, request }, async () =>
        {
            var paid = await Db.Payments.Where(x => x.OrderId == id).SumAsync(x => (decimal?)x.Amount, ct) ?? 0;
            var refunded = await Db.Refunds.Where(x => x.OrderId == id).SumAsync(x => (decimal?)x.Amount, ct) ?? 0;
            if (refunded + request.Amount > paid) throw ApiError.Conflict("Số tiền hoàn vượt số tiền đã thu.");
            var refund = new PaymentRefund { OrderId = id, Method = request.Method, Amount = request.Amount, Note = request.Note?.Trim(), ActorId = ActorId };
            Db.Refunds.Add(refund); Audit("Refund", "PaymentRefund", refund.Id, branch, $"order={id}"); return refund.Id;
        }, ct);
        return Ok(await Db.Refunds.SingleAsync(x => x.Id == refundId, ct));
    }

    [HttpPost("{id:guid}/returns")]
    public async Task<IActionResult> ReturnGoods(Guid id, ReturnGoodsRequest request, [FromHeader(Name = "Idempotency-Key")] string? key, CancellationToken ct)
    {
        RequireRole(Roles.Owner);
        BusinessCommands.Quantity(request.Quantity);
        if (string.IsNullOrWhiteSpace(request.Reason)) throw ApiError.BadRequest("Cần ghi rõ lý do và tình trạng hàng thực nhận.");
        var branch = await Db.Orders.Where(x => x.Id == id).Select(x => (Guid?)x.BranchId).SingleOrDefaultAsync(ct) ?? throw ApiError.NotFound();
        await CheckBranchAsync(branch, ct);
        var result = await commands.RunAsync(ActorId, branch, "ReturnGoods", key, new { id, request }, async () =>
        {
            var order = await Db.Orders.Include(x => x.Items).SingleAsync(x => x.Id == id, ct);
            if (order.DeliveredAt is null) throw ApiError.Conflict("Chỉ nhận lại hàng từ đơn đã giao.");
            var delivered = -(await Db.InventoryTransactions.Where(x => x.ReferenceId == id && x.ProductId == request.ProductId && x.Type == "Sale").SumAsync(x => (decimal?)x.Change, ct) ?? 0);
            var returned = await Db.InventoryTransactions.Where(x => x.ReferenceId == id && x.ProductId == request.ProductId && x.Type == "Return").SumAsync(x => (decimal?)x.Change, ct) ?? 0;
            if (request.Quantity > delivered - returned) throw ApiError.Conflict("Số lượng nhận lại vượt số hàng đã giao chưa được nhận lại.");
            var bouquet = await Db.Bouquets.SingleOrDefaultAsync(x => x.ProductId == request.ProductId, ct);
            if (bouquet is not null)
            {
                if (bouquet.Status != "Sold" || request.Quantity != 1) throw ApiError.Conflict("Bó thành phẩm không thể nhận lại ở trạng thái hiện tại.");
                bouquet.Status = "Available";
            }
            await commands.ChangeStock(branch, request.ProductId, request.Quantity, "Return", id, ActorId, request.Reason.Trim(), ct);
            Audit("ReturnGoods", "SalesOrder", id, branch, $"product={request.ProductId}; quantity={request.Quantity}; reason={request.Reason.Trim()}");
            return id;
        }, ct);
        return Ok(new { orderId = result });
    }

    private static void ValidateOrder(CreateOrderRequest request)
    {
        if (request.Items is null || request.Items.Count is < 1 or > 100 || request.ShippingFee < 0) throw ApiError.BadRequest("Đơn hàng phải có 1–100 dòng và phí giao không âm.");
        BusinessCommands.Money(request.ShippingFee);
        foreach (var item in request.Items)
        {
            BusinessCommands.Quantity(item.Quantity); BusinessCommands.Money(item.Discount);
            if (item.ProductId is null && !item.IsCustomBouquet && string.IsNullOrWhiteSpace(item.Name)) throw ApiError.BadRequest("Dòng bó thiết kế cần tên hoặc đánh dấu IsCustomBouquet.");
            if (item.UnitPrice is < 0 || (item.UnitPrice is not null && decimal.Round(item.UnitPrice.Value, 2) != item.UnitPrice)) throw ApiError.BadRequest("Đơn giá không hợp lệ.");
            if (item.DiscountPercent is < 0 or > 100 || (item.DiscountPercent is not null && decimal.Round(item.DiscountPercent.Value, 2) != item.DiscountPercent.Value) || (item.Discount > 0 && item.DiscountPercent is > 0)) throw ApiError.BadRequest("Phần trăm giảm giá không hợp lệ hoặc trùng với giảm theo số tiền.");
            if ((item.Discount > 0 || item.DiscountPercent is > 0) && string.IsNullOrWhiteSpace(item.DiscountReason)) throw ApiError.BadRequest("Giảm giá phải có lý do.");
        }
    }
}
