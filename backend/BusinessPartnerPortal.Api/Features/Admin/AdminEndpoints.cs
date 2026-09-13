using BusinessPartnerPortal.Api.Common;
using BusinessPartnerPortal.Api.Data;
using BusinessPartnerPortal.Api.Domain;
using BusinessPartnerPortal.Api.Security;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BusinessPartnerPortal.Api.Features.Admin;

public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/v1/admin")
            .RequireAuthorization()
            .WithTags("Admin");

        // =========================================================
        // ROLES
        // =========================================================

        group.MapGet(
            "/roles",
            async (
                CurrentUser current,
                AppDbContext db,
                CancellationToken ct) =>
            {
                DemandAdmin(current);

                var roles =
                    await db.Roles
                        .Where(x => x.IsActive)
                        .OrderBy(x => x.Name)
                        .Select(
                            x => new
                            {
                                x.Id,
                                x.Code,
                                x.Name
                            })
                        .ToListAsync(ct);

                return Results.Ok(roles);
            });

        // =========================================================
        // CREATE ROLE
        // =========================================================

        group.MapPost(
            "/roles",
            async (
                RoleMutationRequest request,
                CurrentUser current,
                AppDbContext db,
                CancellationToken ct) =>
            {
                DemandAdmin(current);

                var name =
                    request.Name
                        .Trim();

                var code =
                    request.Code
                        .Trim()
                        .ToUpperInvariant();

                if (string.IsNullOrWhiteSpace(name))
                {
                    throw new ApiException(
                        400,
                        "Role name is required.");
                }

                if (string.IsNullOrWhiteSpace(code))
                {
                    throw new ApiException(
                        400,
                        "Role code is required.");
                }

                var duplicate =
                    await db.Roles
                        .AnyAsync(
                            x =>
                                x.Code.ToUpper() == code ||
                                x.Name.ToUpper() ==
                                name.ToUpper(),
                            ct);

                if (duplicate)
                {
                    throw new ApiException(
                        409,
                        "A role with this name or code already exists.");
                }

                var role =
                    new Role
                    {
                        Id =
                            Guid.NewGuid(),

                        Code =
                            code,

                        Name =
                            name,

                        IsActive =
                            true
                    };

                db.Roles.Add(role);

                await db.SaveChangesAsync(ct);

                await Audit(
                    db,
                    current.UserId,
                    "ROLE_CREATED",
                    "Role",
                    role.Id,
                    ct);

                return Results.Ok(
                    new
                    {
                        role.Id,
                        role.Code,
                        role.Name
                    });
            });

        // =========================================================
        // PERMISSIONS
        // =========================================================

        group.MapGet(
            "/permissions",
            async (
                CurrentUser current,
                AppDbContext db,
                CancellationToken ct) =>
            {
                DemandAdmin(current);

                var permissions =
                    await db.Permissions
                        .OrderBy(x => x.Code)
                        .Select(
                            x => new
                            {
                                x.Id,
                                x.Code,
                                x.Name
                            })
                        .ToListAsync(ct);

                return Results.Ok(
                    permissions);
            });

        // =========================================================
        // ROLE PERMISSIONS
        // =========================================================

        group.MapGet(
            "/roles/{roleId:guid}/permissions",
            async (
                Guid roleId,
                CurrentUser current,
                AppDbContext db,
                CancellationToken ct) =>
            {
                DemandAdmin(current);

                var role =
                    await db.Roles
                        .FirstOrDefaultAsync(
                            x =>
                                x.Id ==
                                roleId,
                            ct)
                    ?? throw new ApiException(
                        404,
                        "Role not found.");

                /*
                 * ADMIN permissions are implicit/unrestricted.
                 *
                 * Return all IDs so frontend can show
                 * all checked.
                 */
                if (
                    role.Code.Equals(
                        "ADMIN",
                        StringComparison.OrdinalIgnoreCase))
                {
                    var allIds =
                        await db.Permissions
                            .Select(x => x.Id)
                            .ToListAsync(ct);

                    return Results.Ok(
                        allIds);
                }

                var ids =
                    await db.RolePermissions
                        .Where(
                            x =>
                                x.RoleId ==
                                roleId)
                        .Select(
                            x =>
                                x.PermissionId)
                        .ToListAsync(ct);

                return Results.Ok(ids);
            });

        group.MapPut(
            "/roles/{roleId:guid}/permissions",
            async (
                Guid roleId,
                RolePermissionsRequest request,
                CurrentUser current,
                AppDbContext db,
                CancellationToken ct) =>
            {
                DemandAdmin(current);

                var role =
                    await db.Roles
                        .FirstOrDefaultAsync(
                            x =>
                                x.Id ==
                                roleId,
                            ct)
                    ?? throw new ApiException(
                        404,
                        "Role not found.");

                if (
                    role.Code.Equals(
                        "ADMIN",
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new ApiException(
                        409,
                        "Administrator permissions are implicitly unrestricted and cannot be reduced here.");
                }

                var requestedIds =
                    request.PermissionIds
                    ?? new List<Guid>();

                var valid =
                    await db.Permissions
                        .Where(
                            x =>
                                requestedIds.Contains(
                                    x.Id))
                        .Select(x => x.Id)
                        .ToListAsync(ct);

                var existing =
                    await db.RolePermissions
                        .Where(
                            x =>
                                x.RoleId ==
                                roleId)
                        .ToListAsync(ct);

                db.RolePermissions.RemoveRange(
                    existing);

                foreach (
                    var permissionId
                    in valid)
                {
                    db.RolePermissions.Add(
                        new RolePermission
                        {
                            RoleId =
                                roleId,

                            PermissionId =
                                permissionId
                        });
                }

                await db.SaveChangesAsync(ct);

                await Audit(
                    db,
                    current.UserId,
                    "ROLE_PERMISSIONS_UPDATED",
                    "Role",
                    roleId,
                    ct);

                return Results.Ok(
                    new
                    {
                        roleId,
                        permissionIds =
                            valid
                    });
            });

        // =========================================================
        // USERS
        // =========================================================

        group.MapGet(
            "/users",
            async (
                CurrentUser current,
                AppDbContext db,
                CancellationToken ct) =>
            {
                DemandAdmin(current);

                var users =
                    await db.Users
                        .OrderBy(
                            x =>
                                x.FullName)
                        .ToListAsync(ct);

                var result =
                    new List<object>();

                foreach (var user in users)
                {
                    var roles =
                        await (
                            from userRole
                                in db.UserRoles

                            join role
                                in db.Roles

                                on userRole.RoleId
                                equals role.Id

                            where
                                userRole.UserId ==
                                user.Id

                            select
                                role.Code
                        )
                        .ToListAsync(ct);

                    result.Add(
                        new
                        {
                            user.Id,
                            user.FullName,
                            user.Email,
                            user.UserType,
                            user.IsActive,
                            user.IsSuperAdmin,
                            user.LastLoginAt,
                            user.FailedLoginAttempts,
                            user.LockoutUntil,
                            isLocked =
                                user.LockoutUntil is not null &&
                                user.LockoutUntil > DateTimeOffset.UtcNow,
                            roles
                        });
                }

                return Results.Ok(result);
            });

        // =========================================================
        // CREATE USER
        // =========================================================

        group.MapPost(
            "/users",
            async (
                UserMutationRequest request,
                CurrentUser current,
                AppDbContext db,
                CancellationToken ct) =>
            {
                DemandAdmin(current);

                if (
                    string.IsNullOrWhiteSpace(
                        request.Password) ||
                    request.Password.Length < 10)
                {
                    throw new ApiException(
                        400,
                        "Temporary password must be at least 10 characters.");
                }

                var email =
                    request.Email
                        .Trim()
                        .ToLowerInvariant();

                var exists =
                    await db.Users
                        .AnyAsync(
                            x =>
                                x.Email
                                    .ToLower() ==
                                email,
                            ct);

                if (exists)
                {
                    throw new ApiException(
                        409,
                        "A user with this email already exists.");
                }

                var user =
                    new User
                    {
                        Id =
                            Guid.NewGuid(),

                        FullName =
                            request.FullName
                                .Trim(),

                        Email =
                            email,

                        UserType =
                            request.UserType
                                .Trim()
                                .ToUpperInvariant(),

                        IsActive =
                            request.IsActive,

                        IsSuperAdmin =
                            request.IsSuperAdmin,

                        CreatedAt =
                            DateTimeOffset.UtcNow,

                        UpdatedAt =
                            DateTimeOffset.UtcNow
                    };

                user.PasswordHash =
                    new PasswordHasher<User>()
                        .HashPassword(
                            user,
                            request.Password);

                db.Users.Add(user);

                await db.SaveChangesAsync(ct);

                await ReplaceRoles(
                    db,
                    user.Id,
                    request.RoleCodes,
                    ct);

                await Audit(
                    db,
                    current.UserId,
                    "USER_CREATED",
                    "User",
                    user.Id,
                    ct);

                return Results.Ok(
                    new
                    {
                        id =
                            user.Id
                    });
            });

        // =========================================================
        // UPDATE USER
        // =========================================================

        group.MapPut(
            "/users/{userId:guid}",
            async (
                Guid userId,
                UserMutationRequest request,
                CurrentUser current,
                AppDbContext db,
                CancellationToken ct) =>
            {
                DemandAdmin(current);

                var user =
                    await db.Users
                        .FirstOrDefaultAsync(
                            x =>
                                x.Id ==
                                userId,
                            ct)
                    ?? throw new ApiException(
                        404,
                        "User not found.");

                var email =
                    request.Email
                        .Trim()
                        .ToLowerInvariant();

                var duplicate =
                    await db.Users
                        .AnyAsync(
                            x =>
                                x.Id !=
                                    userId &&
                                x.Email
                                    .ToLower() ==
                                email,
                            ct);

                if (duplicate)
                {
                    throw new ApiException(
                        409,
                        "Another user already uses this email.");
                }

                user.FullName =
                    request.FullName.Trim();

                user.Email =
                    email;

                user.UserType =
                    request.UserType
                        .Trim()
                        .ToUpperInvariant();

                user.IsActive =
                    request.IsActive;

                user.IsSuperAdmin =
                    request.IsSuperAdmin;

                user.UpdatedAt =
                    DateTimeOffset.UtcNow;

                if (
                    !string.IsNullOrWhiteSpace(
                        request.Password))
                {
                    if (
                        request.Password.Length <
                        10)
                    {
                        throw new ApiException(
                            400,
                            "Password must be at least 10 characters.");
                    }

                    user.PasswordHash =
                        new PasswordHasher<User>()
                            .HashPassword(
                                user,
                                request.Password);
                }

                await db.SaveChangesAsync(ct);

                await ReplaceRoles(
                    db,
                    user.Id,
                    request.RoleCodes,
                    ct);

                await Audit(
                    db,
                    current.UserId,
                    "USER_UPDATED",
                    "User",
                    user.Id,
                    ct);

                return Results.Ok(
                    new
                    {
                        id =
                            user.Id
                    });
            });

        // =========================================================
        // DELETE USER
        // =========================================================

        group.MapDelete(
            "/users/{userId:guid}",
            async (
                Guid userId,
                CurrentUser current,
                AppDbContext db,
                CancellationToken ct) =>
            {
                DemandAdmin(current);

                if (userId == current.UserId)
                {
                    throw new ApiException(
                        400,
                        "You cannot delete your own signed-in account.");
                }

                var user =
                    await db.Users
                        .FirstOrDefaultAsync(
                            x => x.Id == userId,
                            ct)
                    ?? throw new ApiException(
                        404,
                        "User not found.");

                try
                {
                    db.Users.Remove(user);
                    await db.SaveChangesAsync(ct);
                }
                catch (DbUpdateException)
                {
                    throw new ApiException(
                        409,
                        "This user has historical business or audit records and cannot be permanently deleted. Disable the user instead to preserve history.");
                }

                await Audit(
                    db,
                    current.UserId,
                    "USER_DELETED",
                    "User",
                    userId,
                    ct);

                return Results.Ok(
                    new
                    {
                        userId,
                        message = "User deleted successfully."
                    });
            });

        // =========================================================
        // UNLOCK USER ACCOUNT
        // =========================================================

        group.MapPost(
            "/users/{userId:guid}/unlock",
            async (
                Guid userId,
                CurrentUser current,
                AppDbContext db,
                CancellationToken ct) =>
            {
                DemandAdmin(current);

                var user =
                    await db.Users
                        .FirstOrDefaultAsync(
                            x => x.Id == userId,
                            ct)
                    ?? throw new ApiException(
                        404,
                        "User not found.");

                user.FailedLoginAttempts = 0;
                user.LockoutUntil = null;
                user.UpdatedAt = DateTimeOffset.UtcNow;

                await db.SaveChangesAsync(ct);

                await Audit(
                    db,
                    current.UserId,
                    "USER_ACCOUNT_UNLOCKED",
                    "User",
                    user.Id,
                    ct);

                return Results.Ok(
                    new
                    {
                        userId = user.Id,
                        isLocked = false,
                        failedLoginAttempts = 0,
                        lockoutUntil = (DateTimeOffset?)null,
                        message = "User account unlocked successfully."
                    });
            });

        // =========================================================
        // SYSTEM CONFIG
        // =========================================================

        group.MapGet(
            "/system",
            (
                CurrentUser current,
                IConfiguration config) =>
            {
                DemandAdmin(current);

                return Results.Ok(
                    new
                    {
                        environment =
                            config["APP_ENV"]
                            ?? "Development",

                        oracleConfigured =
                            !string.IsNullOrWhiteSpace(
                                config[
                                    "ORACLE_HOST"]) &&
                            !string.IsNullOrWhiteSpace(
                                config[
                                    "ORACLE_SERVICE_NAME"]),

                        oracleInvoicePostConfigured =
                            !string.IsNullOrWhiteSpace(
                                config[
                                    "ORACLE_INVOICE_POST_URL"]),

                        smtpEnabled =
                            bool.TryParse(
                                config[
                                    "SMTP_ENABLED"],
                                out var smtp)
                            && smtp,

                        maxUploadSizeMb =
                            config[
                                "MAX_UPLOAD_SIZE_MB"]
                            ?? "10",

                        dataRetentionDays =
                            config[
                                "DATA_RETENTION_DAYS"]
                            ?? "0"
                    });
            });

        // =========================================================
        // AUDIT
        // =========================================================

        group.MapGet(
            "/audit",
            async (
                CurrentUser current,
                AppDbContext db,
                CancellationToken ct) =>
            {
                DemandAdmin(current);

                var connection =
                    db.Database.GetDbConnection();

                if (
                    connection.State !=
                    System.Data.ConnectionState.Open)
                {
                    await connection.OpenAsync(ct);
                }

                await using var command =
                    connection.CreateCommand();

                command.CommandText =
                    """
                    SELECT
                        a.id,
                        a.user_id,
                        COALESCE(
                            CASE
                                WHEN UPPER(COALESCE(actor.user_type, '')) = 'VENDOR'
                                    THEN actor_vendor.vendor_name
                                ELSE actor.full_name
                            END,
                            actor.full_name,
                            'System'
                        ) AS user_name,
                        COALESCE(
                            (
                                SELECT string_agg(r.name, ', ' ORDER BY r.name)
                                FROM security.user_roles ur
                                JOIN security.roles r
                                    ON r.id = ur.role_id
                                WHERE ur.user_id = a.user_id
                            ),
                            CASE
                                WHEN UPPER(COALESCE(actor.user_type, '')) = 'VENDOR' THEN 'Vendor'
                                WHEN UPPER(COALESCE(actor.user_type, '')) = 'ADMIN' THEN 'Administrator'
                                WHEN NULLIF(actor.user_type, '') IS NOT NULL THEN initcap(replace(lower(actor.user_type), '_', ' '))
                                ELSE 'System'
                            END
                        ) AS role_name,
                        a.action,
                        a.entity_type,
                        a.entity_id,
                        CASE
                            WHEN UPPER(COALESCE(a.entity_type, '')) = 'INVOICE'
                                THEN COALESCE(invoice_row.invoice_number, a.entity_id::text, '-')
                            WHEN UPPER(COALESCE(a.entity_type, '')) = 'USER'
                                THEN COALESCE(target_user.full_name, a.entity_id::text, '-')
                            WHEN UPPER(COALESCE(a.entity_type, '')) = 'ROLE'
                                THEN COALESCE(target_role.name, a.entity_id::text, '-')
                            WHEN UPPER(COALESCE(a.entity_type, '')) IN ('VENDOR', 'VENDORUSER')
                                THEN COALESCE(target_vendor.vendor_name, actor_vendor.vendor_name, a.entity_id::text, '-')
                            ELSE COALESCE(a.entity_id::text, '-')
                        END AS reference,
                        CASE
                            WHEN UPPER(COALESCE(a.entity_type, '')) = 'INVOICE'
                                THEN NULLIF(
                                    concat_ws(
                                        ', ',
                                        CASE
                                            WHEN NULLIF(invoice_row.po_number, '') IS NOT NULL
                                                THEN 'PO ' || invoice_row.po_number
                                        END,
                                        CASE
                                            WHEN NULLIF(invoice_row.grn_numbers, '') IS NOT NULL
                                                THEN 'GRN ' || invoice_row.grn_numbers
                                        END
                                    ),
                                    ''
                                )
                            WHEN UPPER(COALESCE(a.entity_type, '')) = 'USER'
                                THEN COALESCE(target_user.email, 'User record updated')
                            WHEN UPPER(COALESCE(a.entity_type, '')) = 'ROLE'
                                THEN COALESCE(target_role.code, 'Role record updated')
                            WHEN UPPER(COALESCE(a.entity_type, '')) IN ('VENDOR', 'VENDORUSER')
                                THEN COALESCE(target_vendor.oracle_vendor_id, actor_vendor.oracle_vendor_id, 'Vendor access record updated')
                            ELSE NULL
                        END AS details,
                        a.correlation_id,
                        a.created_at
                    FROM audit.audit_logs a
                    LEFT JOIN security.users actor
                        ON actor.id = a.user_id
                    LEFT JOIN master.vendor_users actor_vendor_user
                        ON actor_vendor_user.user_id = actor.id
                       AND actor_vendor_user.is_active = true
                    LEFT JOIN master.vendors actor_vendor
                        ON actor_vendor.id = actor_vendor_user.vendor_id
                    LEFT JOIN invoice.invoices invoice_row
                        ON invoice_row.id = a.entity_id
                       AND UPPER(COALESCE(a.entity_type, '')) = 'INVOICE'
                    LEFT JOIN security.users target_user
                        ON target_user.id = a.entity_id
                       AND UPPER(COALESCE(a.entity_type, '')) = 'USER'
                    LEFT JOIN security.roles target_role
                        ON target_role.id = a.entity_id
                       AND UPPER(COALESCE(a.entity_type, '')) = 'ROLE'
                    LEFT JOIN master.vendors target_vendor
                        ON target_vendor.id = a.entity_id
                       AND UPPER(COALESCE(a.entity_type, '')) = 'VENDOR'
                    ORDER BY a.created_at DESC
                    LIMIT 500
                    """;

                var list = new List<object>();

                await using var reader =
                    await command.ExecuteReaderAsync(ct);

                while (await reader.ReadAsync(ct))
                {
                    var action =
                        reader.IsDBNull(4)
                            ? string.Empty
                            : reader.GetString(4);

                    var entityType =
                        reader.IsDBNull(5)
                            ? string.Empty
                            : reader.GetString(5);

                    list.Add(
                        new
                        {
                            id = reader.GetInt64(0),

                            userId =
                                reader.IsDBNull(1)
                                    ? null
                                    : reader.GetGuid(1).ToString(),

                            user =
                                reader.IsDBNull(2)
                                    ? "System"
                                    : reader.GetString(2),

                            role =
                                reader.IsDBNull(3)
                                    ? "System"
                                    : reader.GetString(3),

                            action = ToDisplayText(action),
                            module = ToDisplayText(entityType),

                            recordReference =
                                reader.IsDBNull(7)
                                    ? "-"
                                    : reader.GetString(7),

                            details =
                                reader.IsDBNull(8)
                                    ? "-"
                                    : reader.GetString(8),

                            correlationId =
                                reader.IsDBNull(9)
                                    ? null
                                    : reader.GetGuid(9).ToString(),

                            createdAt =
                                reader.GetFieldValue<DateTimeOffset>(10)
                        });
                }

                return Results.Ok(list);
            });

        return app;
    }

    // =============================================================
    // ADMIN ACCESS
    // =============================================================

    private static void DemandAdmin(
        CurrentUser current)
    {
        if (!current.IsAdmin)
        {
            throw new ApiException(
                403,
                "Administrator access is required.");
        }
    }

    // =============================================================
    // REPLACE USER ROLES
    // =============================================================

    private static async Task ReplaceRoles(
        AppDbContext db,
        Guid userId,
        List<string>? codes,
        CancellationToken ct)
    {
        var wanted =
            (codes ?? new List<string>())
                .Select(
                    x =>
                        x.Trim()
                            .ToUpperInvariant())
                .Where(
                    x =>
                        !string.IsNullOrWhiteSpace(
                            x))
                .Distinct()
                .ToList();

        var roleIds =
            await db.Roles
                .Where(
                    x =>
                        x.IsActive &&
                        wanted.Contains(
                            x.Code))
                .Select(
                    x =>
                        x.Id)
                .ToListAsync(ct);

        var existing =
            await db.UserRoles
                .Where(
                    x =>
                        x.UserId ==
                        userId)
                .ToListAsync(ct);

        db.UserRoles.RemoveRange(
            existing);

        foreach (
            var roleId
            in roleIds)
        {
            db.UserRoles.Add(
                new UserRole
                {
                    UserId =
                        userId,

                    RoleId =
                        roleId
                });
        }

        await db.SaveChangesAsync(ct);
    }

    // =============================================================
    // AUDIT HELPER
    // =============================================================

    private static Task<int> Audit(
        AppDbContext db,
        Guid userId,
        string action,
        string entityType,
        Guid entityId,
        CancellationToken ct)
    {
        return db.Database
            .ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO audit.audit_logs
                (
                    user_id,
                    action,
                    entity_type,
                    entity_id,
                    created_at
                )
                VALUES
                (
                    {userId},
                    {action},
                    {entityType},
                    {entityId},
                    now()
                )
                """,
                ct);
    }

    // =============================================================
    // DISPLAY TEXT HELPER
    // =============================================================

    private static string ToDisplayText(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "-";
        }

        return string.Join(
            " ",
            value
                .Trim()
                .Replace('-', '_')
                .Split(
                    '_',
                    StringSplitOptions.RemoveEmptyEntries)
                .Select(
                    part =>
                        part.Length == 1
                            ? part.ToUpperInvariant()
                            : char.ToUpperInvariant(part[0]) +
                              part[1..].ToLowerInvariant()));
    }

    // =============================================================
    // REQUEST MODELS
    // =============================================================

    public sealed record UserMutationRequest(
        string FullName,
        string Email,
        string UserType,
        List<string>? RoleCodes,
        string? Password,
        bool IsActive = true,
        bool IsSuperAdmin = false);

    public sealed record RolePermissionsRequest(
        List<Guid>? PermissionIds);

    public sealed record RoleMutationRequest(
        string Name,
        string Code);
}