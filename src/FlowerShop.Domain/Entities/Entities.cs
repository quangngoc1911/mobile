namespace FlowerShop.Domain.Entities;

// Các chứng từ đã xác nhận được giữ lại để truy xuất lịch sử tiền và hàng.
public static class Roles
{
    public const string Admin = "Admin";
    public const string Owner = "Owner";
    public const string Store = "Store";
    public static readonly string[] All = [Admin, Owner, Store];
}

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Username { get; set; } = "";
    public string NormalizedUsername { get; set; } = "";
    public string FullName { get; set; } = "";
    public string? Phone { get; set; }
    public string PasswordHash { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public Guid? BranchId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<UserRole> Roles { get; set; } = [];
}

public class UserRole { public Guid UserId { get; set; } public string Role { get; set; } = ""; }
public class UserSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string RefreshTokenHash { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
public class Branch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Address { get; set; }
    public bool IsActive { get; set; } = true;
    public long NextOrderNumber { get; set; } = 1;
}
public class ProductCategory { public Guid Id { get; set; } = Guid.NewGuid(); public string Name { get; set; } = ""; }
public class Product
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Sku { get; set; } = "";
    public string Name { get; set; } = "";
    public Guid? CategoryId { get; set; }
    // Material là hoa/vật liệu; Bouquet là thành phẩm có SKU riêng.
    public string Kind { get; set; } = "Material";
    public string Unit { get; set; } = "cành";
    public string? Variant { get; set; }
    public decimal Price { get; set; }
    public decimal LowStockThreshold { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
public class BranchPrice { public Guid BranchId { get; set; } public Guid ProductId { get; set; } public decimal Price { get; set; } }
public class InventoryBalance { public Guid BranchId { get; set; } public Guid ProductId { get; set; } public decimal Quantity { get; set; } }
public class InventoryTransaction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BranchId { get; set; }
    public Guid ProductId { get; set; }
    public decimal Change { get; set; }
    public string Type { get; set; } = "";
    public Guid? ReferenceId { get; set; }
    public string? Note { get; set; }
    public Guid ActorId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
public class GoodsReceipt
{
    public Guid Id { get; set; } = Guid.NewGuid(); public Guid BranchId { get; set; }
    public string? SupplierName { get; set; } public string? Note { get; set; }
    public Guid ActorId { get; set; } public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<GoodsReceiptItem> Items { get; set; } = [];
}
public class GoodsReceiptItem { public Guid Id { get; set; } = Guid.NewGuid(); public Guid GoodsReceiptId { get; set; } public Guid ProductId { get; set; } public decimal Quantity { get; set; } public decimal UnitCost { get; set; } }
public class SalesOrder
{
    public Guid Id { get; set; } = Guid.NewGuid(); public Guid BranchId { get; set; } public string Code { get; set; } = "";
    public string Status { get; set; } = "Confirmed"; public string? CustomerName { get; set; } public string? CustomerPhone { get; set; }
    public string? CustomerAddress { get; set; } public string? Note { get; set; } public DateTimeOffset? ScheduledAt { get; set; }
    public decimal ShippingFee { get; set; } public decimal Total { get; set; }
    public Guid CreatedBy { get; set; } public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DeliveredAt { get; set; } public DateTimeOffset? CancelledAt { get; set; }
    public List<SalesOrderItem> Items { get; set; } = [];
}
public class SalesOrderItem
{
    public Guid Id { get; set; } = Guid.NewGuid(); public Guid SalesOrderId { get; set; }
    public Guid? ProductId { get; set; } public string NameSnapshot { get; set; } = ""; public string? SkuSnapshot { get; set; }
    public decimal Quantity { get; set; } public string UnitSnapshot { get; set; } = "";
    public decimal UnitPrice { get; set; } public decimal Discount { get; set; } public decimal? DiscountPercent { get; set; } public string? DiscountReason { get; set; }
    public string? Note { get; set; } public bool IsCustomBouquet { get; set; }
}
public class Bouquet
{
    public Guid Id { get; set; } = Guid.NewGuid(); public Guid BranchId { get; set; } public Guid ProductId { get; set; }
    public Guid? OrderItemId { get; set; } public string Status { get; set; } = "Available";
    public Guid CreatedBy { get; set; } public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<BouquetMaterial> Materials { get; set; } = [];
}
public class BouquetMaterial { public Guid Id { get; set; } = Guid.NewGuid(); public Guid BouquetId { get; set; } public Guid ProductId { get; set; } public decimal Quantity { get; set; } }
public class Payment
{
    public Guid Id { get; set; } = Guid.NewGuid(); public Guid OrderId { get; set; } public string Method { get; set; } = "";
    public decimal Amount { get; set; } public string? Note { get; set; } public Guid ActorId { get; set; } public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
public class PaymentRefund
{
    public Guid Id { get; set; } = Guid.NewGuid(); public Guid OrderId { get; set; } public string Method { get; set; } = "";
    public decimal Amount { get; set; } public string? Note { get; set; } public Guid ActorId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
public class MediaFile
{
    public Guid Id { get; set; } = Guid.NewGuid(); public string StorageKey { get; set; } = ""; public string OriginalName { get; set; } = "";
    public string ContentType { get; set; } = ""; public long Length { get; set; } public string Checksum { get; set; } = "";
    public string TargetType { get; set; } = ""; public Guid TargetId { get; set; } public Guid? BranchId { get; set; }
    public Guid ActorId { get; set; } public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
public class AuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid(); public Guid? ActorId { get; set; } public string? ActiveRole { get; set; }
    public string Action { get; set; } = ""; public string EntityType { get; set; } = ""; public string EntityId { get; set; } = "";
    public Guid? BranchId { get; set; } public string? Details { get; set; } public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
public class CommandReceipt
{
    public Guid Id { get; set; } = Guid.NewGuid(); public Guid BranchId { get; set; } public Guid ActorId { get; set; }
    public string Key { get; set; } = ""; public string Operation { get; set; } = ""; public string RequestHash { get; set; } = "";
    public Guid ResultId { get; set; } public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
