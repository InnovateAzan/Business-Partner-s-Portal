using BusinessPartnerPortal.Api.Common;
using BusinessPartnerPortal.Api.Data;
using BusinessPartnerPortal.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace BusinessPartnerPortal.Api.Features.PurchaseOrders;

public static class PurchaseOrderEndpoints
{
    public static IEndpointRouteBuilder MapPurchaseOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/purchase-orders").RequireAuthorization().WithTags("Purchase Orders");
        group.MapGet("/open", async (CurrentUser current, AppDbContext db, CancellationToken ct) =>
        {
            await current.DemandAsync("PO.VIEW", ct);
            var query = db.PurchaseOrders.Where(x => x.Status == "OPEN" || x.Status == "PARTIAL");
            if (current.UserType == "VENDOR")
            {
                var vendorId = await current.GetVendorIdAsync(ct) ?? throw new ApiException(403, "Vendor account mapping is missing.");
                query = query.Where(x => x.VendorId == vendorId);
            }
            var rows = await query.OrderByDescending(x => x.PoDate).Select(x => new { x.Id, x.PoNumber, x.PoDate, x.CurrencyCode, x.TotalAmount, x.RemainingAmount, x.Status }).ToListAsync(ct);
            return Results.Ok(rows);
        });
        return app;
    }
}
