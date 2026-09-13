using System.Data.Common;

using BusinessPartnerPortal.Api.Common;
using BusinessPartnerPortal.Api.Data;
using BusinessPartnerPortal.Api.Domain;
using BusinessPartnerPortal.Api.Oracle;
using BusinessPartnerPortal.Api.Security;
using BusinessPartnerPortal.Api.Services;

using Microsoft.EntityFrameworkCore;

namespace BusinessPartnerPortal.Api.Features.Admin;

public static class VendorAccessEndpoints
{
    public sealed record SavePortalContactRequest(
        decimal OracleVendorId,
        string? Email,
        string? Phone
    );

    public sealed record GrantVendorAccessRequest(
        decimal OracleVendorId
    );

    public static IEndpointRouteBuilder MapAdminVendorAccessEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/v1/admin/vendor-access")
            .RequireAuthorization()
            .WithTags("Admin Vendor Access");

        // ========================================================
        // SEARCH ORACLE VENDORS
        //
        // We intentionally fetch the Oracle supplier list first
        // and filter in C#.
        //
        // This fixes the case where the Oracle view search query
        // was returning no rows for a visible supplier like TUAN.
        // ========================================================

        group.MapGet(
            "/oracle-vendors",
            async (
                string? search,
                CurrentUser current,
                AppDbContext db,
                OracleService oracle,
                CancellationToken ct) =>
            {
                EnsureAuthorized(current);

                var suppliers =
                    await oracle.GetSuppliersAsync(ct);

                var query =
                    suppliers.AsEnumerable();

                if (!string.IsNullOrWhiteSpace(search))
                {
                    var term =
                        search
                            .Trim();

                    query =
                        query.Where(x =>
                            Contains(
                                x.VendorName,
                                term)
                            ||
                            Contains(
                                x.VendorId,
                                term)
                            ||
                            Contains(
                                x.SupplierNumber,
                                term)
                            ||
                            Contains(
                                x.Email,
                                term)
                            ||
                            Contains(
                                x.Phone,
                                term));
                }

                // Return the complete Oracle supplier result set.
                // The previous Take(500) cap caused the selector to show
                // only a subset while the dashboard correctly reported
                // the full Oracle vendor count (for example 3,661).
                var rows =
                    query
                        .OrderBy(x => x.VendorName)
                        .ThenBy(x => x.VendorId)
                        .ToList();

                // Load portal contact overrides once instead of executing
                // one PostgreSQL query per Oracle vendor. This keeps the
                // full-list request practical even with several thousand
                // suppliers.
                var portalContacts =
                    await GetPortalContactsAsync(
                        db,
                        ct);

                var result =
                    new List<object>();

                foreach (var supplier in rows)
                {
                    portalContacts.TryGetValue(
                        supplier.VendorId,
                        out var portalContact);

                    portalContact ??=
                        new PortalContact(
                            null,
                            null);

                    result.Add(
                        new
                        {
                            supplier.VendorId,
                            supplier.SupplierNumber,
                            supplier.VendorName,
                            supplier.VendorSiteId,
                            supplier.VendorSiteCode,
                            supplier.OrgId,
                            supplier.OperatingUnit,

                            // Oracle originals
                            oracleEmail =
                                supplier.Email,

                            oraclePhone =
                                supplier.Phone,

                            // Portal override if available,
                            // otherwise Oracle value.
                            email =
                                FirstNonEmpty(
                                    portalContact.Email,
                                    supplier.Email),

                            phone =
                                FirstNonEmpty(
                                    portalContact.Phone,
                                    supplier.Phone),

                            portalEmail =
                                portalContact.Email,

                            portalPhone =
                                portalContact.Phone,

                            supplier.TaxNumber,
                            supplier.VendorType,
                            supplier.AddressLine1,
                            supplier.AddressLine2,
                            supplier.AddressLine3,
                            supplier.City,
                            supplier.State,
                            supplier.PostalCode,
                            supplier.Country
                        });
                }

                return Results.Ok(result);
            });

