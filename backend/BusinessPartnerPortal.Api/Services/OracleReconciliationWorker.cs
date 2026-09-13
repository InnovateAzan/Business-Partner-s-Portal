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
                    string.Equals(
                        x.InvoiceNumber,
                        invoice.InvoiceNumber,
                        StringComparison.OrdinalIgnoreCase));

                if (oracleInvoice is null)
                    continue;

                var nextStatus = Map(oracleInvoice);
                var now = DateTimeOffset.UtcNow;

                // IMPORTANT:
                // APPS.PORTAL_INVOICES_V must return the real AP_INVOICES_ALL.INVOICE_ID.
                // Never store Oracle invoice number in invoice.oracle_invoice_id because
                // the attachment worker requires the numeric Oracle AP INVOICE_ID.
                var hasValidOracleInvoiceId =
                    long.TryParse(oracleInvoice.OracleInvoiceId, out var parsedOracleInvoiceId) &&
                    parsedOracleInvoiceId > 0;

                var oracleInvoiceIdChanged =
                    hasValidOracleInvoiceId &&
                    !string.Equals(
                        invoice.OracleInvoiceId,
                        parsedOracleInvoiceId.ToString(),
                        StringComparison.Ordinal);

                var statusChanged =
                    !string.Equals(
                        nextStatus,
                        invoice.Status,
                        StringComparison.OrdinalIgnoreCase);

                // Even when status is already synchronized, repair oracle_invoice_id
                // if an older version stored the invoice number there.
                if (!statusChanged && !oracleInvoiceIdChanged)
                    continue;

                var oldStatus = invoice.Status;

                if (statusChanged)
                {
                    invoice.Status = nextStatus;
                    invoice.IntegrationStatus = "SUCCESS";
                }

                if (oracleInvoiceIdChanged)
                {
                    invoice.OracleInvoiceId = parsedOracleInvoiceId.ToString();
                }

                invoice.UpdatedAt = now;

                if (statusChanged)
                {
                    db.InvoiceStatusHistory.Add(new InvoiceStatusHistory
                    {
                        InvoiceId = invoice.Id,
                        OldStatus = oldStatus,
                        NewStatus = nextStatus,
                        Source = "ORACLE_EBS",
                        ChangedAt = now
                    });
                }

                var vendorUserIds = statusChanged
                    ? await db.VendorUsers
                        .Where(x => x.VendorId == mapping.Id && x.IsActive)
                        .Select(x => x.UserId)
                        .ToListAsync(ct)
                    : new List<Guid>();

                await db.SaveChangesAsync(ct);

                if (oracleInvoiceIdChanged)
                {
                    logger.LogInformation(
                        "Repaired Oracle INVOICE_ID for portal invoice {PortalInvoiceId}. InvoiceNumber={InvoiceNumber}, OracleInvoiceId={OracleInvoiceId}",
                        invoice.Id,
                        invoice.InvoiceNumber,
                        parsedOracleInvoiceId);
                }

                if (statusChanged)
                {
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
    }

    private static string Map(OracleInvoiceDto invoice)
    {
        var payment = (invoice.PaymentStatus ?? string.Empty).ToUpperInvariant();
        var approval = (invoice.ApprovalStatus ?? string.Empty).ToUpperInvariant();

        // Cancellation must take priority over payment so an Oracle-cancelled
        // invoice never continues to appear as Paid in the portal.
        if (approval.Contains("CANCEL")) return "CANCELLED";
        if (approval.Contains("RETURN") || approval.Contains("REVERT")) return "RETURNED";
        if (approval.Contains("REJECT")) return "REJECTED";
        if (payment.Contains("PAID")) return "PAID";
        if (approval.Contains("APPROV") || approval.Contains("VALID")) return "APPROVED";
        return "PENDING";
    }
}
