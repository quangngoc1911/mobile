using FlowerShop.Domain.Entities;
using FlowerShop.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FlowerShop.Api.Modules.Common;

[ApiController]
[Authorize]
public abstract class SecuredController(FlowerShopDbContext db) : ControllerBase
{
    protected FlowerShopDbContext Db { get; } = db;
    protected Guid ActorId => Guid.Parse(User.FindFirst("sub")!.Value);
    protected Guid SessionId => Guid.Parse(User.FindFirst("sid")!.Value);
    protected string ActiveRole => User.FindFirst("active_role")!.Value;

    protected void RequireRole(params string[] roles)
    {
        if (!roles.Contains(ActiveRole, StringComparer.Ordinal)) throw ApiError.Forbidden();
    }

    protected async Task CheckBranchAsync(Guid branchId, CancellationToken ct)
    {
        RequireRole(Roles.Owner, Roles.Store);
        if (ActiveRole == Roles.Store && !await Db.Users.AnyAsync(x => x.Id == ActorId && x.BranchId == branchId, ct))
            throw ApiError.Forbidden();
        if (!await Db.Branches.AnyAsync(x => x.Id == branchId, ct)) throw ApiError.NotFound();
    }

    protected void Audit(string action, string entityType, object entityId, Guid? branchId = null, string? details = null)
        => Db.AuditLogs.Add(new AuditLog { ActorId = ActorId, ActiveRole = ActiveRole, Action = action, EntityType = entityType, EntityId = entityId.ToString()!, BranchId = branchId, Details = details });
}