        // ========================================================
        // VENDOR DETAIL
        // ========================================================

        group.MapGet(
            "/oracle-vendors/{oracleVendorId:decimal}",
            async (
                decimal oracleVendorId,
                CurrentUser current,
                AppDbContext db,
                OracleService oracle,
                CancellationToken ct) =>
            {
                EnsureAuthorized(current);

                var supplier =
                    await oracle.GetSupplierByVendorIdAsync(
                        oracleVendorId,
                        ct)
                    ??
                    throw new ApiException(
                        404,
                        "Oracle supplier was not found.");

                var portalContact =
                    await GetPortalContactAsync(
                        db,
                        supplier.VendorId,
                        ct);

                return Results.Ok(
                    new
                    {
                        supplier.VendorId,
                        supplier.SupplierNumber,
                        supplier.VendorName,
                        supplier.VendorSiteId,
                        supplier.VendorSiteCode,
                        supplier.OrgId,
                        supplier.OperatingUnit,

                        oracleEmail =
                            supplier.Email,

                        oraclePhone =
                            supplier.Phone,

                        email =
                            FirstNonEmpty(
                                portalContact.Email,
                                supplier.Email),

                        phone =
                            FirstNonEmpty(
                                portalContact.Phone,
                                supplier.Phone),

                        portalEmail =
                            portalContact.Email,

                        portalPhone =
                            portalContact.Phone,

                        supplier.TaxNumber,
                        supplier.VendorType,
                        supplier.AddressLine1,
                        supplier.AddressLine2,
                        supplier.AddressLine3,
                        supplier.City,
                        supplier.State,
                        supplier.PostalCode,
                        supplier.Country
                    });
            });

        // ========================================================
        // SAVE PORTAL-SPECIFIC EMAIL / PHONE
        //
        // Oracle data stays read-only.
        // ========================================================

