using FlowerShop.Domain.Entities;
using FlowerShop.Infrastructure.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FlowerShop.Infrastructure.Persistence;

public static class DatabaseInitializer
{
    // Bootstrap là lệnh chạy một lần tại máy chủ, không phải API công khai.
    public static async Task InitializeFirstAccountAsync(IServiceProvider services, string username, string password, string fullName, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlowerShopDbContext>();
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(fullName) || password.Length < 12)
            throw new ArgumentException("Cần username, họ tên và mật khẩu có ít nhất 12 ký tự.");
        if ((await db.Database.GetPendingMigrationsAsync(ct)).Any())
            throw new InvalidOperationException("Cần áp dụng migration đã được xem xét trước khi bootstrap.");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(739102019)", ct);
        if (await db.Users.AnyAsync(ct))
            throw new InvalidOperationException("Database đã có tài khoản; không thể bootstrap lại.");
        var user = new User { Username = username.Trim(), NormalizedUsername = AccountStore.Normalize(username), FullName = fullName.Trim() };
        // Chỉ lưu password hash, tuyệt đối không ghi mật khẩu bootstrap vào log.
        user.PasswordHash = scope.ServiceProvider.GetRequiredService<IPasswordService>().Hash(user, password);
        user.Roles.Add(new UserRole { UserId = user.Id, Role = Roles.Admin });
        user.Roles.Add(new UserRole { UserId = user.Id, Role = Roles.Owner });
        db.Users.Add(user);
        db.AuditLogs.Add(new AuditLog { ActorId = user.Id, ActiveRole = Roles.Admin, Action = "Bootstrap", EntityType = "User", EntityId = user.Id.ToString() });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
}
