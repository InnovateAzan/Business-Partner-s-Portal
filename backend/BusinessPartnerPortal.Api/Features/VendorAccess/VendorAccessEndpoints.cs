using BusinessPartnerPortal.Api.Common;
using BusinessPartnerPortal.Api.Data;
using BusinessPartnerPortal.Api.Domain;
using BusinessPartnerPortal.Api.Security;
using BusinessPartnerPortal.Api.Services;

using System.Security.Cryptography;
using System.Text;

using Microsoft.EntityFrameworkCore;

namespace BusinessPartnerPortal.Api.Features.VendorAccess;

public static class VendorAccessEndpoints
{
    public sealed record AccessRequest(
        bool IsActive
    );


    public sealed record UpdateVendorUserRequest(
        string FullName,
        string Email
    );

    public static IEndpointRouteBuilder MapVendorAccessEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/v1/vendor-access")
            .RequireAuthorization()
            .WithTags("Vendor Access");

        // ========================================================
        // LIST
        // ========================================================

        group.MapGet(
            "/",
            async (
                CurrentUser current,
                AppDbContext db,
                CancellationToken ct) =>
            {
                EnsureAuthorized(current);

                var now =
                    DateTimeOffset.UtcNow;

                var query =
                    from vendorUser
                        in db.VendorUsers

                    join user
                        in db.Users

                        on vendorUser.UserId
                        equals user.Id

                    join vendor
                        in db.Vendors

                        on vendorUser.VendorId
                        equals vendor.Id

                    orderby
                        vendor.VendorName

                    select new
                    {
                        userId =
                            user.Id,

                        userName =
                            user.FullName,

                        email =
                            user.Email,

                        userActive =
                            user.IsActive,

                        lastLoginAt =
                            user.LastLoginAt,

                        failedLoginAttempts =
                            user.FailedLoginAttempts,

                        lockoutUntil =
                            user.LockoutUntil,

                        isLocked =
                            user.LockoutUntil != null &&
                            user.LockoutUntil > now,

                        vendorId =
                            vendor.Id,

                        vendorCode =
                            vendor.VendorCode,

                        vendorName =
                            vendor.VendorName,

                        oracleVendorId =
                            vendor.OracleVendorId,

                        accessActive =
                            vendorUser.IsActive
                            &&
                            user.IsActive,

                        isPrimary =
                            vendorUser.IsPrimary,

                        passwordSetupCompleted =
                            !string.IsNullOrWhiteSpace(
                                user.PasswordHash)
                    };

                return Results.Ok(
                    await query
                        .ToListAsync(ct));
            });

        group.MapPost(
            "/{userId:guid}/resend-setup-email",
            async (
                Guid userId,
                CurrentUser current,
                AppDbContext db,
                EmailOtpSender emailSender,
                IConfiguration config,
                CancellationToken ct) =>
            {
                EnsureAuthorized(current);

                var vendorUser = await (
                    from mapping in db.VendorUsers
                    join user in db.Users on mapping.UserId equals user.Id
                    join vendor in db.Vendors on mapping.VendorId equals vendor.Id
                    where mapping.UserId == userId
                    select new { User = user, Vendor = vendor }
                ).FirstOrDefaultAsync(ct)
                    ?? throw new ApiException(404, "Vendor portal user not found.");

                if (!string.Equals(
                    vendorUser.User.UserType,
                    "VENDOR",
                    StringComparison.OrdinalIgnoreCase))
                {
                    throw new ApiException(
                        400,
                        "Only vendor accounts can receive a password setup email.");
                }

                var now = DateTimeOffset.UtcNow;
                var previous = await db.PasswordResetTokens.Where(x => x.UserId == userId && x.UsedAt == null && x.ExpiresAt > now).ToListAsync(ct);
                previous.ForEach(x => x.UsedAt = now);
                var rawToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
                db.PasswordResetTokens.Add(new PasswordResetToken { Id = Guid.NewGuid(), UserId = userId, TokenHash = Hash(rawToken), ExpiresAt = now.AddMinutes(30), CreatedAt = now });
                await db.SaveChangesAsync(ct);

                var setupUrl = $"{(config["FRONTEND_URL"] ?? "http://localhost:5173").TrimEnd('/')}/reset-password?token={Uri.EscapeDataString(rawToken)}";
                try
                {
                    await emailSender.SendPortalAccessAsync(vendorUser.User.Email, vendorUser.Vendor.VendorName, setupUrl, ct);
                    await WriteAuditAsync(db, current.UserId, vendorUser.Vendor.Id, userId, "VENDOR_ACCOUNT_SETUP_EMAIL_RESENT", ct);
                    return Results.Ok(new { message = "Password setup email has been sent successfully." });
                }
                catch
                {
                    await WriteAuditAsync(db, current.UserId, vendorUser.Vendor.Id, userId, "VENDOR_ACCOUNT_SETUP_EMAIL_RESEND_FAILED", ct);
                    throw new ApiException(500, "Email could not be sent. Please check the vendor email address or email configuration.");
                }
            });

