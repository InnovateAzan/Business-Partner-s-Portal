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
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Oracle reconciliation iteration failed."
                );
            }

            try
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(30),
                    stoppingToken
                );
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task Reconcile(CancellationToken ct)
    {
        using var scope = services.CreateScope();

        var db =
            scope.ServiceProvider
                .GetRequiredService<AppDbContext>();

        var oracle =
            scope.ServiceProvider
                .GetRequiredService<OracleService>();

        var mappings =
            await db.Vendors
                .Where(
                    v =>
                        v.IsActive &&
                        v.OracleVendorId != null
                )
                .Select(
                    v => new
                    {
                        v.Id,
                        v.OracleVendorId
                    }
                )
                .ToListAsync(ct);

        foreach (var mapping in mappings)
        {
            if (
                !decimal.TryParse(
                    mapping.OracleVendorId,
                    out var oracleVendorId
                )
            )
            {
                continue;
            }

            List<OracleInvoiceDto> oracleRows;

            try
            {
                oracleRows =
                    await oracle.GetInvoicesAsync(
                        oracleVendorId,
                        ct
                    );
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Unable to reconcile Oracle invoices for vendor {VendorId}.",
                    mapping.Id
                );

                continue;
            }

            var portalInvoices =
                await db.Invoices
                    .Where(
                        x =>
                            x.VendorId == mapping.Id &&
                            x.DeletedAt == null &&
                            x.Status != "DRAFT"
                    )
                    .ToListAsync(ct);

            foreach (var invoice in portalInvoices)
            {
                /*
                 * Match by Oracle AP INVOICE_ID first.
                 *
                 * Invoice number is not guaranteed to be unique in the portal
                 * database, and this project already contains duplicate invoice
                 * numbers with different Oracle INVOICE_ID values.
                 *
                 * If the portal row already has oracle_invoice_id, never match it
                 * to a different Oracle invoice merely because the invoice number
                 * is the same.
                 *
                 * Invoice-number fallback is retained only for legacy rows where
                 * oracle_invoice_id has not yet been populated.
                 */
                OracleInvoiceDto? oracleInvoice = null;

                if (!string.IsNullOrWhiteSpace(invoice.OracleInvoiceId))
                {
                    oracleInvoice =
                        oracleRows.FirstOrDefault(
                            x =>
                                string.Equals(
                                    x.OracleInvoiceId?.Trim(),
                                    invoice.OracleInvoiceId.Trim(),
                                    StringComparison.OrdinalIgnoreCase
                                )
                        );
                }
                else
                {
                    oracleInvoice =
                        oracleRows.FirstOrDefault(
                            x =>
                                string.Equals(
                                    x.InvoiceNumber?.Trim(),
                                    invoice.InvoiceNumber?.Trim(),
                                    StringComparison.OrdinalIgnoreCase
                                )
                        );
                }

                if (oracleInvoice is null)
                {
                    logger.LogWarning(
                        "Oracle invoice match not found. PortalInvoiceId={PortalInvoiceId}, " +
                        "PortalInvoiceNumber={PortalInvoiceNumber}, PortalOracleInvoiceId={PortalOracleInvoiceId}",
                        invoice.Id,
                        invoice.InvoiceNumber,
                        invoice.OracleInvoiceId
                    );

                    continue;
                }

                var nextStatus =
                    Map(oracleInvoice);

                var now =
                    DateTimeOffset.UtcNow;

                // IMPORTANT:
                // APPS.PORTAL_INVOICES_V must return the real
                // AP_INVOICES_ALL.INVOICE_ID.
                //
                // Never store Oracle invoice number in
                // invoice.oracle_invoice_id because the attachment
                // worker requires the numeric Oracle AP INVOICE_ID.
                var hasValidOracleInvoiceId =
                    long.TryParse(
                        oracleInvoice.OracleInvoiceId,
                        out var parsedOracleInvoiceId
                    )
                    &&
                    parsedOracleInvoiceId > 0;

                var oracleInvoiceIdChanged =
                    hasValidOracleInvoiceId
                    &&
                    !string.Equals(
                        invoice.OracleInvoiceId,
                        parsedOracleInvoiceId.ToString(),
                        StringComparison.Ordinal
                    );

                var statusChanged =
                    !string.Equals(
                        nextStatus,
                        invoice.Status,
                        StringComparison.OrdinalIgnoreCase
                    );

                var oracleRemarks =
                    string.IsNullOrWhiteSpace(
                        oracleInvoice.Remarks
                    )
                        ? null
                        : oracleInvoice.Remarks.Trim();

                var remarksChanged =
                    !string.Equals(
                        invoice.Remarks,
                        oracleRemarks,
                        StringComparison.Ordinal
                    );

                // Even when status is already synchronized,
                // repair oracle_invoice_id if an older version
                // stored the invoice number there.
                if (
                    !statusChanged &&
                    !oracleInvoiceIdChanged &&
                    !remarksChanged
                )
                {
                    continue;
                }

                var oldStatus =
                    invoice.Status;

                if (statusChanged)
                {
                    invoice.Status =
                        nextStatus;

                    invoice.IntegrationStatus =
                        "SUCCESS";
                }

                if (oracleInvoiceIdChanged)
                {
                    invoice.OracleInvoiceId =
                        parsedOracleInvoiceId.ToString();
                }

                if (remarksChanged)
                {
                    invoice.Remarks =
                        oracleRemarks;
                }

                invoice.UpdatedAt =
                    now;

                if (statusChanged)
                {
                    db.InvoiceStatusHistory.Add(
                        new InvoiceStatusHistory
                        {
                            InvoiceId = invoice.Id,
                            OldStatus = oldStatus,
                            NewStatus = nextStatus,
                            Source = "ORACLE_EBS",
                            ChangedAt = now
                        }
                    );
                }

                var vendorUserIds =
                    statusChanged
                        ? await db.VendorUsers
                            .Where(
                                x =>
                                    x.VendorId == mapping.Id &&
                                    x.IsActive
                            )
                            .Select(
                                x => x.UserId
                            )
                            .ToListAsync(ct)
                        : new List<Guid>();

                await db.SaveChangesAsync(ct);

                if (oracleInvoiceIdChanged)
                {
                    logger.LogInformation(
                        "Repaired Oracle INVOICE_ID for portal invoice {PortalInvoiceId}. " +
                        "InvoiceNumber={InvoiceNumber}, OracleInvoiceId={OracleInvoiceId}",
                        invoice.Id,
                        invoice.InvoiceNumber,
                        parsedOracleInvoiceId
                    );
                }

                if (statusChanged)
                {
                    foreach (var userId in vendorUserIds)
                    {
                        await db.Database.ExecuteSqlInterpolatedAsync(
                            $"""
                            INSERT INTO notification.notifications
                            (
                                id,
                                user_id,
                                title,
                                message,
                                notification_type,
                                entity_type,
                                entity_id,
                                is_read,
                                created_at,
                                updated_at
                            )
                            VALUES
                            (
                                {Guid.NewGuid()},
                                {userId},
                                {"Invoice status updated"},
                                {$"Invoice {invoice.InvoiceNumber} is now {nextStatus}."},
                                {"INVOICE_STATUS"},
                                {"Invoice"},
                                {invoice.Id},
                                false,
                                now(),
                                now()
                            )
                            """,
                            ct
                        );
                    }
                }
            }
        }
    }

    private static string Map(OracleInvoiceDto invoice)
    {
        var approval =
            (invoice.ApprovalStatus ?? string.Empty)
                .Trim()
                .ToUpperInvariant();

        /*
         * IMPORTANT:
         *
         * Oracle Approval Status has priority over Payment Status.
         *
         * Oracle DFF Approval Status values:
         *
         * Pending
         * Approved
         * Rejected
         * Cancelled
         * Paid
         *
         * Do not evaluate PAYMENT_STATUS before these values.
         * Otherwise an invoice whose Approval Status is Approved
         * could incorrectly appear as Paid in the portal.
         */

        if (
            approval.Contains("CANCEL")
        )
        {
            return "CANCELLED";
        }

        if (
            approval.Contains("RETURN") ||
            approval.Contains("REVERT")
        )
        {
            return "RETURNED";
        }

        if (
            approval.Contains("REJECT")
        )
        {
            return "REJECTED";
        }

        if (
            approval.Contains("PEND")
        )
        {
            return "PENDING";
        }

        /*
         * Store Approved as ACCEPTED because ACCEPTED is already
         * allowed by chk_invoice_status and the frontend displays
         * both APPROVED and ACCEPTED as "Approved".
         */
        if (
            approval.Contains("APPROV") ||
            approval.Contains("VALID")
        )
        {
            return "ACCEPTED";
        }

        /*
         * Oracle Approval Status itself can explicitly be Paid.
         */
        if (
            approval.Contains("PAID")
        )
        {
            return "PAID";
        }

        /*
         * IMPORTANT:
         * Do NOT use PAYMENT_STATUS as a fallback for portal workflow status.
         *
         * AP_INVOICES_ALL.ATTRIBUTE11 is the authoritative portal status.
         * Oracle can report PAYMENT_STATUS = PAID independently of the
         * Business Partner Portal approval workflow. Using payment as a
         * fallback can therefore overwrite Pending / Cancelled / Rejected
         * with PAID when the DFF value is temporarily blank/unavailable.
         *
         * A portal invoice becomes PAID only when ATTRIBUTE11 itself says Paid.
         */
        return "PENDING";
    }
}