        group.MapPut(
            "/portal-contact",
            async (
                SavePortalContactRequest request,
                CurrentUser current,
                AppDbContext db,
                OracleService oracle,
                CancellationToken ct) =>
            {
                EnsureAuthorized(current);

                if (request.OracleVendorId <= 0)
                {
                    throw new ApiException(
                        400,
                        "A valid Oracle Vendor ID is required.");
                }

                var supplier =
                    await oracle.GetSupplierByVendorIdAsync(
                        request.OracleVendorId,
                        ct)
                    ??
                    throw new ApiException(
                        404,
                        "Oracle supplier was not found.");

                var email =
                    NormalizeNullable(
                        request.Email);

                var phone =
                    NormalizeNullable(
                        request.Phone);

                if (
                    !string.IsNullOrWhiteSpace(email)
                    &&
                    !IsValidEmail(email))
                {
                    throw new ApiException(
                        400,
                        "Enter a valid email address.");
                }

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
                    INSERT INTO master.vendor_portal_contacts
                    (
                        oracle_vendor_id,
                        email,
                        phone,
                        updated_at,
                        updated_by
                    )
                    VALUES
                    (
                        @oracle_vendor_id,
                        @email,
                        @phone,
                        now(),
                        @updated_by
                    )
                    ON CONFLICT (oracle_vendor_id)
                    DO UPDATE
                    SET
                        email = EXCLUDED.email,
                        phone = EXCLUDED.phone,
                        updated_at = now(),
                        updated_by = EXCLUDED.updated_by
                    """;

                AddParameter(
                    command,
                    "@oracle_vendor_id",
                    supplier.VendorId);

                AddParameter(
                    command,
                    "@email",
                    email);

                AddParameter(
                    command,
                    "@phone",
                    phone);

                AddParameter(
                    command,
                    "@updated_by",
                    current.UserId);

                await command.ExecuteNonQueryAsync(ct);

                return Results.Ok(
                    new
                    {
                        message =
                            "Portal contact details saved successfully.",

                        oracleVendorId =
                            request.OracleVendorId,

                        vendorName =
                            supplier.VendorName,

                        email,

                        phone
                    });
            });

        // ========================================================
        // GRANT ACCESS
        //
        // Oracle vendor
        // -> use portal email override if present
        // -> otherwise Oracle registered email
        // -> create/reuse vendor user
        // -> assign VENDOR role
        // -> create mapping
        // -> password setup email
        // ========================================================

        group.MapPost(
            "/grant",
            async (
                GrantVendorAccessRequest request,
                CurrentUser current,
                AppDbContext db,
                OracleService oracle,
                PasswordSetupTokenService passwordTokens,
                EmailOtpSender emailSender,
                IConfiguration config,
                IWebHostEnvironment env,
                CancellationToken ct) =>
            {
                EnsureAuthorized(current);

                if (request.OracleVendorId <= 0)
                {
                    throw new ApiException(
                        400,
                        "A valid Oracle Vendor ID is required.");
                }

                // Always re-read from Oracle.
                var supplier =
                    await oracle.GetSupplierByVendorIdAsync(
                        request.OracleVendorId,
                        ct)
                    ??
                    throw new ApiException(
                        404,
                        "Oracle supplier was not found.");

                var portalContact =
                    await GetPortalContactAsync(
                        db,
                        supplier.VendorId,
                        ct);

                var email =
                    FirstNonEmpty(
                        portalContact.Email,
                        supplier.Email)
                    ?.Trim()
                    .ToLowerInvariant();

                if (string.IsNullOrWhiteSpace(email))
                {
                    throw new ApiException(
                        409,
                        "This supplier does not have an Oracle or portal email address. Add a portal email before granting access.");
                }

                if (!IsValidEmail(email))
                {
                    throw new ApiException(
                        409,
                        "The configured vendor email address is invalid.");
                }

                if (
                    string.IsNullOrWhiteSpace(
                        supplier.VendorName))
                {
                    throw new ApiException(
                        409,
                        "Oracle supplier name is missing.");
                }

                var oracleVendorIdText =
                    supplier.VendorId;

                await using var transaction =
                    await db.Database
                        .BeginTransactionAsync(ct);

                User portalUser;
                Vendor vendor;

                try
                {
                    // =============================================
                    // VENDOR
                    // =============================================

                    vendor =
                        await db.Vendors
                            .FirstOrDefaultAsync(
                                x =>
                                    x.OracleVendorId ==
                                    oracleVendorIdText,
                                ct);

                    if (vendor is null)
                    {
                        vendor =
                            new Vendor
                            {
                                Id =
                                    Guid.NewGuid(),

                                VendorCode =
                                    !string.IsNullOrWhiteSpace(
                                        supplier.SupplierNumber)
                                        ? supplier.SupplierNumber
                                        : $"ORACLE-{oracleVendorIdText}",

                                VendorName =
                                    supplier.VendorName,

                                OracleVendorId =
                                    oracleVendorIdText,

                                IsActive =
                                    true
                            };

                        db.Vendors.Add(vendor);

                        // Important FK fix:
                        // save parent before vendor_users.
                        await db.SaveChangesAsync(ct);
                    }
                    else
                    {
                        vendor.VendorName =
                            supplier.VendorName;

                        if (
                            !string.IsNullOrWhiteSpace(
                                supplier.SupplierNumber))
                        {
                            vendor.VendorCode =
                                supplier.SupplierNumber;
                        }

                        vendor.IsActive =
                            true;

                        await db.SaveChangesAsync(ct);
                    }

                    // =============================================
                    // USER
                    // =============================================

                    portalUser =
                        await db.Users
                            .FirstOrDefaultAsync(
                                x =>
                                    x.Email.ToLower() ==
                                    email,
                                ct);

                    if (portalUser is null)
                    {
                        portalUser =
                            new User
                            {
                                Id =
                                    Guid.NewGuid(),

                                FullName =
                                    supplier.VendorName,

                                Email =
                                    email,

                                // Password will be set from email.
                                PasswordHash =
                                    null,

                                UserType =
                                    "VENDOR",

                                IsActive =
                                    true,

                                IsSuperAdmin =
                                    false,

                                MfaEnabled =
                                    false,

                                FailedLoginAttempts =
                                    0,

                                CreatedAt =
                                    DateTimeOffset.UtcNow,

                                UpdatedAt =
                                    DateTimeOffset.UtcNow
                            };

                        db.Users.Add(portalUser);

                        await db.SaveChangesAsync(ct);
                    }
                    else
                    {
                        if (
                            !string.Equals(
                                portalUser.UserType,
                                "VENDOR",
                                StringComparison.OrdinalIgnoreCase))
                        {
                            throw new ApiException(
                                409,
                                "This email address is already used by a non-vendor portal account.");
                        }

                        portalUser.FullName =
                            supplier.VendorName;

                        portalUser.IsActive =
                            true;

                        portalUser.IsSuperAdmin =
                            false;

                        portalUser.FailedLoginAttempts =
                            0;

                        portalUser.LockoutUntil =
                            null;

                        portalUser.UpdatedAt =
                            DateTimeOffset.UtcNow;

                        await db.SaveChangesAsync(ct);
                    }

                    // =============================================
                    // VENDOR ROLE
                    // =============================================

                    var vendorRole =
                        await db.Roles
                            .FirstOrDefaultAsync(
                                x =>
                                    x.Code ==
                                        "VENDOR"
                                    &&
                                    x.IsActive,
                                ct)
                        ??
                        throw new ApiException(
                            500,
                            "VENDOR role is missing.");

                    // Vendor user gets only VENDOR role.
                    var existingRoles =
                        await db.UserRoles
                            .Where(
                                x =>
                                    x.UserId ==
                                    portalUser.Id)
                            .ToListAsync(ct);

                    db.UserRoles.RemoveRange(
                        existingRoles);

                    db.UserRoles.Add(
                        new UserRole
                        {
                            UserId =
                                portalUser.Id,

                            RoleId =
                                vendorRole.Id
                        });

                    // =============================================
                    // EXISTING USER MAPPINGS
                    // =============================================

                    var existingMappings =
                        await db.VendorUsers
                            .Where(
                                x =>
                                    x.UserId ==
                                    portalUser.Id)
                            .ToListAsync(ct);

                    foreach (
                        var oldMapping
                        in existingMappings)
                    {
                        oldMapping.IsActive =
                            false;

                        oldMapping.IsPrimary =
                            false;
                    }

                    // =============================================
                    // SAME MAPPING
                    // =============================================

                    var mapping =
                        await db.VendorUsers
                            .FirstOrDefaultAsync(
                                x =>
                                    x.UserId ==
                                        portalUser.Id
                                    &&
                                    x.VendorId ==
                                        vendor.Id,
                                ct);

                    if (mapping is null)
                    {
                        mapping =
                            new VendorUser
                            {
                                UserId =
                                    portalUser.Id,

                                VendorId =
                                    vendor.Id,

                                IsPrimary =
                                    true,

                                IsActive =
                                    true
                            };

                        db.VendorUsers.Add(mapping);
                    }
                    else
                    {
                        mapping.IsPrimary =
                            true;

                        mapping.IsActive =
                            true;
                    }

                    await db.SaveChangesAsync(ct);

                    await transaction.CommitAsync(ct);
                }
                catch
                {
                    await transaction.RollbackAsync(ct);
                    throw;
                }

                // =============================================
                // PASSWORD SETUP EMAIL
                // =============================================

                var token =
                    passwordTokens.Create(
                        portalUser);

                var frontendUrl =
                    (
                        config["FRONTEND_URL"]
                        ??
                        "http://localhost:5173"
                    )
                    .TrimEnd('/');

                var setupUrl =
                    $"{frontendUrl}/set-password?token={Uri.EscapeDataString(token)}";

                await emailSender.SendPortalAccessAsync(
                    portalUser.Email,
                    supplier.VendorName,
                    setupUrl,
                    ct);

                var smtpEnabled =
                    bool.TryParse(
                        config["SMTP_ENABLED"],
                        out var smtp)
                    &&
                    smtp;

                return Results.Ok(
                    new
                    {
                        accessGranted =
                            true,

                        emailSent =
                            smtpEnabled,

                        vendorId =
                            vendor.Id,

                        userId =
                            portalUser.Id,

                        oracleVendorId =
                            request.OracleVendorId,

                        vendorName =
                            supplier.VendorName,

                        email =
                            portalUser.Email,

                        role =
                            "VENDOR",

                        devSetupUrl =
                            env.IsDevelopment()
                            &&
                            !smtpEnabled
                                ? setupUrl
                                : null,

                        message =
                            smtpEnabled
                                ? "Vendor portal access enabled and password setup email sent."
                                : "Vendor portal access enabled."
                    });
            });

        return app;
    }

    // ============================================================
    // HELPERS
    // ============================================================

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

    private static bool Contains(
        string? source,
        string term)
    {
        return
            !string.IsNullOrWhiteSpace(source)
            &&
            source.Contains(
                term,
                StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizeNullable(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    private static string? FirstNonEmpty(
        string? preferred,
        string? fallback)
    {
        if (
            !string.IsNullOrWhiteSpace(
                preferred))
        {
            return preferred.Trim();
        }

        if (
            !string.IsNullOrWhiteSpace(
                fallback))
        {
            return fallback.Trim();
        }

        return null;
    }

    private static bool IsValidEmail(
        string email)
    {
        try
        {
            var parsed =
                new System.Net.Mail.MailAddress(
                    email);

            return
                string.Equals(
                    parsed.Address,
                    email,
                    StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private sealed record PortalContact(
        string? Email,
        string? Phone);

    private static async Task<Dictionary<string, PortalContact>>
        GetPortalContactsAsync(
            AppDbContext db,
            CancellationToken ct)
    {
        var result =
            new Dictionary<string, PortalContact>(
                StringComparer.OrdinalIgnoreCase);

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
            SELECT oracle_vendor_id, email, phone
            FROM master.vendor_portal_contacts
            """;

        await using var reader =
            await command.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            var oracleVendorId =
                Convert.ToString(
                    reader.GetValue(0));

            if (string.IsNullOrWhiteSpace(oracleVendorId))
            {
                continue;
            }

            result[oracleVendorId] =
                new PortalContact(
                    reader.IsDBNull(1)
                        ? null
                        : reader.GetString(1),
                    reader.IsDBNull(2)
                        ? null
                        : reader.GetString(2));
        }

        return result;
    }

    private static async Task<PortalContact>
        GetPortalContactAsync(
            AppDbContext db,
            string oracleVendorId,
            CancellationToken ct)
    {
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
            SELECT email, phone
            FROM master.vendor_portal_contacts
            WHERE oracle_vendor_id = @oracle_vendor_id
            LIMIT 1
            """;

        AddParameter(
            command,
            "@oracle_vendor_id",
            oracleVendorId);

        await using var reader =
            await command.ExecuteReaderAsync(ct);

        if (!await reader.ReadAsync(ct))
        {
            return new PortalContact(
                null,
                null);
        }

        return new PortalContact(
            reader.IsDBNull(0)
                ? null
                : reader.GetString(0),

            reader.IsDBNull(1)
                ? null
                : reader.GetString(1));
    }

    private static void AddParameter(
        DbCommand command,
        string name,
        object? value)
    {
        var parameter =
            command.CreateParameter();

        parameter.ParameterName =
            name;

        parameter.Value =
            value
            ??
            DBNull.Value;

        command.Parameters.Add(
            parameter);
    }
}
