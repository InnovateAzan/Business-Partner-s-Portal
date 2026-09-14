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
        logger.LogInformation(
            "Oracle AP outbox worker started."
        );

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

            try
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(
                        5
                    ),
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
        }

        logger.LogInformation(
            "Oracle AP outbox worker stopped."
        );
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

        if (
            items.Count ==
            0
        )
        {
            return;
        }

        foreach (
            var item in items
        )
        {
            try
            {
                logger.LogInformation(
                    "Starting Oracle AP outbox processing. " +
                    "PortalInvoiceId={PortalInvoiceId}, " +
                    "OutboxEventId={OutboxEventId}, " +
                    "OriginalStatus={OriginalStatus}, " +
                    "Attempts={Attempts}, " +
                    "WasStaleProcessing={WasStaleProcessing}",
                    item.InvoiceId,
                    item.Id,
                    item.OriginalStatus,
                    item.Attempts,
                    item.WasStaleProcessing
                );

                var snapshot =
                    await LoadInvoiceSnapshotAsync(
                        db,
                        item.InvoiceId,
                        ct
                    );

                // ====================================================
                // STALE PROCESSING RECOVERY
                // ====================================================

                if (
                    item.WasStaleProcessing
                )
                {
                    logger.LogInformation(
                        "Checking Oracle for stale PROCESSING invoice. " +
                        "InvoiceNumber={InvoiceNumber}, " +
                        "VendorId={VendorId}, " +
                        "PortalInvoiceId={PortalInvoiceId}, " +
                        "OutboxEventId={OutboxEventId}",
                        snapshot.InvoiceNumber,
                        snapshot.VendorId,
                        item.InvoiceId,
                        item.Id
                    );

                    var outcome =
                        await oracleAp
                            .GetInterfaceOutcomeAsync(
                                snapshot.VendorId,
                                snapshot.InvoiceNumber,
                                ct
                            );

                    logger.LogInformation(
                        "Oracle stale PROCESSING check completed. " +
                        "InvoiceNumber={InvoiceNumber}, " +
                        "PortalInvoiceId={PortalInvoiceId}, " +
                        "OutboxEventId={OutboxEventId}, " +
                        "ImportedInvoiceId={ImportedInvoiceId}, " +
                        "InterfaceInvoiceId={InterfaceInvoiceId}, " +
                        "InterfaceStatus={InterfaceStatus}, " +
                        "RejectionCount={RejectionCount}",
                        snapshot.InvoiceNumber,
                        item.InvoiceId,
                        item.Id,
                        outcome.ImportedInvoiceId,
                        outcome.InterfaceInvoiceId,
                        outcome.InterfaceStatus,
                        outcome.Rejections.Count
                    );

                    // =================================================
                    // ALREADY IMPORTED TO AP
                    // =================================================

                    if (
                        outcome.ImportedInvoiceId
                            .HasValue
                    )
                    {
                        logger.LogInformation(
                            "Recovered stale invoice as already imported. " +
                            "InvoiceNumber={InvoiceNumber}, " +
                            "PortalInvoiceId={PortalInvoiceId}, " +
                            "OracleInvoiceId={OracleInvoiceId}",
                            snapshot.InvoiceNumber,
                            item.InvoiceId,
                            outcome.ImportedInvoiceId.Value
                        );

                        await MarkSuccessAsync(
                            db,
                            item,
                            new OracleApProcessResult(
                                outcome.ImportedInvoiceId.Value,
                                item.OracleRequestId,
                                "RECOVERED",
                                "RECOVERED",
                                Array.Empty<string>()
                            ),
                            ct
                        );

                        continue;
                    }

                    // =================================================
                    // STILL EXISTS IN ORACLE INTERFACE
                    // =================================================

                    if (
                        outcome.InterfaceInvoiceId
                            .HasValue
                    )
                    {
                        if (
                            string.Equals(
                                outcome.InterfaceStatus,
                                "REJECTED",
                                StringComparison.OrdinalIgnoreCase
                            )
                            ||
                            outcome.Rejections.Count >
                            0
                        )
                        {
                            var rejectionMessage =
                                outcome.Rejections.Count >
                                0
                                    ?
                                    string.Join(
                                        "; ",
                                        outcome.Rejections
                                    )
                                    :
                                    "Oracle AP interface status is REJECTED.";

                            await MarkPermanentFailureAsync(
                                db,
                                item,
                                rejectionMessage,
                                "ORACLE_REJECTED",
                                ct,
                                item.OracleRequestId
                            );

                            continue;
                        }

                        await MarkMonitoringAsync(
                            db,
                            item,
                            outcome.InterfaceInvoiceId.Value,
                            outcome.InterfaceStatus,
                            ct
                        );

                        continue;
                    }

                    // =================================================
                    // NOTHING FOUND IN ORACLE
                    //
                    // Safe to return to PENDING. The worker will submit
                    // the invoice again in a later iteration.
                    // =================================================

                    logger.LogWarning(
                        "No Oracle AP invoice or interface record found for stale PROCESSING event. " +
                        "InvoiceNumber={InvoiceNumber}, " +
                        "PortalInvoiceId={PortalInvoiceId}, " +
                        "OutboxEventId={OutboxEventId}. " +
                        "Resetting event to PENDING for safe retry.",
                        snapshot.InvoiceNumber,
                        item.InvoiceId,
                        item.Id
                    );

                    await ResetForSafeRetryAsync(
                        db,
                        item,
                        ct
                    );

                    continue;
                }

                // ====================================================
                // NEW / RETRYABLE ORACLE SUBMISSION
                // ====================================================

                logger.LogInformation(
                    "Calling Oracle AP invoice processing. " +
                    "InvoiceNumber={InvoiceNumber}, " +
                    "PortalInvoiceId={PortalInvoiceId}, " +
                    "OutboxEventId={OutboxEventId}, " +
                    "Attempt={Attempt}",
                    snapshot.InvoiceNumber,
                    item.InvoiceId,
                    item.Id,
                    item.Attempts + 1
                );

                var result =
                    await oracleAp
                        .ProcessInvoiceAsync(
                            snapshot,
                            ct
                        );

                logger.LogInformation(
                    "Oracle AP invoice processing returned successfully. " +
                    "InvoiceNumber={InvoiceNumber}, " +
                    "PortalInvoiceId={PortalInvoiceId}, " +
                    "OutboxEventId={OutboxEventId}, " +
                    "OracleInvoiceId={OracleInvoiceId}, " +
                    "OracleRequestId={OracleRequestId}",
                    snapshot.InvoiceNumber,
                    item.InvoiceId,
                    item.Id,
                    result.OracleInvoiceId,
                    result.ConcurrentRequestId
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
                logger.LogError(
                    ex,
                    "Oracle AP rejected invoice. " +
                    "PortalInvoiceId={PortalInvoiceId}, " +
                    "OutboxEventId={OutboxEventId}, " +
                    "OracleRequestId={OracleRequestId}",
                    item.InvoiceId,
                    item.Id,
                    ex.ConcurrentRequestId
                );

                await MarkPermanentFailureAsync(
                    db,
                    item,
                    ex.Message,
                    "ORACLE_REJECTED",
                    ct,
                    ex.ConcurrentRequestId
                );
            }
            catch (
                OracleApPendingException ex
            )
            {
                logger.LogInformation(
                    "Oracle AP invoice remains pending. " +
                    "PortalInvoiceId={PortalInvoiceId}, " +
                    "OutboxEventId={OutboxEventId}, " +
                    "InterfaceInvoiceId={InterfaceInvoiceId}, " +
                    "InterfaceStatus={InterfaceStatus}",
                    item.InvoiceId,
                    item.Id,
                    ex.InterfaceInvoiceId,
                    ex.InterfaceStatus
                );

                await MarkMonitoringAsync(
                    db,
                    item,
                    ex.InterfaceInvoiceId,
                    ex.InterfaceStatus,
                    ct
                );
            }
            catch (
                OracleApBusinessException ex
            )
            {
                logger.LogError(
                    ex,
                    "Oracle AP business validation failed. " +
                    "PortalInvoiceId={PortalInvoiceId}, " +
                    "OutboxEventId={OutboxEventId}",
                    item.InvoiceId,
                    item.Id
                );

                await MarkPermanentFailureAsync(
                    db,
                    item,
                    ex.Message,
                    "INTEGRATION_FAILED",
                    ct
                );
            }
            catch (
                OperationCanceledException
            )
            when (
                ct.IsCancellationRequested
            )
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Unexpected Oracle AP processing error. " +
                    "PortalInvoiceId={PortalInvoiceId}, " +
                    "OutboxEventId={OutboxEventId}, " +
                    "OriginalStatus={OriginalStatus}",
                    item.InvoiceId,
                    item.Id,
                    item.OriginalStatus
                );

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
    // CLAIM OUTBOX ROW
    //
    // IMPORTANT:
    // Only ONE invoice is claimed at a time.
    //
    // Previously LIMIT 5 caused multiple invoices to immediately
    // appear as PROCESSING even though only the first invoice was
    // actually being processed sequentially.
    //
    // Also:
    // attempt_count is incremented only when claiming a PENDING or
    // RETRYING item.
    //
    // A stale PROCESSING polling/reconciliation check is NOT a new
    // Oracle submission attempt, therefore it must not increase
    // attempt_count.
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
                correlation_id,
                status,
                oracle_request_id

            FROM
                integration.outbox_messages

            WHERE
                (
                    status IN
                    (
                        'PENDING',
                        'RETRYING'
                    )

                    OR

                    (
                        status = 'PROCESSING'

                        AND
                        (
                            next_attempt_at <= now()

                            OR

                            (
                                next_attempt_at IS NULL

                                AND
                                created_at <=
                                    now() - interval '15 minutes'
                            )
                        )
                    )
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

            LIMIT 1
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
                    reader.GetGuid(
                        0
                    ),
                    reader.GetGuid(
                        1
                    ),
                    reader.GetInt32(
                        2
                    ),
                    reader.GetGuid(
                        3
                    ),
                    reader.GetString(
                        4
                    ),
                    reader.IsDBNull(
                        5
                    )
                        ?
                        null
                        :
                        reader.GetInt64(
                            5
                        )
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

            // --------------------------------------------------------
            // Only increment attempt_count when an actual PENDING /
            // RETRYING invoice is being claimed for submission.
            //
            // A PROCESSING record is only being reconciled/polled and
            // therefore should keep the existing attempt count.
            // --------------------------------------------------------

            update.CommandText =
                """
                UPDATE
                    integration.outbox_messages

                SET
                    status = 'PROCESSING',

                    attempt_count =
                        CASE
                            WHEN status IN
                            (
                                'PENDING',
                                'RETRYING'
                            )
                            THEN attempt_count + 1

                            ELSE attempt_count
                        END,

                    next_attempt_at =
                        now() + interval '15 minutes',

                    last_error =
                        NULL

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
    // STALE PROCESSING RECOVERY / MONITORING
    // ============================================================

    private async Task MarkMonitoringAsync(
        AppDbContext db,
        OutboxItem item,
        long interfaceInvoiceId,
        string? interfaceStatus,
        CancellationToken ct)
    {
        var next =
            DateTimeOffset.UtcNow
                .AddMinutes(
                    5
                );

        await db.Database
            .ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE
                    integration.outbox_messages

                SET
                    status = {"PROCESSING"},
                    next_attempt_at = {next},
                    last_error = NULL,
                    external_reference = {interfaceInvoiceId.ToString()}

                WHERE
                    id = {item.Id}
                """,
                ct
            );

        // The portal must reflect an interface row that is being monitored;
        // otherwise a stale recovery can leave the user-facing state queued.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE invoice.invoices
            SET integration_status = {"PROCESSING"}, updated_at = now()
            WHERE id = {item.InvoiceId}
            """, ct);

        logger.LogInformation(
            "Oracle invoice remains under interface processing. " +
            "PortalInvoiceId={PortalInvoiceId}, " +
            "OutboxEventId={OutboxEventId}, " +
            "CurrentOutboxStatus={CurrentOutboxStatus}, " +
            "OracleInterfaceInvoiceId={OracleInterfaceInvoiceId}, " +
            "OracleRequestId={OracleRequestId}, " +
            "OracleInterfaceStatus={OracleInterfaceStatus}, " +
            "NextCheckAt={NextCheckAt}, " +
            "FinalPortalStatus={FinalPortalStatus}",
            item.InvoiceId,
            item.Id,
            "PROCESSING",
            interfaceInvoiceId,
            item.OracleRequestId,
            interfaceStatus ?? "<null>",
            next,
            "PROCESSING"
        );
    }

    // ============================================================
    // SAFE RETRY
    // ============================================================

    private async Task<int> ResetForSafeRetryAsync(
        AppDbContext db,
        OutboxItem item,
        CancellationToken ct)
    {
        var affected =
            await db.Database
                .ExecuteSqlInterpolatedAsync(
                    $"""
                    UPDATE
                        integration.outbox_messages

                    SET
                        status = {"PENDING"},
                        next_attempt_at = NULL,
                        last_error = NULL

                    WHERE
                        id = {item.Id}

                    AND
                        status = {"PROCESSING"}
                    """,
                    ct
                );

        logger.LogWarning(
            "Stale PROCESSING outbox event reset to PENDING. " +
            "PortalInvoiceId={PortalInvoiceId}, " +
            "OutboxEventId={OutboxEventId}, " +
            "AffectedRows={AffectedRows}, " +
            "AttemptCountPreserved={AttemptCount}",
            item.InvoiceId,
            item.Id,
            affected,
            item.Attempts
        );

        return affected;
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

                    // PostgreSQL auto-increment PK_ID
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

            // Send portal PK_ID to Oracle DTO
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

        // ========================================================
        // QUEUE DOCUMENT / ATTACHMENT SYNCHRONIZATION
        // ========================================================

        var attachmentOutboxId =
            Guid.NewGuid();

        var attachmentCorrelationId =
            Guid.NewGuid();

        var attachmentPayload =
            System.Text.Json.JsonSerializer.Serialize(
                new
                {
                    portalInvoiceId =
                        invoice.Id,

                    oracleInvoiceId =
                        result.OracleInvoiceId
                }
            );

        // Crash recovery may call MarkSuccessAsync after Oracle committed but
        // before PostgreSQL did. Keep attachment synchronization idempotent.
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

                SELECT
                    {attachmentOutboxId},
                    {"InvoiceAttachmentsReady"},
                    {"Invoice"},
                    {invoice.Id},
                    {attachmentPayload}::jsonb,
                    {"PENDING"},
                    0,
                    {attachmentCorrelationId},
                    {now}
                WHERE NOT EXISTS
                (
                    SELECT 1
                    FROM integration.outbox_messages existing
                    WHERE existing.aggregate_id = {invoice.Id}
                    AND existing.event_type = {"InvoiceAttachmentsReady"}
                    AND existing.status <> {"FAILED"}
                )
                """, ct);

        await db.SaveChangesAsync(
            ct
        );

        await transaction.CommitAsync(
            ct
        );

        logger.LogInformation(
            "Portal invoice imported to Oracle AP successfully. " +
            "PortalInvoiceId={InvoiceId}, " +
            "OutboxEventId={OutboxEventId}, " +
            "OracleInvoiceId={OracleInvoiceId}, " +
            "RequestId={RequestId}, " +
            "GroupId=NULL, " +
            "BatchName={BatchName}",
            item.InvoiceId,
            item.Id,
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
        CancellationToken ct,
        long? oracleRequestId = null)
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
                    processed_at = {now},
                    next_attempt_at = NULL,
                    last_error = {error},
                    oracle_request_id =
                        COALESCE(
                            {oracleRequestId},
                            oracle_request_id
                        )

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

            // Keep the existing integration_status value here.
            // The production database check constraint does not currently
            // allow INTEGRATION_FAILED in invoice.invoices.integration_status.
            // The permanent failure is represented by invoice.Status below
            // and by the FAILED outbox row / last_error. This avoids rolling
            // back the failure transaction because of the DB constraint.
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
            "Oracle AP integration permanently failed. " +
            "PortalInvoiceId={InvoiceId}, " +
            "OutboxEventId={OutboxEventId}, " +
            "FinalPortalStatus={FinalPortalStatus}, " +
            "OracleRequestId={OracleRequestId}, " +
            "Error={Error}",
            item.InvoiceId,
            item.Id,
            status,
            oracleRequestId,
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
        // --------------------------------------------------------
        // item.Attempts is the attempt count BEFORE ClaimAsync.
        //
        // For PENDING / RETRYING:
        // ClaimAsync has already incremented the DB count by 1,
        // therefore current submission attempt = Attempts + 1.
        //
        // For stale PROCESSING:
        // ClaimAsync DOES NOT increment the count.
        // Therefore existing count is preserved.
        // --------------------------------------------------------

        var attemptNumber =
            item.WasStaleProcessing
                ?
                item.Attempts
                :
                item.Attempts + 1;

        const int maxAttempts =
            6;

        // --------------------------------------------------------
        // For an old PROCESSING item, an Oracle lookup/network
        // problem should still honor the existing retry ceiling.
        // --------------------------------------------------------

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

        var retryExponent =
            Math.Max(
                1,
                attemptNumber
            );

        var delayMinutes =
            Math.Min(
                30,
                Math.Pow(
                    2,
                    Math.Min(
                        retryExponent,
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
            "Oracle AP integration will retry invoice. " +
            "PortalInvoiceId={InvoiceId}, " +
            "OutboxEventId={OutboxEventId}, " +
            "Attempt={Attempt}, " +
            "Next={Next}, " +
            "WasStaleProcessing={WasStaleProcessing}, " +
            "Error={Error}",
            item.InvoiceId,
            item.Id,
            attemptNumber,
            next,
            item.WasStaleProcessing,
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

    // ============================================================
    // OUTBOX ITEM
    // ============================================================

    private sealed record OutboxItem(
        Guid Id,
        Guid InvoiceId,
        int Attempts,
        Guid CorrelationId,
        string OriginalStatus,
        long? OracleRequestId
    )
    {
        public bool WasStaleProcessing =>
            string.Equals(
                OriginalStatus,
                "PROCESSING",
                StringComparison.OrdinalIgnoreCase
            );
    }
}
