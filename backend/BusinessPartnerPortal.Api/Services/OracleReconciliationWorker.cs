using BusinessPartnerPortal.Api.Data;
using BusinessPartnerPortal.Api.Domain;
using BusinessPartnerPortal.Api.Oracle;
using Microsoft.EntityFrameworkCore;

namespace BusinessPartnerPortal.Api.Services;

public sealed class OracleReconciliationWorker(
    IServiceProvider services,
    ILogger<OracleReconciliationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Reconcile(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Oracle reconciliation iteration failed.");
            }

            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }

    private async Task Reconcile(CancellationToken ct)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var oracle = scope.ServiceProvider.GetRequiredService<OracleService>();

        var mappings = await db.Vendors
            .Where(v => v.IsActive && v.OracleVendorId != null)
            .Select(v => new { v.Id, v.OracleVendorId })
            .ToListAsync(ct);

        foreach (var mapping in mappings)
        {
            if (!decimal.TryParse(mapping.OracleVendorId, out var oracleVendorId))
                continue;

            List<OracleInvoiceDto> oracleRows;
            try
            {
                oracleRows = await oracle.GetInvoicesAsync(oracleVendorId, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Unable to reconcile Oracle invoices for vendor {VendorId}.", mapping.Id);
                continue;
            }

            var portalInvoices = await db.Invoices
                .Where(x => x.VendorId == mapping.Id && x.DeletedAt == null && x.Status != "DRAFT")
                .ToListAsync(ct);

            foreach (var invoice in portalInvoices)
            {
                var oracleInvoice = oracleRows.FirstOrDefault(x =>
                    string.Equals(x.InvoiceNumber, invoice.InvoiceNumber, StringComparison.OrdinalIgnoreCase));

                if (oracleInvoice is null)
                    continue;

                var nextStatus = Map(oracleInvoice);
                if (nextStatus == invoice.Status)
                    continue;

                var oldStatus = invoice.Status;
                invoice.Status = nextStatus;
                invoice.IntegrationStatus = "SENT_TO_ORACLE";
                invoice.OracleInvoiceId = oracleInvoice.InvoiceNumber;
                invoice.UpdatedAt = DateTimeOffset.UtcNow;

                db.InvoiceStatusHistory.Add(new InvoiceStatusHistory
                {
                    InvoiceId = invoice.Id,
                    OldStatus = oldStatus,
                    NewStatus = nextStatus,
                    Source = "ORACLE_EBS",
                    ChangedAt = DateTimeOffset.UtcNow
                });

                var vendorUserIds = await db.VendorUsers
                    .Where(x => x.VendorId == mapping.Id && x.IsActive)
                    .Select(x => x.UserId)
                    .ToListAsync(ct);

                await db.SaveChangesAsync(ct);

                foreach (var userId in vendorUserIds)
                {
                    await db.Database.ExecuteSqlInterpolatedAsync($"""
                        INSERT INTO notification.notifications
                        (id,user_id,title,message,notification_type,entity_type,entity_id,is_read,created_at,updated_at)
                        VALUES
                        ({Guid.NewGuid()},{userId},{"Invoice status updated"},{$"Invoice {invoice.InvoiceNumber} is now {nextStatus}."},{"INVOICE_STATUS"},{"Invoice"},{invoice.Id},false,now(),now())
                        """, ct);
                }
            }
        }
    }

    private static string Map(OracleInvoiceDto invoice)
    {
        var payment = (invoice.PaymentStatus ?? string.Empty).ToUpperInvariant();
        var approval = (invoice.ApprovalStatus ?? string.Empty).ToUpperInvariant();

        if (payment.Contains("PAID")) return "PAID";
        if (approval.Contains("CANCEL")) return "CANCELLED";
        if (approval.Contains("RETURN") || approval.Contains("REVERT")) return "RETURNED";
        if (approval.Contains("APPROV") || approval.Contains("VALID")) return "ACCEPTED";
        if (approval.Contains("REJECT")) return "REJECTED";
        return "UNDER_FINANCE_REVIEW";
    }
}
