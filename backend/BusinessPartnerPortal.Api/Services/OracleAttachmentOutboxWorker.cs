using BusinessPartnerPortal.Api.Data;
using BusinessPartnerPortal.Api.Oracle;

using Microsoft.EntityFrameworkCore;

namespace BusinessPartnerPortal.Api.Services;

/// <summary>
/// Synchronizes portal invoice documents to Oracle only after the AP invoice
/// has been created and the real Oracle INVOICE_ID has been stored in
/// invoice.invoices.oracle_invoice_id.
///
/// Routing:
/// Portal Invoice UUID -> OracleInvoiceId -> Documents(InvoiceId)
///
/// Each document is tracked independently in
/// integration.attachment_sync_logs. Successful documents are skipped on
/// retry, while only failed/pending documents are attempted again.
/// </summary>
public sealed class OracleAttachmentOutboxWorker(
    IServiceProvider services,
    ILogger<OracleAttachmentOutboxWorker> logger)
    : BackgroundService
{
    private const int MaxAttempts = 5;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Oracle attachment outbox worker iteration failed.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task ProcessBatchAsync(CancellationToken ct)
    {
        using var scope = services.CreateScope();

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var oracleRead = scope.ServiceProvider.GetRequiredService<OracleService>();
        var oracleOptions = scope.ServiceProvider.GetRequiredService<OracleOptions>();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();

        var oracleAp = new OracleApInvoiceService(
            oracleOptions,
            oracleRead,
            configuration,
            loggerFactory.CreateLogger<OracleApInvoiceService>());

        var items = await ClaimAsync(db, ct);

        foreach (var item in items)
        {
            try
            {
                var invoice = await db.Invoices
                    .AsNoTracking()
                    .Where(x => x.Id == item.InvoiceId && x.DeletedAt == null)
                    .Select(x => new
                    {
                        x.Id,
                        x.InvoiceNumber,
                        x.OracleInvoiceId
                    })
                    .SingleOrDefaultAsync(ct)
                    ?? throw new InvalidOperationException(
                        $"Portal invoice {item.InvoiceId} was not found.");

                if (string.IsNullOrWhiteSpace(invoice.OracleInvoiceId) ||
                    !long.TryParse(invoice.OracleInvoiceId, out var oracleInvoiceId) ||
                    oracleInvoiceId <= 0)
                {
                    throw new InvalidOperationException(
                        $"Portal invoice {invoice.Id} does not contain a valid Oracle INVOICE_ID.");
                }

                // Files are loaded only for the claimed invoice so memory stays
                // bounded even when many vendors submit invoices concurrently.
                var documents = await db.Documents
                    .AsNoTracking()
                    .Where(x => x.InvoiceId == invoice.Id)
                    .OrderBy(x => x.UploadedAt)
                    .Select(x => new OracleApDocument(
                        x.Id,
                        x.DocumentType,
                        x.OriginalFileName,
                        x.ContentType,
                        x.FileContent))
                    .ToListAsync(ct);

                if (documents.Count == 0)
                {
                    await MarkSuccessAsync(db, item, oracleInvoiceId, 0, ct);
                    continue;
                }

                var failedDocuments = new List<string>();
                var successfulDocuments = 0;

                foreach (var document in documents)
                {
                    await EnsureDocumentTrackingAsync(
                        db,
                        invoice.Id,
                        document,
                        oracleInvoiceId,
                        ct);

                    var currentStatus = await GetDocumentStatusAsync(
                        db,
                        document.DocumentId,
                        oracleInvoiceId,
                        ct);

                    // A successful document is never pushed again on an invoice
                    // retry. Oracle-side existence checking in the service remains
                    // an additional idempotency guard.
                    if (string.Equals(currentStatus, "SUCCESS", StringComparison.OrdinalIgnoreCase))
                    {
                        successfulDocuments++;
                        continue;
                    }

                    await MarkDocumentProcessingAsync(
                        db,
                        document.DocumentId,
                        oracleInvoiceId,
                        ct);

                    try
                    {
                        await oracleAp.AttachDocumentsToInvoiceAsync(
                            oracleInvoiceId,
                            new[] { document },
                            ct);

                        await MarkDocumentSuccessAsync(
                            db,
                            document.DocumentId,
                            oracleInvoiceId,
                            ct);

                        successfulDocuments++;

                        logger.LogInformation(
                            "Oracle AP attachment synchronized. PortalInvoiceId={PortalInvoiceId}, DocumentId={DocumentId}, FileName={FileName}, OracleInvoiceId={OracleInvoiceId}",
                            invoice.Id,
                            document.DocumentId,
                            document.FileName,
                            oracleInvoiceId);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        var error = Sanitize(ex.Message);

                        await MarkDocumentFailureAsync(
                            db,
                            document.DocumentId,
                            oracleInvoiceId,
                            error,
                            ct);

                        failedDocuments.Add($"{document.FileName}: {error}");

                        logger.LogWarning(
                            ex,
                            "Oracle AP attachment failed. PortalInvoiceId={PortalInvoiceId}, DocumentId={DocumentId}, FileName={FileName}, OracleInvoiceId={OracleInvoiceId}",
                            invoice.Id,
                            document.DocumentId,
                            document.FileName,
                            oracleInvoiceId);
                    }
                }

                if (failedDocuments.Count == 0)
                {
                    await MarkSuccessAsync(
                        db,
                        item,
                        oracleInvoiceId,
                        successfulDocuments,
                        ct);
                }
                else
                {
                    var invoiceLevelError = Sanitize(
                        $"{failedDocuments.Count} attachment(s) failed. " +
                        string.Join(" | ", failedDocuments));

                    await MarkFailureAsync(
                        db,
                        item,
                        invoiceLevelError,
                        ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                await MarkFailureAsync(db, item, Sanitize(ex.Message), ct);
            }
        }
    }

    private static async Task<List<AttachmentOutboxItem>> ClaimAsync(
        AppDbContext db,
        CancellationToken ct)
    {
        var items = new List<AttachmentOutboxItem>();
        var connection = db.Database.GetDbConnection();

        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
        }

        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT
                id,
                aggregate_id,
                attempt_count,
                correlation_id
            FROM integration.outbox_messages
            WHERE
              (
                status IN ('PENDING', 'RETRYING')
                OR
                (
                  status = 'PROCESSING'
                  AND
                  (
                    next_attempt_at <= now()
                    OR
                    (next_attempt_at IS NULL AND created_at <= now() - interval '15 minutes')
                  )
                )
              )
              AND event_type = 'InvoiceAttachmentsReady'
              AND
              (
                status = 'PROCESSING'
                OR next_attempt_at IS NULL
                OR next_attempt_at <= now()
              )
            ORDER BY created_at
            FOR UPDATE SKIP LOCKED
            LIMIT 3
            """;

        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            items.Add(new AttachmentOutboxItem(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetInt32(2),
                reader.GetGuid(3)));
        }

        await reader.DisposeAsync();

        foreach (var item in items)
        {
            await using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText =
                """
                UPDATE integration.outbox_messages
                SET status = 'PROCESSING',
                    attempt_count = attempt_count + 1,
                    next_attempt_at = now() + interval '15 minutes',
                    last_error = NULL
                WHERE id = @id
                """;

            var parameter = update.CreateParameter();
            parameter.ParameterName = "id";
            parameter.Value = item.Id;
            update.Parameters.Add(parameter);

            await update.ExecuteNonQueryAsync(ct);
        }

        await transaction.CommitAsync(ct);
        return items;
    }

    private static async Task EnsureDocumentTrackingAsync(
        AppDbContext db,
        Guid invoiceId,
        OracleApDocument document,
        long oracleInvoiceId,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO integration.attachment_sync_logs
            (
                id,
                invoice_id,
                document_id,
                oracle_invoice_id,
                file_name,
                document_type,
                status,
                attempt_count,
                created_at,
                updated_at
            )
            VALUES
            (
                {id},
                {invoiceId},
                {document.DocumentId},
                {oracleInvoiceId},
                {document.FileName},
                {document.DocumentType},
                {"PENDING"},
                0,
                {now},
                {now}
            )
            ON CONFLICT (document_id, oracle_invoice_id)
            DO UPDATE SET
                invoice_id = EXCLUDED.invoice_id,
                file_name = EXCLUDED.file_name,
                document_type = EXCLUDED.document_type,
                updated_at = EXCLUDED.updated_at
            """,
            ct);
    }

    private static async Task<string?> GetDocumentStatusAsync(
        AppDbContext db,
        Guid documentId,
        long oracleInvoiceId,
        CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;

        if (shouldClose)
        {
            await connection.OpenAsync(ct);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT status
                FROM integration.attachment_sync_logs
                WHERE document_id = @document_id
                  AND oracle_invoice_id = @oracle_invoice_id
                LIMIT 1
                """;

            var documentParameter = command.CreateParameter();
            documentParameter.ParameterName = "document_id";
            documentParameter.Value = documentId;
            command.Parameters.Add(documentParameter);

            var oracleParameter = command.CreateParameter();
            oracleParameter.ParameterName = "oracle_invoice_id";
            oracleParameter.Value = oracleInvoiceId;
            command.Parameters.Add(oracleParameter);

            var result = await command.ExecuteScalarAsync(ct);
            return result is null || result == DBNull.Value
                ? null
                : Convert.ToString(result);
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static Task<int> MarkDocumentProcessingAsync(
        AppDbContext db,
        Guid documentId,
        long oracleInvoiceId,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        return db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE integration.attachment_sync_logs
            SET status = {"PROCESSING"},
                attempt_count = attempt_count + 1,
                last_error = NULL,
                updated_at = {now}
            WHERE document_id = {documentId}
              AND oracle_invoice_id = {oracleInvoiceId}
            """,
            ct);
    }

    private static Task<int> MarkDocumentSuccessAsync(
        AppDbContext db,
        Guid documentId,
        long oracleInvoiceId,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        return db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE integration.attachment_sync_logs
            SET status = {"SUCCESS"},
                last_error = NULL,
                synced_at = {now},
                updated_at = {now}
            WHERE document_id = {documentId}
              AND oracle_invoice_id = {oracleInvoiceId}
            """,
            ct);
    }

    private static Task<int> MarkDocumentFailureAsync(
        AppDbContext db,
        Guid documentId,
        long oracleInvoiceId,
        string error,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        return db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE integration.attachment_sync_logs
            SET status = {"FAILED"},
                last_error = {error},
                synced_at = NULL,
                updated_at = {now}
            WHERE document_id = {documentId}
              AND oracle_invoice_id = {oracleInvoiceId}
            """,
            ct);
    }

    private async Task MarkSuccessAsync(
        AppDbContext db,
        AttachmentOutboxItem item,
        long oracleInvoiceId,
        int documentCount,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE integration.outbox_messages
            SET status = 'PROCESSED',
                processed_at = {now},
                next_attempt_at = NULL,
                last_error = NULL,
                external_reference = {oracleInvoiceId.ToString()}
            WHERE id = {item.Id}
            """,
            ct);

        logger.LogInformation(
            "Oracle AP attachments synchronized. PortalInvoiceId={PortalInvoiceId}, OracleInvoiceId={OracleInvoiceId}, DocumentCount={DocumentCount}",
            item.InvoiceId,
            oracleInvoiceId,
            documentCount);
    }

    private async Task MarkFailureAsync(
        AppDbContext db,
        AttachmentOutboxItem item,
        string error,
        CancellationToken ct)
    {
        var attemptAfterClaim = item.AttemptCount + 1;
        var permanent = attemptAfterClaim >= MaxAttempts;
        var status = permanent ? "FAILED" : "RETRYING";
        DateTimeOffset? nextAttemptAt = permanent
            ? null
            : DateTimeOffset.UtcNow.Add(GetRetryDelay(attemptAfterClaim));

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE integration.outbox_messages
            SET status = {status},
                next_attempt_at = {nextAttemptAt},
                last_error = {error}
            WHERE id = {item.Id}
            """,
            ct);

        if (permanent)
        {
            logger.LogError(
                "Oracle AP attachment synchronization failed permanently. PortalInvoiceId={PortalInvoiceId}, Error={Error}",
                item.InvoiceId,
                error);
        }
        else
        {
            logger.LogWarning(
                "Oracle AP attachment synchronization will retry. PortalInvoiceId={PortalInvoiceId}, Attempt={Attempt}, Error={Error}",
                item.InvoiceId,
                attemptAfterClaim,
                error);
        }
    }

    private static TimeSpan GetRetryDelay(int attempt) =>
        attempt switch
        {
            <= 1 => TimeSpan.FromMinutes(1),
            2 => TimeSpan.FromMinutes(5),
            3 => TimeSpan.FromMinutes(15),
            _ => TimeSpan.FromHours(1)
        };

    private static string Sanitize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Oracle attachment synchronization failed.";
        }

        var cleaned = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return cleaned.Length <= 2000 ? cleaned : cleaned[..2000];
    }

    private sealed class AttachmentOutboxItem
    {
        public AttachmentOutboxItem(
            Guid id,
            Guid invoiceId,
            int attemptCount,
            Guid correlationId)
        {
            Id = id;
            InvoiceId = invoiceId;
            AttemptCount = attemptCount;
            CorrelationId = correlationId;
        }

        public Guid Id { get; }
        public Guid InvoiceId { get; }
        public int AttemptCount { get; }
        public Guid CorrelationId { get; }
    }
}
