using System.Text.RegularExpressions;
using FlowerShop.Api.Modules.Common;
using FlowerShop.Domain.Entities;
using FlowerShop.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FlowerShop.Api.Modules.Branches;

public sealed record CreateBranchRequest(string Code, string Name, string? Address);
public sealed record UpdateBranchRequest(string Name, string? Address, bool IsActive);

[Route("api/branches")]
public sealed class BranchesController(FlowerShopDbContext db) : SecuredController(db)
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        RequireRole(Roles.Admin, Roles.Owner, Roles.Store);
        var query = Db.Branches.AsQueryable();
        if (ActiveRole == Roles.Store)
        {
            var branchId = await Db.Users.Where(x => x.Id == ActorId).Select(x => x.BranchId).SingleAsync(ct);
            query = query.Where(x => x.Id == branchId);
        }
        return Ok(await query.OrderBy(x => x.Code).Select(x => new { x.Id, x.Code, x.Name, x.Address, x.IsActive }).ToListAsync(ct));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        RequireRole(Roles.Admin, Roles.Owner, Roles.Store);
        // Store chỉ được xem chi tiết điểm bán gắn với tài khoản hiện tại.
        if (ActiveRole == Roles.Store && !await Db.Users.AnyAsync(x => x.Id == ActorId && x.BranchId == id, ct))
            throw ApiError.Forbidden();
        var branch = await Db.Branches.Where(x => x.Id == id)
            .Select(x => new { x.Id, x.Code, x.Name, x.Address, x.IsActive }).SingleOrDefaultAsync(ct)
            ?? throw ApiError.NotFound();
        return Ok(branch);
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateBranchRequest request, CancellationToken ct)
    {
        RequireRole(Roles.Admin, Roles.Owner);
        var code = request.Code?.Trim().ToUpperInvariant() ?? "";
        if (!Regex.IsMatch(code, "^[A-Z0-9]{1,10}$") || string.IsNullOrWhiteSpace(request.Name)) throw ApiError.BadRequest("Mã cửa hàng gồm 1–10 chữ cái Latin hoặc chữ số; cần nhập tên.");
        if (await Db.Branches.AnyAsync(x => x.Code == code, ct)) throw ApiError.Conflict("Mã cửa hàng đã tồn tại.");
        var branch = new Branch { Code = code, Name = request.Name.Trim(), Address = request.Address?.Trim() };
        Db.Branches.Add(branch);
        Audit("CreateBranch", "Branch", branch.Id, branch.Id);
        await Db.SaveChangesAsync(ct);
        return Created($"/api/branches/{branch.Id}", new { branch.Id, branch.Code, branch.Name, branch.Address, branch.IsActive });
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdateBranchRequest request, CancellationToken ct)
    {
        RequireRole(Roles.Admin, Roles.Owner);
        if (string.IsNullOrWhiteSpace(request.Name)) throw ApiError.BadRequest("Cần nhập tên cửa hàng.");
        var branch = await Db.Branches.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw ApiError.NotFound();
        branch.Name = request.Name.Trim(); branch.Address = request.Address?.Trim(); branch.IsActive = request.IsActive;
        Audit("UpdateBranch", "Branch", id, id, $"active={request.IsActive}");
        await Db.SaveChangesAsync(ct);
        return NoContent();
    }
}
