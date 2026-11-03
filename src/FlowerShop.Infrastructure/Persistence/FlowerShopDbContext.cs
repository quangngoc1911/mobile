using FlowerShop.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FlowerShop.Infrastructure.Persistence;

public sealed class FlowerShopDbContext(DbContextOptions<FlowerShopDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<UserSession> Sessions => Set<UserSession>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<ProductCategory> Categories => Set<ProductCategory>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<BranchPrice> BranchPrices => Set<BranchPrice>();
    public DbSet<InventoryBalance> Balances => Set<InventoryBalance>();
    public DbSet<InventoryTransaction> InventoryTransactions => Set<InventoryTransaction>();
    public DbSet<GoodsReceipt> GoodsReceipts => Set<GoodsReceipt>();
    public DbSet<GoodsReceiptItem> GoodsReceiptItems => Set<GoodsReceiptItem>();
    public DbSet<SalesOrder> Orders => Set<SalesOrder>();
    public DbSet<SalesOrderItem> OrderItems => Set<SalesOrderItem>();
    public DbSet<Bouquet> Bouquets => Set<Bouquet>();
    public DbSet<BouquetMaterial> BouquetMaterials => Set<BouquetMaterial>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<PaymentRefund> Refunds => Set<PaymentRefund>();
    public DbSet<MediaFile> Media => Set<MediaFile>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<CommandReceipt> CommandReceipts => Set<CommandReceipt>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        // Hàng tồn không âm được bảo vệ ở cả tầng nghiệp vụ và database.
        model.Entity<InventoryBalance>().ToTable(t => t.HasCheckConstraint("ck_balance_nonnegative", "\"Quantity\" >= 0"));
        model.Entity<User>().HasIndex(x => x.NormalizedUsername).IsUnique();
        model.Entity<User>().HasMany(x => x.Roles).WithOne().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<User>().HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<UserRole>().HasKey(x => new { x.UserId, x.Role });
        model.Entity<UserSession>().HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<UserSession>().HasIndex(x => x.UserId);
        model.Entity<Branch>().HasIndex(x => x.Code).IsUnique();
        model.Entity<ProductCategory>().HasIndex(x => x.Name).IsUnique();
        model.Entity<Product>().HasIndex(x => x.Sku).IsUnique();
        model.Entity<Product>().HasOne<ProductCategory>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<BranchPrice>().HasKey(x => new { x.BranchId, x.ProductId });
        model.Entity<BranchPrice>().HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<BranchPrice>().HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<InventoryBalance>().HasKey(x => new { x.BranchId, x.ProductId });
        model.Entity<InventoryBalance>().HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<InventoryBalance>().HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<InventoryTransaction>().HasIndex(x => new { x.BranchId, x.ProductId, x.CreatedAt });
        model.Entity<InventoryTransaction>().HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<InventoryTransaction>().HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<GoodsReceipt>().HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<GoodsReceipt>().HasMany(x => x.Items).WithOne().HasForeignKey(x => x.GoodsReceiptId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<GoodsReceiptItem>().HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<SalesOrder>().HasIndex(x => x.Code).IsUnique();
        model.Entity<SalesOrder>().HasIndex(x => new { x.BranchId, x.CreatedAt });
        model.Entity<SalesOrder>().HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<SalesOrder>().HasMany(x => x.Items).WithOne().HasForeignKey(x => x.SalesOrderId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<SalesOrderItem>().HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<Bouquet>().HasIndex(x => x.ProductId).IsUnique();
        model.Entity<Bouquet>().HasIndex(x => x.OrderItemId).IsUnique();
        model.Entity<Bouquet>().HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<Bouquet>().HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<Bouquet>().HasOne<SalesOrderItem>().WithMany().HasForeignKey(x => x.OrderItemId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<Bouquet>().HasMany(x => x.Materials).WithOne().HasForeignKey(x => x.BouquetId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<BouquetMaterial>().HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<Payment>().HasOne<SalesOrder>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<PaymentRefund>().HasOne<SalesOrder>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<MediaFile>().HasIndex(x => new { x.TargetType, x.TargetId });
        model.Entity<AuditLog>().HasIndex(x => new { x.BranchId, x.CreatedAt });
        model.Entity<CommandReceipt>().HasIndex(x => new { x.BranchId, x.Operation, x.Key }).IsUnique();

        foreach (var entity in model.Model.GetEntityTypes())
        foreach (var property in entity.GetProperties())
        {
            if (property.ClrType == typeof(decimal) || property.ClrType == typeof(decimal?))
            {
                property.SetPrecision(18);
                property.SetScale(property.Name is "Quantity" or "Change" or "LowStockThreshold" ? 3 : 2);
            }
            if (property.ClrType == typeof(string) && property.GetMaxLength() is null)
                property.SetMaxLength(property.Name is "Details" or "Note" ? 4000 : 500);
        }
    }
}
