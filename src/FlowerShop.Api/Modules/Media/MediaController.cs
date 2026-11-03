using FlowerShop.Api.Modules.Common;
using FlowerShop.Domain.Entities;
using FlowerShop.Infrastructure.Persistence;
using FlowerShop.Infrastructure.Storage;
using ImageMagick;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FlowerShop.Api.Modules.Media;

[Route("api/media")]
public sealed class MediaController(FlowerShopDbContext db, IFileStorage storage) : SecuredController(db)
{
    private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase) { "image/jpeg", "image/png", "image/heic" };
    private const long MaxLength = 10 * 1024 * 1024;

    [HttpPost]
    [RequestSizeLimit(MaxLength + 64 * 1024)]
    public async Task<IActionResult> Upload([FromForm] IFormFile file, [FromForm] string targetType, [FromForm] Guid targetId, CancellationToken ct)
    {
        if (file is null || file.Length is <= 0 or > MaxLength || !Allowed.Contains(file.ContentType)) throw ApiError.BadRequest("Ảnh phải là JPEG, PNG hoặc HEIC và không quá 10 MB.");
        targetType = CanonicalTarget(targetType);
        var target = await ResolveTarget(targetType, targetId, ct);
        var extension = file.ContentType.ToLowerInvariant() switch { "image/jpeg" => ".jpg", "image/png" => ".png", _ => ".heic" };
        await using var stream = file.OpenReadStream();
        var header = new byte[64];
        var read = 0;
        while (read < header.Length)
        {
            var count = await stream.ReadAsync(header.AsMemory(read), ct);
            if (count == 0) break;
            read += count;
        }
        if (!MatchesFormat(header.AsSpan(0, read), file.ContentType)) throw ApiError.BadRequest("Nội dung ảnh không khớp định dạng được khai báo.");
        stream.Position = 0;
        try
        {
            // Ping đọc metadata ảnh mà không giải nén toàn bộ pixel vào bộ nhớ.
            using var image = new MagickImage();
            await image.PingAsync(stream, ct);
            var expected = file.ContentType.ToLowerInvariant() switch { "image/jpeg" => MagickFormat.Jpeg, "image/png" => MagickFormat.Png, _ => MagickFormat.Heic };
            if (image.Format != expected || image.Width == 0 || image.Height == 0)
                throw ApiError.BadRequest("Nội dung ảnh không khớp định dạng được khai báo.");
        }
        catch (MagickException)
        {
            throw ApiError.BadRequest("Không đọc được metadata ảnh; tệp có thể bị hỏng hoặc không đúng định dạng.");
        }
        stream.Position = 0;
        var stored = await storage.SaveAsync(extension, stream, ct);
        var originalName = Path.GetFileName(file.FileName);
        if (originalName.Length > 255) originalName = originalName[^255..];
        var media = new MediaFile { StorageKey = stored.StorageKey, OriginalName = originalName, ContentType = file.ContentType.ToLowerInvariant(), Length = file.Length, Checksum = stored.Checksum, TargetType = targetType, TargetId = targetId, BranchId = target.BranchId, ActorId = ActorId };
        try { Db.Media.Add(media); Audit("UploadMedia", "MediaFile", media.Id, target.BranchId, $"target={targetType}/{targetId}"); await Db.SaveChangesAsync(ct); }
        catch { await storage.DeleteAsync(stored.StorageKey); throw; }
        return Created($"/api/media/{media.Id}", new { media.Id, media.TargetType, media.TargetId, media.ContentType, media.Length });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Download(Guid id, CancellationToken ct)
    {
        var media = await Db.Media.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw ApiError.NotFound();
        await ResolveTarget(media.TargetType, media.TargetId, ct);
        var stream = await storage.OpenReadAsync(media.StorageKey, ct) ?? throw ApiError.NotFound();
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(stream, media.ContentType, fileDownloadName: "image" + Path.GetExtension(media.StorageKey), enableRangeProcessing: false);
    }

    [HttpGet("for/{targetType}/{targetId:guid}")]
    public async Task<IActionResult> List(string targetType, Guid targetId, CancellationToken ct)
    {
        targetType = CanonicalTarget(targetType);
        await ResolveTarget(targetType, targetId, ct);
        return Ok(await Db.Media.Where(x => x.TargetType == targetType && x.TargetId == targetId).OrderBy(x => x.CreatedAt).Select(x => new { x.Id, x.ContentType, x.Length, x.CreatedAt }).ToListAsync(ct));
    }

    private async Task<(Guid? BranchId, object Target)> ResolveTarget(string targetType, Guid id, CancellationToken ct)
    {
        var normalized = targetType.Trim();
        if (normalized.Equals("Product", StringComparison.OrdinalIgnoreCase))
        {
            var product = await Db.Products.FindAsync([id], ct) ?? throw ApiError.NotFound();
            if (product.Kind == "Bouquet")
            {
                var bouquet = await Db.Bouquets.SingleAsync(x => x.ProductId == id, ct);
                await CheckBranchAsync(bouquet.BranchId, ct); return (bouquet.BranchId, product);
            }
            RequireRole(Roles.Owner, Roles.Store); return (null, product);
        }
        if (normalized.Equals("Order", StringComparison.OrdinalIgnoreCase))
        {
            var order = await Db.Orders.FindAsync([id], ct) ?? throw ApiError.NotFound(); await CheckBranchAsync(order.BranchId, ct); return (order.BranchId, order);
        }
        if (normalized.Equals("Bouquet", StringComparison.OrdinalIgnoreCase))
        {
            var bouquet = await Db.Bouquets.FindAsync([id], ct) ?? throw ApiError.NotFound(); await CheckBranchAsync(bouquet.BranchId, ct); return (bouquet.BranchId, bouquet);
        }
        throw ApiError.BadRequest("targetType chỉ hỗ trợ Product, Order hoặc Bouquet.");
    }

    private static string CanonicalTarget(string? value) => value?.Trim().ToUpperInvariant() switch
    { "PRODUCT" => "Product", "ORDER" => "Order", "BOUQUET" => "Bouquet", _ => throw ApiError.BadRequest("Loại đối tượng ảnh không hợp lệ.") };

    private static bool MatchesFormat(ReadOnlySpan<byte> header, string contentType)
    {
        if (contentType.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase)) return header.Length >= 3 && header[0] == 0xff && header[1] == 0xd8 && header[2] == 0xff;
        if (contentType.Equals("image/png", StringComparison.OrdinalIgnoreCase)) return header.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        if (header.Length < 12 || !header.Slice(4, 4).SequenceEqual("ftyp"u8)) return false;
        for (var i = 8; i + 4 <= header.Length; i += 4)
            if (header.Slice(i, 4).SequenceEqual("heic"u8) || header.Slice(i, 4).SequenceEqual("heix"u8) || header.Slice(i, 4).SequenceEqual("hevc"u8) || header.Slice(i, 4).SequenceEqual("hevx"u8)) return true;
        return false;
    }
}