        // ========================================================
        // UPDATE VENDOR USER
        //
        // Vendor Access is also available to Supply Chain, so this
        // endpoint deliberately keeps the account type/role fixed
        // to VENDOR and only allows identity fields to be updated.
        // ========================================================

        group.MapPut(
            "/{userId:guid}",
            async (
                Guid userId,
                UpdateVendorUserRequest request,
                CurrentUser current,
                AppDbContext db,
                CancellationToken ct) =>
            {
                EnsureAuthorized(current);

                var vendorUser = await (
                    from mapping in db.VendorUsers
                    join user in db.Users on mapping.UserId equals user.Id
                    join vendor in db.Vendors on mapping.VendorId equals vendor.Id
                    where mapping.UserId == userId
                    select new { User = user, Vendor = vendor }
                ).FirstOrDefaultAsync(ct)
                    ?? throw new ApiException(404, "Vendor portal user not found.");

                if (!string.Equals(
                    vendorUser.User.UserType,
                    "VENDOR",
                    StringComparison.OrdinalIgnoreCase))
                {
                    throw new ApiException(
                        400,
                        "Only vendor accounts can be managed here.");
                }

                var fullName = request.FullName?.Trim() ?? string.Empty;
                var email = request.Email?.Trim().ToLowerInvariant() ?? string.Empty;

                if (string.IsNullOrWhiteSpace(fullName))
                {
                    throw new ApiException(400, "Full name is required.");
                }

                if (string.IsNullOrWhiteSpace(email))
                {
                    throw new ApiException(400, "Email is required.");
                }

                var duplicate = await db.Users.AnyAsync(
                    x => x.Id != userId && x.Email.ToLower() == email,
                    ct);

                if (duplicate)
                {
                    throw new ApiException(
                        409,
                        "Another user already uses this email.");
                }

                vendorUser.User.FullName = fullName;
                vendorUser.User.Email = email;
                vendorUser.User.UserType = "VENDOR";
                vendorUser.User.UpdatedAt = DateTimeOffset.UtcNow;

                var vendorRole = await db.Roles
                    .FirstOrDefaultAsync(x => x.Code == "VENDOR", ct)
                    ?? throw new ApiException(
                        409,
                        "VENDOR role is not configured.");

                var existingRoles = await db.UserRoles
                    .Where(x => x.UserId == userId)
                    .ToListAsync(ct);

                db.UserRoles.RemoveRange(existingRoles);

                db.UserRoles.Add(
                    new UserRole
                    {
                        UserId = userId,
                        RoleId = vendorRole.Id
                    });

                await db.SaveChangesAsync(ct);

                await WriteAuditAsync(
                    db,
                    current.UserId,
                    vendorUser.Vendor.Id,
                    userId,
                    "VENDOR_USER_UPDATED",
                    ct);

                return Results.Ok(
                    new
                    {
                        userId,
                        fullName = vendorUser.User.FullName,
                        email = vendorUser.User.Email,
                        userType = "VENDOR",
                        role = "VENDOR",
                        message = "Vendor user updated successfully."
                    });
            });

        // ========================================================
        // ENABLE / DISABLE
        // ========================================================

