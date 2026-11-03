using FlowerShop.Api.Modules.Common;
using FlowerShop.Domain.Entities;
using FlowerShop.Infrastructure.Authentication;
using FlowerShop.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FlowerShop.Api.Modules.Identity;

public sealed record LoginRequest(string Username, string Password, string ActiveRole);
public sealed record RefreshRequest(Guid SessionId, string RefreshToken, string ActiveRole);
public sealed record SwitchRoleRequest(string ActiveRole);

[Route("api/auth")]
public sealed class AuthController(FlowerShopDbContext db, AccountStore accounts, IPasswordService passwords, ITokenService tokens)
    : SecuredController(db)
{
    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrEmpty(request.Password) || string.IsNullOrWhiteSpace(request.ActiveRole))
            throw ApiError.BadRequest("Cần username, mật khẩu và vai trò sử dụng.");
        await using var transaction = await Db.Database.BeginTransactionAsync(ct);
        var normalized = AccountStore.Normalize(request.Username);
        // Đồng bộ tạo phiên với khóa hoặc đặt lại mật khẩu của cùng tài khoản.
        var user = await Db.Users.FromSqlInterpolated($"SELECT * FROM \"Users\" WHERE \"NormalizedUsername\" = {normalized} FOR UPDATE").SingleOrDefaultAsync(ct);
        if (user is not null) await Db.Entry(user).Collection(x => x.Roles).LoadAsync(ct);
        if (user is null || !user.IsActive || !passwords.Verify(user, user.PasswordHash, request.Password))
            return Unauthorized(new { message = "Thông tin đăng nhập không hợp lệ." });
        if (!user.Roles.Any(x => x.Role == request.ActiveRole)) throw ApiError.Forbidden();
        if (request.ActiveRole == Roles.Store && user.BranchId is null) throw ApiError.Forbidden();
        var raw = TokenRandomizer.CreateRefreshToken();
        var session = new UserSession { UserId = user.Id, RefreshTokenHash = tokens.HashRefreshToken(raw) };
        var pair = tokens.Create(user, session.Id, request.ActiveRole, raw);
        session.ExpiresAt = pair.RefreshExpiresAt;
        Db.Sessions.Add(session);
        Db.AuditLogs.Add(new AuditLog { ActorId = user.Id, ActiveRole = request.ActiveRole, Action = "Login", EntityType = "Session", EntityId = session.Id.ToString(), BranchId = user.BranchId });
        await Db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Ok(new { sessionId = session.Id, tokens = pair, user = PublicUser(user) });
    }

    [AllowAnonymous]
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(RefreshRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken) || string.IsNullOrWhiteSpace(request.ActiveRole))
            return Unauthorized();
        await using var transaction = await Db.Database.BeginTransactionAsync(ct);
        // Khóa phiên để hai yêu cầu đồng thời không thể cùng dùng một refresh token.
        var session = await Db.Sessions.FromSqlInterpolated($"SELECT * FROM \"Sessions\" WHERE \"Id\" = {request.SessionId} FOR UPDATE").SingleOrDefaultAsync(ct);
        if (session is null || session.RevokedAt is not null || session.ExpiresAt <= DateTimeOffset.UtcNow || session.RefreshTokenHash != tokens.HashRefreshToken(request.RefreshToken))
            return Unauthorized();
        var user = await accounts.FindByIdAsync(session.UserId, ct);
        if (user is null || !user.IsActive || !user.Roles.Any(x => x.Role == request.ActiveRole) || (request.ActiveRole == Roles.Store && user.BranchId is null))
            return Unauthorized();
        var raw = TokenRandomizer.CreateRefreshToken();
        var pair = tokens.Create(user, session.Id, request.ActiveRole, raw);
        session.RefreshTokenHash = tokens.HashRefreshToken(raw);
        session.ExpiresAt = pair.RefreshExpiresAt;
        await Db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Ok(new { sessionId = session.Id, tokens = pair, user = PublicUser(user) });
    }

    [HttpPost("switch-role")]
    public async Task<IActionResult> SwitchRole(SwitchRoleRequest request, CancellationToken ct)
    {
        var user = await accounts.FindByIdAsync(ActorId, ct) ?? throw ApiError.NotFound();
        if (!user.Roles.Any(x => x.Role == request.ActiveRole) || (request.ActiveRole == Roles.Store && user.BranchId is null)) throw ApiError.Forbidden();
        var pair = tokens.Create(user, SessionId, request.ActiveRole, "");
        Audit("SwitchRole", "Session", SessionId, user.BranchId, request.ActiveRole);
        await Db.SaveChangesAsync(ct);
        return Ok(new { pair.AccessToken, pair.AccessExpiresAt, activeRole = request.ActiveRole });
    }

    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var user = await accounts.FindByIdAsync(ActorId, ct) ?? throw ApiError.NotFound();
        return Ok(new { user = PublicUser(user), activeRole = ActiveRole });
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        var session = await Db.Sessions.SingleAsync(x => x.Id == SessionId && x.UserId == ActorId, ct);
        session.RevokedAt = DateTimeOffset.UtcNow;
        Audit("Logout", "Session", session.Id);
        await Db.SaveChangesAsync(ct);
        return NoContent();
    }

    private static object PublicUser(User user) => new { user.Id, user.Username, user.FullName, user.Phone, user.BranchId, roles = user.Roles.Select(x => x.Role) };
}
