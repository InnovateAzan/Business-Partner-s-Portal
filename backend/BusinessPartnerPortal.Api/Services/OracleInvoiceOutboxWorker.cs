using BusinessPartnerPortal.Api.Data;
using BusinessPartnerPortal.Api.Oracle;

using Microsoft.EntityFrameworkCore;

namespace BusinessPartnerPortal.Api.Services;

public sealed class OracleInvoiceOutboxWorker(
    IServiceProvider services,
    ILogger<OracleInvoiceOutboxWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        while (
            !stoppingToken
                .IsCancellationRequested
        )
        {
            try
            {
                await ProcessBatchAsync(
                    stoppingToken
                );
            }
            catch (
                OperationCanceledException
            )
            when (
                stoppingToken
                    .IsCancellationRequested
            )
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Oracle AP outbox worker iteration failed."
                );
            }

            await Task.Delay(
                TimeSpan.FromSeconds(
                    5
                ),
                stoppingToken
            );
        }
    }

    // ============================================================
    // PROCESS OUTBOX BATCH
    // ============================================================

    private async Task ProcessBatchAsync(
        CancellationToken ct)
    {
        using var scope =
            services.CreateScope();

        var db =
            scope.ServiceProvider
                .GetRequiredService<
                    AppDbContext
                >();

        var oracleRead =
            scope.ServiceProvider
                .GetRequiredService<
                    OracleService
                >();

        var oracleOptions =
            scope.ServiceProvider
                .GetRequiredService<
                    OracleOptions
                >();

        var configuration =
            scope.ServiceProvider
                .GetRequiredService<
                    IConfiguration
                >();

        var loggerFactory =
            scope.ServiceProvider
                .GetRequiredService<
                    ILoggerFactory
                >();

        var oracleAp =
            new OracleApInvoiceService(
                oracleOptions,
                oracleRead,
                configuration,
                loggerFactory
                    .CreateLogger<
                        OracleApInvoiceService
                    >()
            );

        var items =
            await ClaimAsync(
                db,
                ct
            );

        foreach (
            var item in items
        )
        {
            try
            {
                var snapshot =
                    await LoadInvoiceSnapshotAsync(
                        db,
                        item.InvoiceId,
                        ct
                    );

                var result =
                    await oracleAp
                        .ProcessInvoiceAsync(
                            snapshot,
                            ct
                        );

                await MarkSuccessAsync(
                    db,
                    item,
                    result,
                    ct
                );
            }
            catch (
                OracleApRejectedException ex
            )
            {
                await MarkPermanentFailureAsync(
                    db,
                    item,
                    ex.Message,
                    "ORACLE_REJECTED",
                    ct
                );
            }
            catch (
                OracleApBusinessException ex
            )
            {
                await MarkPermanentFailureAsync(
                    db,
                    item,
                    ex.Message,
                    "INTEGRATION_FAILED",
                    ct
                );
            }
            catch (Exception ex)
            {
                await MarkRetryableFailureAsync(
                    db,
                    item,
                    Sanitize(
                        ex.Message
                    ),
                    ct
                );
            }
        }
    }

    // ============================================================
    // CLAIM OUTBOX ROWS
    // ============================================================

    private static async Task<List<OutboxItem>>
        ClaimAsync(
            AppDbContext db,
            CancellationToken ct)
    {
        var items =
            new List<OutboxItem>();

        var connection =
            db.Database
                .GetDbConnection();

        if (
            connection.State !=
            System.Data.ConnectionState.Open
        )
        {
            await connection.OpenAsync(
                ct
            );
        }

        await using var transaction =
            await connection
                .BeginTransactionAsync(
                    ct
                );

        await using var command =
            connection.CreateCommand();

        command.Transaction =
            transaction;

        command.CommandText =
            """
            SELECT
                id,
                aggregate_id,
                attempt_count,
                correlation_id

            FROM
                integration.outbox_messages

            WHERE
                status IN
                (
                    'PENDING',
                    'RETRYING'
                )

            AND
                event_type IN
                (
                    'InvoiceSubmitted',
                    'InvoiceResubmitted'
                )

            AND
                (
                    next_attempt_at IS NULL
                    OR
                    next_attempt_at <= now()
                )

            ORDER BY
                created_at

            FOR UPDATE
            SKIP LOCKED

            LIMIT 5
            """;

        await using var reader =
            await command
                .ExecuteReaderAsync(
                    ct
                );

        while (
            await reader.ReadAsync(
                ct
            )
        )
        {
            items.Add(
                new OutboxItem(
                    reader.GetGuid(0),
                    reader.GetGuid(1),
                    reader.GetInt32(2),
                    reader.GetGuid(3)
                )
            );
        }

        await reader.DisposeAsync();

        foreach (
            var item in items
        )
        {
            await using var update =
                connection.CreateCommand();

            update.Transaction =
                transaction;

            update.CommandText =
                """
                UPDATE
                    integration.outbox_messages

                SET
                    status = 'PROCESSING',
                    attempt_count = attempt_count + 1,
                    last_error = NULL

                WHERE
                    id = @id
                """;

            var parameter =
                update.CreateParameter();

            parameter.ParameterName =
                "id";

            parameter.Value =
                item.Id;

            update.Parameters.Add(
                parameter
            );

            await update.ExecuteNonQueryAsync(
                ct
            );
        }

        await transaction.CommitAsync(
            ct
        );

        return items;
    }

    // ============================================================
    // LOAD PORTAL INVOICE
    // ============================================================

    private static async Task<OracleApPortalInvoice>
        LoadInvoiceSnapshotAsync(
            AppDbContext db,
            Guid invoiceId,
            CancellationToken ct)
    {
        var row =
            await (
                from invoice
                    in db.Invoices

                join vendor
                    in db.Vendors

                    on
                        invoice.VendorId
                    equals
                        vendor.Id

                where
                    invoice.Id ==
                    invoiceId

                &&
                    invoice.DeletedAt ==
                    null

                select new
                {
                    invoice.Id,

                    // NEW: PostgreSQL auto-increment PK_ID
                    invoice.PkId,

                    invoice.InvoiceNumber,
                    invoice.InvoiceDate,
                    invoice.InvoiceAmount,
                    invoice.CurrencyCode,
                    invoice.PoNumber,
                    invoice.GrnNumbers,
                    invoice.InvoiceType,
                    invoice.Remarks,
                    vendor.OracleVendorId
                }
            )
            .SingleOrDefaultAsync(
                ct
            )
            ??
            throw new OracleApBusinessException(
                $"Portal invoice {invoiceId} was not found."
            );

        // ========================================================
        // VALIDATE PK_ID
        // ========================================================

        if (
            row.PkId <=
            0
        )
        {
            throw new OracleApBusinessException(
                $"Portal invoice {invoiceId} does not contain a valid PK_ID."
            );
        }

        // ========================================================
        // VALIDATE ORACLE VENDOR
        // ========================================================

        if (
            !decimal.TryParse(
                row.OracleVendorId,
                out var oracleVendorId
            )
        )
        {
            throw new OracleApBusinessException(
                "Oracle vendor mapping is missing or invalid."
            );
        }

        // ========================================================
        // VALIDATE INVOICE DATE
        // ========================================================

        if (
            row.InvoiceDate
            is null
        )
        {
            throw new OracleApBusinessException(
                "Invoice date is required before Oracle submission."
            );
        }

        // ========================================================
        // VALIDATE PO
        // ========================================================

        if (
            string.IsNullOrWhiteSpace(
                row.PoNumber
            )
        )
        {
            throw new OracleApBusinessException(
                "PO number is required before Oracle submission."
            );
        }

        // ========================================================
        // GRN LIST
        // ========================================================

        var grns =
            (
                row.GrnNumbers
                ??
                string.Empty
            )
            .Split(
                ',',
                StringSplitOptions.RemoveEmptyEntries
                |
                StringSplitOptions.TrimEntries
            )
            .Distinct(
                StringComparer.OrdinalIgnoreCase
            )
            .ToList();

        // ========================================================
        // RECEIPT TRANSACTION IDS
        // ========================================================

        var receiptTransactionIds =
            await db
                .InvoiceLineGrnAllocations
                .AsNoTracking()
                .Where(
                    x =>
                        x.InvoiceId ==
                        row.Id
                )
                .Select(
                    x =>
                        x.RcvTransactionId
                )
                .Distinct()
                .ToListAsync(
                    ct
                );

        // ========================================================
        // BUILD ORACLE AP DTO
        // ========================================================

        return new OracleApPortalInvoice(
            row.Id,

            // NEW: send portal PK_ID to Oracle DTO
            row.PkId,

            oracleVendorId,
            row.PoNumber,
            grns,
            receiptTransactionIds,
            row.InvoiceNumber,
            row.InvoiceDate.Value,
            row.InvoiceAmount,
            row.CurrencyCode,
            row.InvoiceType,
            row.Remarks
        );
    }

    // ============================================================
    // SUCCESS
    // ============================================================

    private async Task MarkSuccessAsync(
        AppDbContext db,
        OutboxItem item,
        OracleApProcessResult result,
        CancellationToken ct)
    {
        var now =
            DateTimeOffset.UtcNow;

        await using var transaction =
            await db.Database
                .BeginTransactionAsync(
                    ct
                );

        await db.Database
            .ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE
                    integration.outbox_messages

                SET
                    status = 'PROCESSED',
                    processed_at = {now},
                    next_attempt_at = NULL,
                    last_error = NULL,
                    external_reference = {result.OracleInvoiceId.ToString()},
                    oracle_request_id = {result.ConcurrentRequestId},
                    oracle_group_id = NULL

                WHERE
                    id = {item.Id}
                """,
                ct
            );

        var invoice =
            await db.Invoices
                .SingleAsync(
                    x =>
                        x.Id ==
                        item.InvoiceId,
                    ct
                );

        var oldStatus =
            invoice.Status;

        invoice.IntegrationStatus =
            "SUCCESS";

        invoice.OracleInvoiceId =
            result
                .OracleInvoiceId
                .ToString();

        invoice.Status =
            "SENT_TO_ORACLE";

        invoice.UpdatedAt =
            now;

        if (
            !string.Equals(
                oldStatus,
                invoice.Status,
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            db.InvoiceStatusHistory.Add(
                new Domain.InvoiceStatusHistory
                {
                    InvoiceId =
                        invoice.Id,

                    OldStatus =
                        oldStatus,

                    NewStatus =
                        invoice.Status,

                    Source =
                        "ORACLE_EBS",

                    ChangedAt =
                        now,

                    CorrelationId =
                        item.CorrelationId
                }
            );
        }

        // Queue document synchronization only after Oracle has returned
        // the real AP INVOICE_ID and it has been stored on the portal invoice.
        // The attachment worker maps:
        // Portal Invoice UUID -> invoice.oracle_invoice_id -> portal documents.
        var attachmentOutboxId = Guid.NewGuid();
        var attachmentCorrelationId = Guid.NewGuid();
        var attachmentPayload =
            System.Text.Json.JsonSerializer.Serialize(
                new
                {
                    portalInvoiceId = invoice.Id,
                    oracleInvoiceId = result.OracleInvoiceId
                }
            );

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO integration.outbox_messages
            (
                id,
                event_type,
                aggregate_type,
                aggregate_id,
                payload,
                status,
                attempt_count,
                correlation_id,
                created_at
            )
            VALUES
            (
                {attachmentOutboxId},
                {"InvoiceAttachmentsReady"},
                {"Invoice"},
                {invoice.Id},
                {attachmentPayload}::jsonb,
                {"PENDING"},
                0,
                {attachmentCorrelationId},
                {now}
            )
            """,
            ct
        );

        await db.SaveChangesAsync(
            ct
        );

        await transaction.CommitAsync(
            ct
        );

        logger.LogInformation(
            "Portal invoice {InvoiceId} imported to Oracle AP. " +
            "OracleInvoiceId={OracleInvoiceId}, " +
            "RequestId={RequestId}, " +
            "GroupId=NULL, " +
            "BatchName={BatchName}",
            item.InvoiceId,
            result.OracleInvoiceId,
            result.ConcurrentRequestId,
            result.BatchName
        );
    }

    // ============================================================
    // PERMANENT FAILURE
    // ============================================================

    private async Task MarkPermanentFailureAsync(
        AppDbContext db,
        OutboxItem item,
        string error,
        string status,
        CancellationToken ct)
    {
        error =
            Sanitize(
                error
            );

        var now =
            DateTimeOffset.UtcNow;

        await using var transaction =
            await db.Database
                .BeginTransactionAsync(
                    ct
                );

        await db.Database
            .ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE
                    integration.outbox_messages

                SET
                    status = 'FAILED',
                    next_attempt_at = NULL,
                    last_error = {error}

                WHERE
                    id = {item.Id}
                """,
                ct
            );

        var invoice =
            await db.Invoices
                .SingleOrDefaultAsync(
                    x =>
                        x.Id ==
                        item.InvoiceId,
                    ct
                );

        if (
            invoice
            is not null
        )
        {
            var oldStatus =
                invoice.Status;

            invoice.IntegrationStatus =
                "FAILED";

            invoice.Status =
                status;

            invoice.UpdatedAt =
                now;

            if (
                !string.Equals(
                    oldStatus,
                    status,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                db.InvoiceStatusHistory.Add(
                    new Domain.InvoiceStatusHistory
                    {
                        InvoiceId =
                            invoice.Id,

                        OldStatus =
                            oldStatus,

                        NewStatus =
                            status,

                        Remarks =
                            error,

                        Source =
                            "ORACLE_EBS",

                        ChangedAt =
                            now,

                        CorrelationId =
                            item.CorrelationId
                    }
                );
            }

            await db.SaveChangesAsync(
                ct
            );
        }

        await transaction.CommitAsync(
            ct
        );

        logger.LogError(
            "Oracle AP integration permanently failed " +
            "for invoice {InvoiceId}: {Error}",
            item.InvoiceId,
            error
        );
    }

    // ============================================================
    // RETRYABLE FAILURE
    // ============================================================

    private async Task MarkRetryableFailureAsync(
        AppDbContext db,
        OutboxItem item,
        string error,
        CancellationToken ct)
    {
        var attemptNumber =
            item.Attempts + 1;

        const int maxAttempts =
            6;

        if (
            attemptNumber >=
            maxAttempts
        )
        {
            await MarkPermanentFailureAsync(
                db,
                item,
                $"Retry limit reached. {error}",
                "INTEGRATION_FAILED",
                ct
            );

            return;
        }

        var delayMinutes =
            Math.Min(
                30,
                Math.Pow(
                    2,
                    Math.Min(
                        attemptNumber,
                        4
                    )
                )
            );

        var next =
            DateTimeOffset.UtcNow
                .AddMinutes(
                    delayMinutes
                );

        await db.Database
            .ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE
                    integration.outbox_messages

                SET
                    status = 'RETRYING',
                    next_attempt_at = {next},
                    last_error = {error}

                WHERE
                    id = {item.Id}
                """,
                ct
            );

        await db.Database
            .ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE
                    invoice.invoices

                SET
                    integration_status = 'RETRYING',
                    updated_at = now()

                WHERE
                    id = {item.InvoiceId}
                """,
                ct
            );

        logger.LogWarning(
            "Oracle AP integration will retry invoice {InvoiceId}. " +
            "Attempt={Attempt}, Next={Next}, Error={Error}",
            item.InvoiceId,
            attemptNumber,
            next,
            error
        );
    }

    // ============================================================
    // SANITIZE ERROR FOR PORTAL / DB
    // ============================================================

    private static string Sanitize(
        string? value)
    {
        if (
            string.IsNullOrWhiteSpace(
                value
            )
        )
        {
            return
                "Unknown Oracle integration error.";
        }

        var cleaned =
            value
                .Replace(
                    '\r',
                    ' '
                )
                .Replace(
                    '\n',
                    ' '
                )
                .Trim();

        return
            cleaned.Length <=
            1000
                ?
                cleaned
                :
                cleaned[..1000];
    }

    private sealed record OutboxItem(
        Guid Id,
        Guid InvoiceId,
        int Attempts,
        Guid CorrelationId
    );
}