        group.MapPut(
            "/{userId:guid}/active",
            async (
                Guid userId,
                AccessRequest request,
                CurrentUser current,
                AppDbContext db,
                CancellationToken ct) =>
            {
                EnsureAuthorized(current);

                var user =
                    await db.Users
                        .FirstOrDefaultAsync(
                            x =>
                                x.Id ==
                                userId,
                            ct)
                    ??
                    throw new ApiException(
                        404,
                        "Vendor user not found.");

                if (
                    !string.Equals(
                        user.UserType,
                        "VENDOR",
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new ApiException(
                        400,
                        "Only vendor accounts can be managed here.");
                }

                user.IsActive =
                    request.IsActive;

                user.UpdatedAt =
                    DateTimeOffset.UtcNow;

                var mappings =
                    await db.VendorUsers
                        .Where(
                            x =>
                                x.UserId ==
                                userId)
                        .ToListAsync(ct);

                foreach (
                    var mapping
                    in mappings)
                {
                    mapping.IsActive =
                        request.IsActive;
                }

                await db.SaveChangesAsync(ct);

                return Results.Ok(
                    new
                    {
                        message =
                            request.IsActive
                                ? "Vendor portal access enabled."
                                : "Vendor portal access disabled.",

                        userId,

                        isActive =
                            request.IsActive
                    });
            });

        // ========================================================
        // UNLOCK VENDOR ACCOUNT
        // ========================================================

        group.MapPost(
            "/{userId:guid}/unlock",
            async (
                Guid userId,
                CurrentUser current,
                AppDbContext db,
                CancellationToken ct) =>
            {
                EnsureAuthorized(current);

                var vendorUser = await (
                    from mapping in db.VendorUsers
                    join user in db.Users on mapping.UserId equals user.Id
                    join vendor in db.Vendors on mapping.VendorId equals vendor.Id
                    where mapping.UserId == userId
                    select new { User = user, Vendor = vendor }
                ).FirstOrDefaultAsync(ct)
                    ?? throw new ApiException(404, "Vendor portal user not found.");

                if (!string.Equals(
                    vendorUser.User.UserType,
                    "VENDOR",
                    StringComparison.OrdinalIgnoreCase))
                {
                    throw new ApiException(
                        400,
                        "Only vendor accounts can be unlocked here.");
                }

                vendorUser.User.FailedLoginAttempts = 0;
                vendorUser.User.LockoutUntil = null;
                vendorUser.User.UpdatedAt = DateTimeOffset.UtcNow;

                await db.SaveChangesAsync(ct);

                await WriteAuditAsync(
                    db,
                    current.UserId,
                    vendorUser.Vendor.Id,
                    userId,
                    "VENDOR_ACCOUNT_UNLOCKED",
                    ct);

                return Results.Ok(
                    new
                    {
                        userId,
                        isLocked = false,
                        failedLoginAttempts = 0,
                        lockoutUntil = (DateTimeOffset?)null,
                        message = "Vendor account unlocked successfully."
                    });
            });

        // ========================================================
        // DELETE PORTAL ACCESS
        //
        // Important:
        // Oracle supplier is NOT deleted.
        // master.vendors is retained.
        //
        // We remove:
        // - vendor_users mappings
        // - VENDOR role assignments
        //
        // We disable:
        // - portal vendor user
        // ========================================================

        group.MapDelete(
            "/{userId:guid}",
            async (
                Guid userId,
                CurrentUser current,
                AppDbContext db,
                CancellationToken ct) =>
            {
                EnsureAuthorized(current);

                var user =
                    await db.Users
                        .FirstOrDefaultAsync(
                            x =>
                                x.Id ==
                                userId,
                            ct)
                    ??
                    throw new ApiException(
                        404,
                        "Vendor user not found.");

                if (
                    !string.Equals(
                        user.UserType,
                        "VENDOR",
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new ApiException(
                        400,
                        "Only vendor portal access can be deleted here.");
                }

                await using var transaction =
                    await db.Database
                        .BeginTransactionAsync(ct);

                try
                {
                    // ---------------------------------------------
                    // Remove vendor mappings
                    // ---------------------------------------------

                    var mappings =
                        await db.VendorUsers
                            .Where(
                                x =>
                                    x.UserId ==
                                    userId)
                            .ToListAsync(ct);

                    db.VendorUsers.RemoveRange(
                        mappings);

                    // ---------------------------------------------
                    // Remove VENDOR role
                    // ---------------------------------------------

                    var vendorRoleIds =
                        await db.Roles
                            .Where(
                                x =>
                                    x.Code ==
                                    "VENDOR")
                            .Select(
                                x => x.Id)
                            .ToListAsync(ct);

                    var roleMappings =
                        await db.UserRoles
                            .Where(
                                x =>
                                    x.UserId ==
                                        userId
                                    &&
                                    vendorRoleIds.Contains(
                                        x.RoleId))
                            .ToListAsync(ct);

                    db.UserRoles.RemoveRange(
                        roleMappings);

                    // ---------------------------------------------
                    // Disable portal login.
                    //
                    // Do NOT hard-delete user record because invoice
                    // history / audit records may reference it.
                    // ---------------------------------------------

                    user.IsActive =
                        false;

                    user.LockoutUntil =
                        null;

                    user.FailedLoginAttempts =
                        0;

                    user.UpdatedAt =
                        DateTimeOffset.UtcNow;

                    await db.SaveChangesAsync(ct);

                    await transaction.CommitAsync(ct);
                }
                catch
                {
                    await transaction.RollbackAsync(ct);
                    throw;
                }

                return Results.Ok(
                    new
                    {
                        message =
                            "Vendor portal access deleted successfully. Oracle supplier data was not changed.",

                        userId
                    });
            });

        return app;
    }

    private static void EnsureAuthorized(
        CurrentUser current)
    {
        var admin =
            string.Equals(
                current.UserType,
                "ADMIN",
                StringComparison.OrdinalIgnoreCase);

        var internalUser =
            string.Equals(
                current.UserType,
                "INTERNAL",
                StringComparison.OrdinalIgnoreCase);

        if (!admin && !internalUser)
        {
            throw new ApiException(
                403,
                "Vendor access management requires Admin or Internal access.");
        }
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static Task<int> WriteAuditAsync(AppDbContext db, Guid adminUserId, Guid vendorId, Guid vendorUserId, string action, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($@"INSERT INTO audit.audit_logs (user_id, vendor_id, action, entity_type, entity_id, created_at) VALUES ({adminUserId}, {vendorId}, {action}, {"VendorUser"}, {vendorUserId}, {DateTimeOffset.UtcNow})", ct);
}
