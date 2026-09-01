using BusinessPartnerPortal.Api.Common;
using BusinessPartnerPortal.Api.Data;
using BusinessPartnerPortal.Api.Oracle;
using BusinessPartnerPortal.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace BusinessPartnerPortal.Api.Features.Oracle;

public static class OracleEndpoints
{
    public static IEndpointRouteBuilder MapOracleEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/v1/oracle")
            .RequireAuthorization()
            .WithTags("Oracle");

        // ------------------------------------------------------------
        // GET: /api/v1/oracle/suppliers/my
        // Returns Oracle supplier information for logged-in vendor.
        // ------------------------------------------------------------
        group.MapGet(
            "/suppliers/my",
            async (
                CurrentUser current,
                AppDbContext db,
                OracleService oracle,
                CancellationToken ct) =>
            {
                var oracleVendorId = await ResolveOracleVendorId(
                    current,
                    db,
                    ct);

                var supplier = await oracle.GetSupplierByVendorIdAsync(
                    oracleVendorId,
                    ct);

                if (supplier is null)
                {
                    return Results.NotFound(
                        new
                        {
                            message = "Oracle supplier not found."
                        });
                }

                return Results.Ok(supplier);
            });

        // ------------------------------------------------------------
        // GET: /api/v1/oracle/po-grns/my
        // Returns PO / GRN information for logged-in vendor.
        // Optional filter: ?poNumber=XXXX
        // ------------------------------------------------------------
        group.MapGet(
            "/po-grns/my",
            async (
                string? poNumber,
                CurrentUser current,
                AppDbContext db,
                OracleService oracle,
                CancellationToken ct) =>
            {
                var oracleVendorId = await ResolveOracleVendorId(
                    current,
                    db,
                    ct);

                var rows = await oracle.GetPoGrnsAsync(
                    oracleVendorId,
                    poNumber,
                    ct);

                return Results.Ok(rows);
            });

        // ------------------------------------------------------------
        // GET: /api/v1/oracle/invoices/my
        // Returns Oracle invoice/payment status for logged-in vendor.
        // ------------------------------------------------------------
        group.MapGet(
            "/invoices/my",
            async (
                CurrentUser current,
                AppDbContext db,
                OracleService oracle,
                CancellationToken ct) =>
            {
                var oracleVendorId = await ResolveOracleVendorId(
                    current,
                    db,
                    ct);

                var invoices = await oracle.GetInvoicesAsync(
                    oracleVendorId,
                    ct);

                return Results.Ok(invoices);
            });

        // ------------------------------------------------------------
        // GET: /api/v1/oracle/suppliers
        // Internal/Admin supplier lookup.
        // Vendor users cannot access complete supplier list.
        // ------------------------------------------------------------
        group.MapGet(
            "/suppliers",
            async (
                CurrentUser current,
                OracleService oracle,
                CancellationToken ct) =>
            {
                if (string.Equals(
                    current.UserType,
                    "VENDOR",
                    StringComparison.OrdinalIgnoreCase))
                {
                    throw new ApiException(
                        403,
                        "Internal access required.");
                }

                var suppliers = await oracle.GetSuppliersAsync(ct);

                return Results.Ok(suppliers);
            });

        // ------------------------------------------------------------
        // GET: /api/v1/oracle/diagnostics/connectivity
        // Development-only Oracle connectivity test.
        // ------------------------------------------------------------
        group.MapGet(
            "/diagnostics/connectivity",
            async (
                IWebHostEnvironment env,
                OracleService oracle,
                CancellationToken ct) =>
            {
                if (!env.IsDevelopment())
                {
                    return Results.NotFound();
                }

                try
                {
                    var connected = await oracle.TestConnectivityAsync(ct);

                    return Results.Ok(
                        new
                        {
                            connected,
                            test = "SELECT 1 FROM DUAL",
                            result = 1
                        });
                }
                catch (
                    global::Oracle.ManagedDataAccess.Client.OracleException ex)
                {
                    var oracleMessage =
                        ex.Message
                            .Split(
                                new[] { '\r', '\n' },
                                StringSplitOptions.RemoveEmptyEntries)
                            .FirstOrDefault()
                        ?? ex.Message;

                    return Results.Problem(
                        detail: $"ORA-{ex.Number:D5}: {oracleMessage}",
                        statusCode: StatusCodes.Status503ServiceUnavailable,
                        title: "Oracle connectivity failed");
                }
                catch (Exception ex)
                {
                    return Results.Problem(
                        detail: ex.Message,
                        statusCode: StatusCodes.Status503ServiceUnavailable,
                        title: "Oracle connectivity failed");
                }
            });

        return app;
    }

    // ------------------------------------------------------------
    // Resolve logged-in portal vendor -> Oracle VENDOR_ID.
    //
    // Security:
    // Vendor does not provide Oracle Vendor ID from frontend.
    // Mapping is always resolved server-side:
    //
    // Logged-in User
    //      -> VendorUsers
    //      -> Vendors
    //      -> OracleVendorId
    //      -> Oracle VENDOR_ID
    // ------------------------------------------------------------
    private static async Task<decimal> ResolveOracleVendorId(
        CurrentUser current,
        AppDbContext db,
        CancellationToken ct)
    {
        var portalVendorId = await current.GetVendorIdAsync(ct);

        if (portalVendorId is null)
        {
            throw new ApiException(
                403,
                "Vendor account mapping is missing.");
        }

        var oracleVendorIdRaw = await db.Vendors
            .AsNoTracking()
            .Where(x => x.Id == portalVendorId.Value)
            .Select(x => x.OracleVendorId)
            .FirstOrDefaultAsync(ct);

        if (string.IsNullOrWhiteSpace(oracleVendorIdRaw))
        {
            throw new ApiException(
                409,
                "Oracle vendor mapping is missing.");
        }

        if (!decimal.TryParse(
            oracleVendorIdRaw,
            out var oracleVendorId))
        {
            throw new ApiException(
                409,
                "Oracle vendor mapping is invalid.");
        }

        return oracleVendorId;
    }
}