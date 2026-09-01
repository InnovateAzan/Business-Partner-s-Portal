using BusinessPartnerPortal.Api.Auth;
using BusinessPartnerPortal.Api.Common;
using BusinessPartnerPortal.Api.Data;
using BusinessPartnerPortal.Api.Domain;
using BusinessPartnerPortal.Api.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BusinessPartnerPortal.Api.Features.Auth;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/auth").WithTags("Auth");

        group.MapPost("/login", async (LoginRequest request, AppDbContext db, JwtTokenService jwt, ILoggerFactory loggerFactory, CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("Login");
            var user = await db.Users.FirstOrDefaultAsync(x => x.Email == request.Email, ct);
            if (user is null || !user.IsActive || string.IsNullOrWhiteSpace(user.PasswordHash))
            {
                logger.LogWarning("Login failed for {Email}: user missing or inactive.", request.Email);
                throw new ApiException(StatusCodes.Status401Unauthorized, "Invalid email address or password.");
            }

            if (user.LockoutUntil is not null && user.LockoutUntil > DateTimeOffset.UtcNow)
                throw new ApiException(StatusCodes.Status423Locked, "Account is temporarily locked. Please try again later.");

            var result = new PasswordHasher<User>().VerifyHashedPassword(user, user.PasswordHash, request.Password);
            if (result == PasswordVerificationResult.Failed)
            {
                user.FailedLoginAttempts += 1;
                if (user.FailedLoginAttempts >= 5)
                {
                    user.LockoutUntil = DateTimeOffset.UtcNow.AddMinutes(15);
                    user.FailedLoginAttempts = 0;
                }
                await db.SaveChangesAsync(ct);
                logger.LogWarning("Login failed for {Email}.", request.Email);
                throw new ApiException(StatusCodes.Status401Unauthorized, "Invalid email address or password.");
            }

            user.FailedLoginAttempts = 0;
            user.LockoutUntil = null;
            user.LastLoginAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);

            var roles = await (from ur in db.UserRoles join r in db.Roles on ur.RoleId equals r.Id where ur.UserId == user.Id select r.Code).ToListAsync(ct);
            var (token, expires) = jwt.Create(user, roles);
            var vendorId = await db.VendorUsers.Where(x => x.UserId == user.Id && x.IsActive).Select(x => (Guid?)x.VendorId).FirstOrDefaultAsync(ct);
            var permissions = user.IsSuperAdmin
                ? await db.Permissions.Select(x => x.Code).ToListAsync(ct)
                : await GetEffectivePermissions(db, user.Id, ct);

            logger.LogInformation("Login succeeded for {Email}. UserId={UserId}", user.Email, user.Id);
            return Results.Ok(new LoginResponse(token, expires, new UserResponse(user.Id, user.FullName, user.Email, user.UserType, roles, permissions, vendorId)));
        }).AllowAnonymous();

        group.MapGet("/me", async (CurrentUser current, AppDbContext db, CancellationToken ct) =>
        {
            var user = await db.Users.FirstAsync(x => x.Id == current.UserId, ct);
            var roles = await (from ur in db.UserRoles join r in db.Roles on ur.RoleId equals r.Id where ur.UserId == user.Id select r.Code).ToListAsync(ct);
            var permissions = await current.GetPermissionsAsync(ct);
            var vendorId = await current.GetVendorIdAsync(ct);
            return Results.Ok(new UserResponse(user.Id, user.FullName, user.Email, user.UserType, roles, permissions, vendorId));
        }).RequireAuthorization();

        return app;
    }

    private static async Task<List<string>> GetEffectivePermissions(AppDbContext db, Guid userId, CancellationToken ct)
    {
        var permissions = await (from ur in db.UserRoles join rp in db.RolePermissions on ur.RoleId equals rp.RoleId join p in db.Permissions on rp.PermissionId equals p.Id where ur.UserId == userId select p.Code).Distinct().ToListAsync(ct);
        var overrides = await db.UserPermissionOverrides.Where(x => x.UserId == userId).ToListAsync(ct);
        foreach (var item in overrides)
        {
            var code = await db.Permissions.Where(x => x.Id == item.PermissionId).Select(x => x.Code).FirstAsync(ct);
            if (item.IsAllowed && !permissions.Contains(code)) permissions.Add(code);
            if (!item.IsAllowed) permissions.Remove(code);
        }
        return permissions;
    }
}

public sealed record LoginRequest(string Email, string Password);
public sealed record UserResponse(Guid Id, string FullName, string Email, string UserType, List<string> Roles, List<string> Permissions, Guid? VendorId);
public sealed record LoginResponse(string AccessToken, DateTimeOffset ExpiresAt, UserResponse User);
