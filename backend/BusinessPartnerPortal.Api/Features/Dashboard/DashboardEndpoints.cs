using BusinessPartnerPortal.Api.Data;
using BusinessPartnerPortal.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace BusinessPartnerPortal.Api.Features.Dashboard;

public static class DashboardEndpoints
{
    public static IEndpointRouteBuilder MapDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/dashboard", async (CurrentUser current, AppDbContext db, CancellationToken ct) =>
        {
            var vendorId = await current.GetVendorIdAsync(ct);
            var invoices = db.Invoices.Where(x => x.DeletedAt == null);
            if (!current.IsAdmin && current.UserType == "VENDOR") invoices = invoices.Where(x => x.VendorId == vendorId);

            var data = new
            {
                totalSubmittedInvoices = await invoices.CountAsync(x => x.Status != "DRAFT", ct),
                drafts = await invoices.CountAsync(x => x.Status == "DRAFT", ct),
                inProcess = await invoices.CountAsync(x => (x.Status == "SUBMITTED" || x.Status == "SENT_TO_ORACLE" || x.Status == "UNDER_FINANCE_REVIEW" || x.Status == "RESUBMITTED"), ct),
                validated = await invoices.CountAsync(x => x.Status == "ACCEPTED", ct),
                rejected = await invoices.CountAsync(x => x.Status == "RETURNED", ct),
                paid = await invoices.CountAsync(x => x.Status == "PAID", ct),
                poOutstanding = vendorId.HasValue ? await db.PurchaseOrders.CountAsync(x => x.VendorId == vendorId && (x.Status == "OPEN" || x.Status == "PARTIAL"), ct) : await db.PurchaseOrders.CountAsync(x => x.Status == "OPEN" || x.Status == "PARTIAL", ct),
                grnOutstanding = vendorId.HasValue ? await (from g in db.Grns join p in db.PurchaseOrders on g.PurchaseOrderId equals p.Id where p.VendorId == vendorId && (g.Status == "OPEN" || g.Status == "PARTIAL") select g).CountAsync(ct) : await db.Grns.CountAsync(x => x.Status == "OPEN" || x.Status == "PARTIAL", ct)
            };
            return Results.Ok(data);
        }).RequireAuthorization();
        return app;
    }
}
