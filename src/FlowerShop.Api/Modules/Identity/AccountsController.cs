using FlowerShop.Api.Modules.Common;
using FlowerShop.Domain.Entities;
using FlowerShop.Infrastructure.Authentication;
using FlowerShop.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FlowerShop.Api.Modules.Identity;

public sealed record CreateAccountRequest(string Username, string Password, string FullName, string? Phone, Guid? BranchId, string[] Roles);
public sealed record UpdateAccountRequest(string FullName, string? Phone, Guid? BranchId);
public sealed record SetRolesRequest(string[] Roles);
public sealed record SetActiveRequest(bool IsActive);
public sealed record ResetPasswordRequest(string Password);

[Route("api/accounts")]
public sealed class AccountsController(FlowerShopDbContext db, IPasswordService passwords) : SecuredController(db)
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        RequireRole(Roles.Admin, Roles.Owner);
        var query = Db.Users.Include(x => x.Roles).AsQueryable();
        if (ActiveRole == Roles.Owner) query = query.Where(x => x.Roles.Any(r => r.Role == Roles.Store) && !x.Roles.Any(r => r.Role == Roles.Admin || r.Role == Roles.Owner));
        var users = await query.OrderBy(x => x.Username).ToListAsync(ct);
        return Ok(users.Select(View));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        RequireRole(Roles.Admin, Roles.Owner);
        var user = await Db.Users.Include(x => x.Roles).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw ApiError.NotFound();
        EnsureCanManage(user);
        return Ok(View(user));
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateAccountRequest request, CancellationToken ct)
    {
        RequireRole(Roles.Admin, Roles.Owner);
        ValidateNames(request.Username, request.FullName);
        ValidatePassword(request.Password);
        await ValidateRolesAndBranch(request.Roles, request.BranchId, ct);
        if (ActiveRole == Roles.Owner && (request.Roles.Length != 1 || request.Roles[0] != Roles.Store)) throw ApiError.Forbidden();
        var normalized = AccountStore.Normalize(request.Username);
        if (await Db.Users.AnyAsync(x => x.NormalizedUsername == normalized, ct)) throw ApiError.Conflict("Username đã tồn tại.");
        var user = new User { Username = request.Username.Trim(), NormalizedUsername = normalized, FullName = request.FullName.Trim(), Phone = request.Phone?.Trim(), BranchId = request.BranchId };
        user.PasswordHash = passwords.Hash(user, request.Password);
        user.Roles = request.Roles.Select(x => new UserRole { UserId = user.Id, Role = x }).ToList();
        Db.Users.Add(user);
        Audit("CreateAccount", "User", user.Id, user.BranchId, string.Join(",", request.Roles));
        await Db.SaveChangesAsync(ct);
        return Created($"/api/accounts/{user.Id}", View(user));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdateAccountRequest request, CancellationToken ct)
    {
        RequireRole(Roles.Admin, Roles.Owner);
        ValidateNames("placeholder", request.FullName);
        await using var transaction = await Db.Database.BeginTransactionAsync(ct);
        var user = await LockUser(id, ct);
        EnsureCanManage(user);
        await ValidateRolesAndBranch(user.Roles.Select(x => x.Role).ToArray(), request.BranchId, ct);
        var previousBranch = user.BranchId;
        user.FullName = request.FullName.Trim(); user.Phone = request.Phone?.Trim(); user.BranchId = request.BranchId;
        Audit("UpdateAccount", "User", user.Id, user.BranchId, $"previousBranch={previousBranch}");
        await Db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Ok(View(user));
    }

    [HttpPut("{id:guid}/roles")]
    public async Task<IActionResult> SetRoles(Guid id, SetRolesRequest request, CancellationToken ct)
    {
        RequireRole(Roles.Admin);
        await using var transaction = await Db.Database.BeginTransactionAsync(ct);
        await LockAccountAdministration(ct);
        var user = await LockUser(id, ct);
        await ValidateRolesAndBranch(request.Roles, user.BranchId, ct);
        if (user.IsActive) await EnsureAdministrativeCoverage(user, request.Roles, ct);
        var oldRoles = string.Join(",", user.Roles.Select(x => x.Role));
        Db.UserRoles.RemoveRange(user.Roles.Where(x => !request.Roles.Contains(x.Role)));
        foreach (var role in request.Roles.Where(x => user.Roles.All(r => r.Role != x))) user.Roles.Add(new UserRole { UserId = user.Id, Role = role });
        await RevokeSessions(id, ct);
        Audit("SetRoles", "User", user.Id, user.BranchId, $"{oldRoles} -> {string.Join(',', request.Roles)}");
        await Db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return NoContent();
    }

    [HttpPut("{id:guid}/active")]
    public async Task<IActionResult> SetActive(Guid id, SetActiveRequest request, CancellationToken ct)
    {
        RequireRole(Roles.Admin, Roles.Owner);
        await using var transaction = await Db.Database.BeginTransactionAsync(ct);
        await LockAccountAdministration(ct);
        var user = await LockUser(id, ct);
        EnsureCanManage(user);
        if (user.IsActive && !request.IsActive) await EnsureAdministrativeCoverage(user, [], ct);
        user.IsActive = request.IsActive;
        if (!request.IsActive) await RevokeSessions(id, ct);
        Audit(request.IsActive ? "ActivateAccount" : "LockAccount", "User", user.Id, user.BranchId);
        await Db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/reset-password")]
    public async Task<IActionResult> ResetPassword(Guid id, ResetPasswordRequest request, CancellationToken ct)
    {
        RequireRole(Roles.Admin, Roles.Owner);
        ValidatePassword(request.Password);
        await using var transaction = await Db.Database.BeginTransactionAsync(ct);
        var user = await LockUser(id, ct);
        EnsureCanManage(user);
        user.PasswordHash = passwords.Hash(user, request.Password);
        await RevokeSessions(id, ct);
        Audit("ResetPassword", "User", user.Id, user.BranchId);
        await Db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return NoContent();
    }

    private async Task<User> LockUser(Guid id, CancellationToken ct)
    {
        var user = await Db.Users.FromSqlInterpolated($"SELECT * FROM \"Users\" WHERE \"Id\" = {id} FOR UPDATE").SingleOrDefaultAsync(ct) ?? throw ApiError.NotFound();
        await Db.Entry(user).Collection(x => x.Roles).LoadAsync(ct);
        return user;
    }
    private void EnsureCanManage(User user)
    {
        // Owner không được đặt lại mật khẩu tài khoản mang quyền cao hơn.
        if (ActiveRole == Roles.Owner && (user.Roles.Count != 1 || user.Roles[0].Role != Roles.Store)) throw ApiError.Forbidden();
    }
    private async Task RevokeSessions(Guid id, CancellationToken ct)
        => await Db.Sessions.Where(x => x.UserId == id && x.RevokedAt == null).ExecuteUpdateAsync(x => x.SetProperty(s => s.RevokedAt, DateTimeOffset.UtcNow), ct);
    private async Task LockAccountAdministration(CancellationToken ct)
        => await Db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(739102020)", ct);
    private async Task EnsureAdministrativeCoverage(User user, string[] remainingRoles, CancellationToken ct)
    {
        // Khóa toàn cục tránh hai Admin đồng thời vô tình thu hồi người cuối cùng.
        foreach (var role in new[] { Roles.Admin, Roles.Owner })
            if (user.Roles.Any(x => x.Role == role) && !remainingRoles.Contains(role) && !await Db.Users.AnyAsync(x => x.Id != user.Id && x.IsActive && x.Roles.Any(r => r.Role == role), ct))
                throw ApiError.Conflict($"Không thể thu hồi hoặc khóa tài khoản {role} đang hoạt động cuối cùng.");
    }
    private async Task ValidateRolesAndBranch(string[] roles, Guid? branchId, CancellationToken ct)
    {
        if (roles is null || roles.Length == 0 || roles.Distinct().Count() != roles.Length || roles.Any(x => !Roles.All.Contains(x))) throw ApiError.BadRequest("Vai trò không hợp lệ.");
        if (roles.Contains(Roles.Store) && branchId is null) throw ApiError.BadRequest("Tài khoản Store phải thuộc một cửa hàng.");
        if (branchId is not null && !await Db.Branches.AnyAsync(x => x.Id == branchId && x.IsActive, ct)) throw ApiError.BadRequest("Cửa hàng không tồn tại hoặc đã ngừng hoạt động.");
    }
    private static void ValidateNames(string username, string fullName)
    {
        if (string.IsNullOrWhiteSpace(username) || username.Trim().Length > 100 || string.IsNullOrWhiteSpace(fullName) || fullName.Trim().Length > 200)
            throw ApiError.BadRequest("Username hoặc họ tên không hợp lệ.");
    }
    private static void ValidatePassword(string password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < 12 || password.Length > 200) throw ApiError.BadRequest("Mật khẩu phải có từ 12 đến 200 ký tự.");
    }
    private static object View(User user) => new { user.Id, user.Username, user.FullName, user.Phone, user.BranchId, user.IsActive, user.CreatedAt, roles = user.Roles.Select(x => x.Role) };
}
