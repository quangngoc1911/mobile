using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace FlowerShop.Infrastructure.Storage;

public sealed class StorageOptions { public string RootPath { get; set; } = "storage"; }
public sealed record StoredFile(string StorageKey, string Checksum);
public interface IFileStorage
{
    Task<StoredFile> SaveAsync(string extension, Stream content, CancellationToken ct);
    Task<Stream?> OpenReadAsync(string storageKey, CancellationToken ct);
    Task DeleteAsync(string storageKey);
}

public sealed class LocalFileStorage(IOptions<StorageOptions> options) : IFileStorage
{
    private readonly string _root = Path.GetFullPath(options.Value.RootPath);

    public async Task<StoredFile> SaveAsync(string extension, Stream content, CancellationToken ct)
    {
        // Tên lưu do server tạo, không dùng tên gốc làm đường dẫn.
        var key = $"{DateTime.UtcNow:yyyy/MM/dd}/{Guid.NewGuid():N}{extension}";
        var path = Resolve(key) ?? throw new InvalidOperationException("Đường dẫn lưu trữ không hợp lệ.");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[81920];
            int read;
            while ((read = await content.ReadAsync(buffer, ct)) > 0)
            {
                sha.AppendData(buffer, 0, read);
                await output.WriteAsync(buffer.AsMemory(0, read), ct);
            }
            await output.FlushAsync(ct);
            return new(key, Convert.ToHexString(sha.GetHashAndReset()));
        }
        catch { File.Delete(path); throw; }
    }

    public Task<Stream?> OpenReadAsync(string storageKey, CancellationToken ct)
    {
        var path = Resolve(storageKey);
        return Task.FromResult<Stream?>(path is not null && File.Exists(path) ? File.OpenRead(path) : null);
    }

    public Task DeleteAsync(string storageKey)
    {
        var path = Resolve(storageKey);
        if (path is not null) File.Delete(path);
        return Task.CompletedTask;
    }

    private string? Resolve(string key)
    {
        var path = Path.GetFullPath(Path.Combine(_root, key.Replace('/', Path.DirectorySeparatorChar)));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return path.StartsWith(Path.TrimEndingDirectorySeparator(_root) + Path.DirectorySeparatorChar, comparison) ? path : null;
    }
}
