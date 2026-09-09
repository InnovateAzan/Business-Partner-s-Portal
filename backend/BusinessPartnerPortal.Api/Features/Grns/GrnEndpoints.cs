using BusinessPartnerPortal.Api.Common;
using BusinessPartnerPortal.Api.Data;
using BusinessPartnerPortal.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace BusinessPartnerPortal.Api.Features.Grns;

public static class GrnEndpoints
{
    public static IEndpointRouteBuilder MapGrnEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/purchase-orders/{poId:guid}/grns", async (Guid poId, CurrentUser current, AppDbContext db, CancellationToken ct) =>
        {
            await current.DemandAsync("GRN.VIEW", ct);
            var po = await db.PurchaseOrders.FirstOrDefaultAsync(x => x.Id == poId, ct) ?? throw new ApiException(404, "Purchase order not found.");
            if (current.UserType == "VENDOR")
            {
                var vendorId = await current.GetVendorIdAsync(ct) ?? throw new ApiException(403, "Vendor account mapping is missing.");
                if (po.VendorId != vendorId) throw new ApiException(403, "You are not allowed to access this purchase order.");
            }
            var rows = await db.Grns.Where(x => x.PurchaseOrderId == poId && (x.Status == "OPEN" || x.Status == "PARTIAL"))
                .OrderByDescending(x => x.GrnDate).Select(x => new { x.Id, x.GrnNumber, poNumber = po.PoNumber, x.GrnDate, x.Status, x.QcStatus }).ToListAsync(ct);
            return Results.Ok(rows);
        }).RequireAuthorization().WithTags("GRNs");
        return app;
    }
}
