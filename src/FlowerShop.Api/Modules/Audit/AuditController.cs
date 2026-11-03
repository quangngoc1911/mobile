using FlowerShop.Api.Modules.Common;
using FlowerShop.Domain.Entities;
using FlowerShop.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FlowerShop.Api.Modules.Audit;

[Route("api/audit")]
public sealed class AuditController(FlowerShopDbContext db) : SecuredController(db)
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid? branchId, CancellationToken ct)
    {
        RequireRole(Roles.Admin, Roles.Owner, Roles.Store);
        var query = Db.AuditLogs.AsQueryable();
        if (ActiveRole == Roles.Admin)
        {
            string[] administrativeTypes = ["User", "Session", "Branch"];
            query = query.Where(x => administrativeTypes.Contains(x.EntityType));
        }
        if (ActiveRole == Roles.Store)
        {
            var own = await Db.Users.Where(x => x.Id == ActorId).Select(x => x.BranchId).SingleAsync(ct);
            if (branchId is not null && branchId != own) throw ApiError.Forbidden();
            query = query.Where(x => x.BranchId == own && x.ActorId == ActorId);
        }
        else if (branchId is not null) query = query.Where(x => x.BranchId == branchId);
        return Ok(await query.OrderByDescending(x => x.CreatedAt).Take(500).ToListAsync(ct));
    }
}
