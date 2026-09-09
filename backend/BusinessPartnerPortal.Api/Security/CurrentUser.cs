using System.Security.Claims;
using BusinessPartnerPortal.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace BusinessPartnerPortal.Api.Security;

public sealed class CurrentUser(IHttpContextAccessor accessor, AppDbContext db)
{
    public Guid UserId => Guid.Parse(accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new UnauthorizedAccessException());
    public string UserType => accessor.HttpContext?.User.FindFirstValue("user_type") ?? "";
    public bool IsAdmin => UserType == "ADMIN";

    public async Task<Guid?> GetVendorIdAsync(CancellationToken ct = default)
    {
        if (IsAdmin) return null;
        return await db.VendorUsers.Where(x => x.UserId == UserId && x.IsActive).Select(x => (Guid?)x.VendorId).FirstOrDefaultAsync(ct);
    }

    public async Task<List<string>> GetPermissionsAsync(CancellationToken ct = default)
    {
        if (IsAdmin) return await db.Permissions.Select(x => x.Code).ToListAsync(ct);
        var rolePermissions = from ur in db.UserRoles join rp in db.RolePermissions on ur.RoleId equals rp.RoleId join p in db.Permissions on rp.PermissionId equals p.Id where ur.UserId == UserId select p.Code;
        var roleList = await rolePermissions.Distinct().ToListAsync(ct);
        var overrides = await db.UserPermissionOverrides.Where(x => x.UserId == UserId).ToListAsync(ct);
        foreach (var ov in overrides)
        {
            var code = await db.Permissions.Where(x => x.Id == ov.PermissionId).Select(x => x.Code).FirstAsync(ct);
            if (ov.IsAllowed && !roleList.Contains(code)) roleList.Add(code);
            if (!ov.IsAllowed) roleList.Remove(code);
        }
        return roleList;
    }

    public async Task DemandAsync(string permission, CancellationToken ct = default)
    {
        if (IsAdmin) return;
        var permissions = await GetPermissionsAsync(ct);
        if (!permissions.Contains(permission)) throw new BusinessPartnerPortal.Api.Common.ApiException(StatusCodes.Status403Forbidden, "You do not have permission to perform this action.");
    }
}
