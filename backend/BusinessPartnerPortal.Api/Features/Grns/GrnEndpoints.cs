using System.Globalization;

using BusinessPartnerPortal.Api.Common;
using BusinessPartnerPortal.Api.Data;
using BusinessPartnerPortal.Api.Oracle;
using BusinessPartnerPortal.Api.Security;

using Microsoft.EntityFrameworkCore;

namespace BusinessPartnerPortal.Api.Features.Grns;

public static class GrnEndpoints
{
    private static readonly HashSet<string> PendingQcStatuses =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "PENDING",
            "PENDING QC",
            "PENDING_QC",
            "AWAITING INSPECTION",
            "NOT RECEIVED"
        };

    public static IEndpointRouteBuilder MapGrnEndpoints(
        this IEndpointRouteBuilder app)
    {
        app.MapGet(
                "/api/v1/purchase-orders/{poId:guid}/grns",
                async (
                    Guid poId,
                    CurrentUser current,
                    AppDbContext db,
                    CancellationToken ct) =>
                {
                    await current.DemandAsync(
                        "GRN.VIEW",
                        ct);

                    var po =
                        await db.PurchaseOrders
                            .FirstOrDefaultAsync(
                                x => x.Id == poId,
                                ct)
                        ?? throw new ApiException(
                            404,
                            "Purchase order not found.");

                    if (
                        current.UserType ==
                        "VENDOR"
                    )
                    {
                        var vendorId =
                            await current.GetVendorIdAsync(
                                ct)
                            ?? throw new ApiException(
                                403,
                                "Vendor account mapping is missing.");

                        if (
                            po.VendorId !=
                            vendorId
                        )
                        {
                            throw new ApiException(
                                403,
                                "You are not allowed to access this purchase order.");
                        }
                    }

                    var rows =
                        await db.Grns
                            .Where(
                                x =>
                                    x.PurchaseOrderId ==
                                    poId
                                    &&
                                    (
                                        x.Status == "OPEN"
                                        ||
                                        x.Status == "PARTIAL"
                                    ))
                            .OrderByDescending(
                                x =>
                                    x.GrnDate)
                            .Select(
                                x =>
                                    new
                                    {
                                        x.Id,

                                        x.GrnNumber,

                                        poNumber =
                                            po.PoNumber,

                                        x.GrnDate,

                                        x.Status,

                                        x.QcStatus
                                    })
                            .ToListAsync(
                                ct);

                    return Results.Ok(
                        rows);
                })
            .RequireAuthorization()
            .WithTags("GRNs");

        // ========================================================
        // SUPPLY CHAIN - QC PENDING GRNs
        // ========================================================

        app.MapGet(
                "/api/v1/grns/qc-pending",
                async (
                    CurrentUser current,
                    AppDbContext db,
                    OracleService oracle,
                    CancellationToken ct) =>
                {
                    if (
                        !current.IsAdmin
                        &&
                        !string.Equals(
                            current.UserType,
                            "INTERNAL",
                            StringComparison.OrdinalIgnoreCase)
                    )
                    {
                        return Results.Forbid();
                    }

                    if (
                        !current.IsAdmin
                    )
                    {
                        var permissions =
                            await current.GetPermissionsAsync(
                                ct);

                        if (
                            !permissions.Contains(
                                "SUPPLY_CHAIN_GRN_VIEW")
                            &&
                            !permissions.Contains(
                                "VENDOR.MANAGE")
                        )
                        {
                            throw new ApiException(
                                StatusCodes.Status403Forbidden,
                                "You do not have permission to view QC pending GRNs.");
                        }
                    }

                    var activeVendorRows =
                        await (
                            from vendor
                                in db.Vendors
                                    .AsNoTracking()

                            join mapping
                                in db.VendorUsers
                                    .AsNoTracking()
                                on vendor.Id
                                equals mapping.VendorId

                            join user
                                in db.Users
                                    .AsNoTracking()
                                on mapping.UserId
                                equals user.Id

                            where
                                vendor.IsActive
                                &&
                                mapping.IsActive
                                &&
                                user.IsActive
                                &&
                                vendor.OracleVendorId != null
                                &&
                                vendor.OracleVendorId != ""

                            select new
                            {
                                vendor.Id,
                                vendor.VendorCode,
                                vendor.VendorName,
                                vendor.OracleVendorId
                            }
                        )
                        .Distinct()
                        .OrderBy(
                            x =>
                                x.VendorName)
                        .ToListAsync(
                            ct);

                    var result =
                        new List<object>();

                    foreach (
                        var vendor
                        in activeVendorRows
                    )
                    {
                        if (
                            !decimal.TryParse(
                                vendor.OracleVendorId,
                                NumberStyles.Number,
                                CultureInfo.InvariantCulture,
                                out var oracleVendorId)
                        )
                        {
                            continue;
                        }

                        var oracleRows =
                            await oracle.GetPoGrnsAsync(
                                oracleVendorId,
                                null,
                                ct);

                        var pendingRows =
                            oracleRows
                                .Where(
                                    row =>
                                        IsPendingQc(
                                            row.InspectionStatus))
                                .GroupBy(
                                    row =>
                                        string.Join(
                                            "|",
                                            row.VendorId,
                                            row.PoNumber,
                                            row.GrnNumber,
                                            row.PoLineId,
                                            row.ShipmentLineId,
                                            row.ItemId))
                                .Select(
                                    group =>
                                        group.First())
                                .OrderByDescending(
                                    row =>
                                        row.ReceiptDate)
                                .ThenByDescending(
                                    row =>
                                        row.PoNumber)
                                .ToList();

                        foreach (
                            var row
                            in pendingRows
                        )
                        {
                            result.Add(
                                new
                                {
                                    portalVendorId =
                                        vendor.Id,

                                    vendorCode =
                                        vendor.VendorCode,

                                    vendorName =
                                        vendor.VendorName,

                                    oracleVendorId =
                                        vendor.OracleVendorId,

                                    poNumber =
                                        row.PoNumber,

                                    prNumber =
                                        string.IsNullOrWhiteSpace(
                                            row.PrNumber)
                                            ? "-"
                                            : row.PrNumber,

                                    // NEW - PO DATE
                                    poDate =
                                        row.PoCreationDate,

                                    grnNumber =
                                        string.IsNullOrWhiteSpace(
                                            row.GrnNumber)
                                            ? "-"
                                            : row.GrnNumber,

                                    grnDate =
                                        row.ReceiptDate,

                                    poLineNumber =
                                        row.PoLineNum,

                                    itemCode =
                                        row.ItemCode,

                                    itemDescription =
                                        row.ItemDescription,

                                    uom =
                                        row.Uom,

                                    receivedQuantity =
                                        row.GrnReceivedQuantity
                                        ??
                                        row.ReceivedQuantity,

                                    qcStatus =
                                        "Pending with QC",

                                    oracleQcStatus =
                                        row.InspectionStatus,

                                    agingDays =
                                        CalculateAgingDays(
                                            row.ReceiptDate)
                                });
                        }
                    }

                    return Results.Ok(
                        result);
                })
            .RequireAuthorization()
            .WithTags("GRNs");

        return app;
    }

    private static bool IsPendingQc(
        string? status)
    {
        if (
            string.IsNullOrWhiteSpace(
                status)
        )
        {
            return false;
        }

        return PendingQcStatuses.Contains(
            status.Trim());
    }

    private static int CalculateAgingDays(
        DateTime? receiptDate)
    {
        if (
            !receiptDate.HasValue
        )
        {
            return 0;
        }

        var days =
            (
                DateTime.UtcNow.Date
                -
                receiptDate.Value.Date
            )
            .Days;

        return Math.Max(
            0,
            days);
    }
}