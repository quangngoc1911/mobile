using FlowerShop.Api.Modules.Common;
using FlowerShop.Domain.Entities;
using FlowerShop.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FlowerShop.Api.Modules.Products;

public sealed record CreateCategoryRequest(string Name);
public sealed record CreateProductRequest(string Name, string Kind, string Unit, string? Variant, Guid? CategoryId, decimal Price, decimal LowStockThreshold);
public sealed record UpdateProductRequest(string Name, string Unit, string? Variant, Guid? CategoryId, decimal Price, decimal LowStockThreshold, bool IsActive);
public sealed record SetBranchPriceRequest(Guid BranchId, decimal Price);

[Route("api/products")]
public sealed class ProductsController(FlowerShopDbContext db) : SecuredController(db)
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid? branchId, [FromQuery] bool includeInactive = false, CancellationToken ct = default)
    {
        RequireRole(Roles.Owner, Roles.Store);
        var selectedBranch = await ResolveBranch(branchId, ct);
        var products = Db.Products.AsQueryable();
        if (!includeInactive) products = products.Where(x => x.IsActive);
        products = products.Where(x => x.Kind != "Bouquet" || Db.Bouquets.Any(b => b.ProductId == x.Id && b.Status == "Available" && (selectedBranch == null || b.BranchId == selectedBranch)));
        var rows = await products.OrderBy(x => x.Name).ToListAsync(ct);
        var prices = selectedBranch is null ? [] : await Db.BranchPrices.Where(x => x.BranchId == selectedBranch).ToDictionaryAsync(x => x.ProductId, x => x.Price, ct);
        var balances = selectedBranch is null ? [] : await Db.Balances.Where(x => x.BranchId == selectedBranch).ToDictionaryAsync(x => x.ProductId, x => x.Quantity, ct);
        return Ok(rows.Select(x => new { x.Id, x.Sku, x.Name, x.Kind, x.Unit, x.Variant, x.CategoryId, Price = prices.GetValueOrDefault(x.Id, x.Price), x.LowStockThreshold, Quantity = balances.GetValueOrDefault(x.Id), x.IsActive }));
    }

    [HttpGet("categories")]
    public async Task<IActionResult> Categories(CancellationToken ct)
    {
        RequireRole(Roles.Owner, Roles.Store);
        return Ok(await Db.Categories.OrderBy(x => x.Name).Select(x => new { x.Id, x.Name }).ToListAsync(ct));
    }

    [HttpGet("categories/{id:guid}")]
    public async Task<IActionResult> GetCategory(Guid id, CancellationToken ct)
    {
        RequireRole(Roles.Owner, Roles.Store);
        var category = await Db.Categories.Where(x => x.Id == id).Select(x => new { x.Id, x.Name }).SingleOrDefaultAsync(ct)
            ?? throw ApiError.NotFound();
        return Ok(category);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, [FromQuery] Guid? branchId, CancellationToken ct)
    {
        RequireRole(Roles.Owner, Roles.Store);
        var selectedBranch = await ResolveBranch(branchId, ct);
        var product = await Db.Products.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw ApiError.NotFound();
        if (product.Kind == "Bouquet")
        {
            // Không để chi tiết sản phẩm làm lộ bó theo đơn hoặc bó của cửa hàng khác.
            var visible = await Db.Bouquets.AnyAsync(x => x.ProductId == id && x.Status == "Available" && (selectedBranch == null || x.BranchId == selectedBranch), ct);
            if (!visible) throw ApiError.NotFound();
        }
        var price = selectedBranch is null ? null : await Db.BranchPrices.Where(x => x.BranchId == selectedBranch && x.ProductId == id).Select(x => (decimal?)x.Price).SingleOrDefaultAsync(ct);
        var quantity = selectedBranch is null ? null : await Db.Balances.Where(x => x.BranchId == selectedBranch && x.ProductId == id).Select(x => (decimal?)x.Quantity).SingleOrDefaultAsync(ct);
        return Ok(new { product.Id, product.Sku, product.Name, product.Kind, product.Unit, product.Variant, product.CategoryId, Price = price ?? product.Price, product.LowStockThreshold, Quantity = quantity ?? 0, product.IsActive });
    }

    [HttpPost("categories")]
    public async Task<IActionResult> CreateCategory(CreateCategoryRequest request, CancellationToken ct)
    {
        RequireRole(Roles.Owner);
        var name = request.Name?.Trim() ?? "";
        if (name.Length is < 1 or > 200) throw ApiError.BadRequest("Tên danh mục không hợp lệ.");
        if (await Db.Categories.AnyAsync(x => x.Name.ToUpper() == name.ToUpper(), ct)) throw ApiError.Conflict("Danh mục đã tồn tại.");
        var category = new ProductCategory { Name = name };
        Db.Categories.Add(category); Audit("CreateCategory", "ProductCategory", category.Id);
        await Db.SaveChangesAsync(ct);
        return Created($"/api/products/categories/{category.Id}", new { category.Id, category.Name });
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateProductRequest request, CancellationToken ct)
    {
        RequireRole(Roles.Owner, Roles.Store);
        Validate(request.Name, request.Kind, request.Unit, request.Price, request.LowStockThreshold);
        if (request.Kind.Trim().Equals("Bouquet", StringComparison.OrdinalIgnoreCase)) throw ApiError.BadRequest("Bó thành phẩm được tạo bằng thao tác làm bó.");
        if (request.CategoryId is not null && !await Db.Categories.AnyAsync(x => x.Id == request.CategoryId, ct)) throw ApiError.BadRequest("Danh mục không tồn tại.");
        var product = new Product { Sku = await NewSku(ct), Name = request.Name.Trim(), Kind = request.Kind.Trim(), Unit = request.Unit.Trim(), Variant = request.Variant?.Trim(), CategoryId = request.CategoryId, Price = request.Price, LowStockThreshold = request.LowStockThreshold };
        Db.Products.Add(product); Audit("CreateProduct", "Product", product.Id);
        await Db.SaveChangesAsync(ct);
        return Created($"/api/products/{product.Id}", product);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdateProductRequest request, CancellationToken ct)
    {
        RequireRole(Roles.Owner);
        Validate(request.Name, "Product", request.Unit, request.Price, request.LowStockThreshold);
        var product = await Db.Products.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw ApiError.NotFound();
        if (request.CategoryId is not null && !await Db.Categories.AnyAsync(x => x.Id == request.CategoryId, ct)) throw ApiError.BadRequest("Danh mục không tồn tại.");
        product.Name = request.Name.Trim(); product.Unit = request.Unit.Trim(); product.Variant = request.Variant?.Trim(); product.CategoryId = request.CategoryId; product.Price = request.Price; product.LowStockThreshold = request.LowStockThreshold; product.IsActive = request.IsActive;
        Audit("UpdateProduct", "Product", id); await Db.SaveChangesAsync(ct); return NoContent();
    }

    [HttpPut("{id:guid}/branch-price")]
    public async Task<IActionResult> SetBranchPrice(Guid id, SetBranchPriceRequest request, CancellationToken ct)
    {
        RequireRole(Roles.Owner);
        BusinessCommands.Money(request.Price);
        if (!await Db.Products.AnyAsync(x => x.Id == id, ct) || !await Db.Branches.AnyAsync(x => x.Id == request.BranchId && x.IsActive, ct)) throw ApiError.BadRequest("Sản phẩm hoặc cửa hàng không hợp lệ.");
        var price = await Db.BranchPrices.SingleOrDefaultAsync(x => x.ProductId == id && x.BranchId == request.BranchId, ct);
        if (price is null) Db.BranchPrices.Add(new BranchPrice { ProductId = id, BranchId = request.BranchId, Price = request.Price }); else price.Price = request.Price;
        Audit("SetBranchPrice", "Product", id, request.BranchId, request.Price.ToString("0.00")); await Db.SaveChangesAsync(ct); return NoContent();
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
    private async Task<string> NewSku(CancellationToken ct)
    {
        string sku;
        do sku = $"SKU-{Guid.NewGuid():N}"[..16].ToUpperInvariant(); while (await Db.Products.AnyAsync(x => x.Sku == sku, ct));
        return sku;
    }
    private static void Validate(string name, string kind, string unit, decimal price, decimal threshold)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 500 || string.IsNullOrWhiteSpace(kind) || kind.Length > 100 || string.IsNullOrWhiteSpace(unit) || unit.Length > 100 || threshold < 0 || threshold >= 1_000_000_000_000_000m || decimal.Round(threshold, 3) != threshold) throw ApiError.BadRequest("Thông tin sản phẩm không hợp lệ.");
        BusinessCommands.Money(price);
    }
}
