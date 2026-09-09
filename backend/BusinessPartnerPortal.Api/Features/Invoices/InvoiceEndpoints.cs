using System.Security.Cryptography;

using BusinessPartnerPortal.Api.Common;
using BusinessPartnerPortal.Api.Data;
using BusinessPartnerPortal.Api.Domain;
using BusinessPartnerPortal.Api.Oracle;
using BusinessPartnerPortal.Api.Security;

using Microsoft.EntityFrameworkCore;

namespace BusinessPartnerPortal.Api.Features.Invoices;

public static class InvoiceEndpoints
{
    // ============================================================
    // MAP ENDPOINTS
    // ============================================================

    public static IEndpointRouteBuilder MapInvoiceEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/v1/invoices")
            .RequireAuthorization()
            .WithTags("Invoices");

        // ========================================================
        // MY INVOICES
        // ========================================================

        group.MapGet(
            "/my",
            async (
                CurrentUser current,
                AppDbContext db,
                CancellationToken ct) =>
            {
                await current.DemandAsync(
                    "INVOICE.VIEW_OWN",
                    ct);

                var vendorId =
                    await current.GetVendorIdAsync(ct)
                    ?? throw new ApiException(
                        403,
                        "Vendor account mapping is missing.");

                var rows =
                    await db.Invoices
                        .Where(
                            x =>
                                x.VendorId == vendorId &&
                                x.DeletedAt == null)
                        .OrderByDescending(
                            x => x.UpdatedAt)
                        .Select(
                            x => new
                            {
                                x.Id,
                                x.InvoiceNumber,
                                x.InvoiceDate,
                                x.InvoiceAmount,
                                x.Status,
                                x.IntegrationStatus,
                                x.SubmissionDate,
                                x.UpdatedAt,
                                x.PoNumber,
                                x.GrnNumbers,
                                x.InvoiceType,
                                x.Remarks
                            })
                        .ToListAsync(ct);

                return Results.Ok(
                    rows.Select(
                        x => new
                        {
                            x.Id,
                            x.InvoiceNumber,
                            x.InvoiceDate,
                            x.InvoiceAmount,
                            x.Status,
                            x.IntegrationStatus,
                            x.SubmissionDate,
                            x.UpdatedAt,

                            /*
                             * Backward compatibility.
                             */
                            x.PoNumber,

                            poNumbers =
                                SplitCsv(
                                    x.PoNumber),

                            grnNumbers =
                                SplitCsv(
                                    x.GrnNumbers),

                            x.InvoiceType,

                            description =
                                x.Remarks
                        }));
            });

        // ========================================================
        // GET INVOICE
        // ========================================================

        group.MapGet(
            "/{id:guid}",
            async (
                Guid id,
                CurrentUser current,
                AppDbContext db,
                CancellationToken ct) =>
            {
                var vendorId =
                    await current
                        .GetVendorIdAsync(ct);

                var invoice =
                    await db.Invoices
                        .FirstOrDefaultAsync(
                            x =>
                                x.Id == id &&
                                (
                                    current.IsAdmin ||
                                    x.VendorId == vendorId
                                ),
                            ct)
                    ??
                    throw new ApiException(
                        404,
                        "Invoice not found.");

                return Results.Ok(
                    new
                    {
                        invoice.Id,
                        invoice.InvoiceNumber,
                        invoice.InvoiceDate,
                        invoice.InvoiceAmount,

                        invoice.PoNumber,

                        poNumbers =
                            SplitCsv(
                                invoice.PoNumber),

                        grnNumbers =
                            SplitCsv(
                                invoice.GrnNumbers),

                        invoice.InvoiceType,
                        invoice.Status,
                        invoice.IntegrationStatus,

                        description =
                            invoice.Remarks
                    });
            });

        // ========================================================
        // HISTORY
        // ========================================================

        group.MapGet(
            "/{id:guid}/history",
            async (
                Guid id,
                CurrentUser current,
                AppDbContext db,
                CancellationToken ct) =>
            {
                var vendorId =
                    await current
                        .GetVendorIdAsync(ct);

                var exists =
                    await db.Invoices
                        .AnyAsync(
                            x =>
                                x.Id == id &&
                                (
                                    current.IsAdmin ||
                                    x.VendorId == vendorId
                                ),
                            ct);

                if (!exists)
                {
                    throw new ApiException(
                        404,
                        "Invoice not found.");
                }

                return Results.Ok(
                    await db
                        .InvoiceStatusHistory
                        .Where(
                            x =>
                                x.InvoiceId ==
                                id)
                        .OrderBy(
                            x =>
                                x.ChangedAt)
                        .ToListAsync(ct));
            });

        // ========================================================
        // SAVE DRAFT
        // ========================================================

        group.MapPost(
                "/draft",
                async (
                    HttpRequest request,
                    CurrentUser current,
                    AppDbContext db,
                    OracleService oracle,
                    OracleOptions oracleOptions,
                    IConfiguration config,
                    ILoggerFactory loggerFactory,
                    CancellationToken ct) =>
                    await Save(
                        request,
                        current,
                        db,
                        oracle,
                        oracleOptions,
                        config,
                        loggerFactory.CreateLogger(
                            "Invoice"),
                        true,
                        null,
                        ct))
            .DisableAntiforgery();

        // ========================================================
        // SUBMIT
        // ========================================================

        group.MapPost(
                "/",
                async (
                    HttpRequest request,
                    CurrentUser current,
                    AppDbContext db,
                    OracleService oracle,
                    OracleOptions oracleOptions,
                    IConfiguration config,
                    ILoggerFactory loggerFactory,
                    CancellationToken ct) =>
                    await Save(
                        request,
                        current,
                        db,
                        oracle,
                        oracleOptions,
                        config,
                        loggerFactory.CreateLogger(
                            "Invoice"),
                        false,
                        null,
                        ct))
            .DisableAntiforgery();

        // ========================================================
        // RESUBMIT
        // ========================================================

        group.MapPost(
                "/{id:guid}/resubmit",
                async (
                    Guid id,
                    HttpRequest request,
                    CurrentUser current,
                    AppDbContext db,
                    OracleService oracle,
                    OracleOptions oracleOptions,
                    IConfiguration config,
                    ILoggerFactory loggerFactory,
                    CancellationToken ct) =>
                    await Save(
                        request,
                        current,
                        db,
                        oracle,
                        oracleOptions,
                        config,
                        loggerFactory.CreateLogger(
                            "Invoice"),
                        false,
                        id,
                        ct))
            .DisableAntiforgery();

        return app;
    }

    // ============================================================
    // SAVE
    // ============================================================

    private static async Task<IResult> Save(
        HttpRequest request,
        CurrentUser current,
        AppDbContext db,
        OracleService oracle,
        OracleOptions oracleOptions,
        IConfiguration config,
        ILogger log,
        bool draft,
        Guid? resubmitId,
        CancellationToken ct)
    {
        await current.DemandAsync(
            resubmitId is null
                ? "INVOICE.CREATE"
                : "INVOICE.RESUBMIT",
            ct);

        var vendorId =
            await current.GetVendorIdAsync(ct)
            ??
            throw new ApiException(
                403,
                "Vendor account mapping is missing.");

        var oracleRaw =
            await db.Vendors
                .Where(
                    x =>
                        x.Id ==
                        vendorId)
                .Select(
                    x =>
                        x.OracleVendorId)
                .FirstOrDefaultAsync(ct);

        if (
            !decimal.TryParse(
                oracleRaw,
                out var oracleVendorId))
        {
            throw new ApiException(
                409,
                "Oracle vendor mapping is missing or invalid.");
        }

        if (
            !request.HasFormContentType)
        {
            throw new ApiException(
                400,
                "multipart/form-data is required.");
        }

        var form =
            await request
                .ReadFormAsync(ct);

        var idempotencyKey = request.Headers["Idempotency-Key"].FirstOrDefault()?.Trim();
        if (!draft && string.IsNullOrWhiteSpace(idempotencyKey))
            throw new ApiException(400, "Idempotency-Key header is required for invoice submission.");

        // ========================================================
        // MULTI PO
        // ========================================================

        var poNumbers =
            form["poNumbers"]
                .Select(
                    x =>
                        x?.Trim() ??
                        "")
                .Where(
                    x =>
                        x.Length >
                        0)
                .Distinct(
                    StringComparer
                        .OrdinalIgnoreCase)
                .ToList();

        /*
         * Backward compatibility:
         * old frontend sends poNumber.
         *
         * New frontend also sends
         * poNumber as comma separated.
         */
        if (
            poNumbers.Count ==
            0)
        {
            var legacyPoValue =
                form["poNumber"]
                    .FirstOrDefault()
                    ?.Trim()
                ??
                "";

            if (
                legacyPoValue.Length >
                0)
            {
                poNumbers =
                    legacyPoValue
                        .Split(
                            ',',
                            StringSplitOptions
                                .RemoveEmptyEntries |
                            StringSplitOptions
                                .TrimEntries)
                        .Distinct(
                            StringComparer
                                .OrdinalIgnoreCase)
                        .ToList();
            }
        }

        // ========================================================
        // GRNS
        // ========================================================

        var grnNumbers =
            form["grnNumbers"]
                .Select(
                    x =>
                        x?.Trim() ??
                        "")
                .Where(
                    x =>
                        x.Length >
                        0)
                .Distinct(
                    StringComparer
                        .OrdinalIgnoreCase)
                .ToList();

        var rcvTransactionIds = form["rcvTransactionIds"]
            .Select(x => long.TryParse(x, out var id) ? id : 0L)
            .Where(x => x > 0)
            .Distinct()
            .ToList();

        // ========================================================
        // INVOICE FIELDS
        // ========================================================

        var invoiceNumber =
            form["invoiceNumber"]
                .FirstOrDefault()
                ?.Trim()
            ??
            "";

        var invoiceType =
            (
                form["invoiceType"]
                    .FirstOrDefault()
                ??
                "GOODS"
            )
            .Trim()
            .ToUpperInvariant();

        var description =
            form["description"]
                .FirstOrDefault()
                ?.Trim()
            ??
            "";

        if (
            description.Length >
            500)
        {
            throw new ApiException(
                400,
                "Description cannot exceed 500 characters.");
        }

        var invoiceDate =
            DateOnly.TryParse(
                form["invoiceDate"]
                    .FirstOrDefault(),
                out var parsedDate)
                ?
                parsedDate
                :
                (DateOnly?)null;

        var invoiceAmount =
            decimal.TryParse(
                form["invoiceAmount"]
                    .FirstOrDefault(),
                out var parsedAmount)
                ?
                parsedAmount
                :
                0m;

        // ========================================================
        // BASIC VALIDATION
        // ========================================================

        if (
            !draft &&
            (
                poNumbers.Count ==
                    0 ||
                invoiceNumber.Length ==
                    0 ||
                invoiceDate is null ||
                invoiceAmount <=
                    0 ||
                grnNumbers.Count ==
                    0 ||
                rcvTransactionIds.Count == 0
            ))
        {
            throw new ApiException(
                400,
                "At least one PO, at least one GRN line, invoice number, invoice date and positive amount are required.");
        }

        if (
            invoiceType !=
                "GOODS" &&
            invoiceType !=
                "SERVICE")
        {
            throw new ApiException(
                400,
                "Invoice type must be GOODS or SERVICE.");
        }

        // ========================================================
        // VALIDATE ALL SELECTED POS / GRNS AGAINST ORACLE
        // ========================================================

        var allOracleRows =
            new List<
                OraclePoGrnDto
            >();

        foreach (
            var poNumber in
            poNumbers)
        {
            var oracleRows =
                await oracle
                    .GetPoGrnsAsync(
                        oracleVendorId,
                        poNumber,
                        ct);

            if (
                oracleRows.Count ==
                0)
            {
                throw new ApiException(
                    400,
                    $"PO {poNumber} is not available for this vendor in Oracle.");
            }

            allOracleRows
                .AddRange(
                    oracleRows);
        }

        var selectedReceiptLines = new List<OracleReceiptLine>();
        if (!draft)
        {
            var ap = new OracleApInvoiceService(oracleOptions, oracle, config, Microsoft.Extensions.Logging.Abstractions.NullLogger<OracleApInvoiceService>.Instance);
            foreach (var poNumber in poNumbers)
            {
                var poGrns = grnNumbers.Where(g => allOracleRows.Any(r => r.PoNumber == poNumber && string.Equals(r.GrnNumber, g, StringComparison.OrdinalIgnoreCase))).ToList();
                if (poGrns.Count == 0) continue;
                var lines = await ap.GetReceiptLinesForPortalAsync(oracleVendorId, poNumber, poGrns, ct);
                selectedReceiptLines.AddRange(lines.Where(x => rcvTransactionIds.Contains(x.RcvTransactionId)));
            }
            var resolvedIds = selectedReceiptLines.Select(x => x.RcvTransactionId).Distinct().ToHashSet();
            var missingIds = rcvTransactionIds.Where(x => !resolvedIds.Contains(x)).ToList();
            if (missingIds.Count > 0) throw new ApiException(409, $"Selected receipt line(s) are no longer available: {string.Join(", ", missingIds)}");
            var selectedGrns = selectedReceiptLines.Select(x => x.GrnNumber).Distinct(StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (grnNumbers.Any(x => !selectedGrns.Contains(x))) throw new ApiException(409, "One or more selected GRNs do not contain an eligible selected receipt line.");
        }

        foreach (
            var grnNumber in
            grnNumbers)
        {
            var matchingRows =
                allOracleRows
                    .Where(
                        x =>
                            string.Equals(
                                x.GrnNumber,
                                grnNumber,
                                StringComparison
                                    .OrdinalIgnoreCase))
                    .ToList();

            if (
                matchingRows.Count ==
                0)
            {
                throw new ApiException(
                    400,
                    $"GRN {grnNumber} does not belong to any selected PO for this vendor.");
            }

            var validRow =
                matchingRows
                    .FirstOrDefault(
                        row =>
                        {
                            var inspection =
                                (
                                    row.InspectionStatus
                                    ??
                                    ""
                                )
                                .ToUpperInvariant();

                            var blocked =
                                inspection is
                                    "PENDING" or
                                    "PENDING QC" or
                                    "PENDING_QC" or
                                    "AWAITING INSPECTION" or
                                    "NOT RECEIVED";

                            var available =
                                (
                                    row.QuantityAvailableToInvoice
                                    ??
                                    row.GrnReceivedQuantity
                                    ??
                                    0
                                )
                                >
                                0;

                            return
                                !blocked &&
                                available;
                        });

            if (
                validRow is
                not null)
            {
                continue;
            }

            var qcBlocked =
                matchingRows.Any(
                    row =>
                    {
                        var inspection =
                            (
                                row.InspectionStatus
                                ??
                                ""
                            )
                            .ToUpperInvariant();

                        return
                            inspection is
                                "PENDING" or
                                "PENDING QC" or
                                "PENDING_QC" or
                                "AWAITING INSPECTION" or
                                "NOT RECEIVED";
                    });

            if (qcBlocked)
            {
                throw new ApiException(
                    409,
                    $"GRN {grnNumber} is Pending with QC and cannot be invoiced yet.");
            }

            throw new ApiException(
                409,
                $"GRN {grnNumber} has no available quantity to invoice.");
        }

        // ========================================================
        // DUPLICATE INVOICE
        // ========================================================

        var duplicate =
            await db.Invoices
                .AnyAsync(
                    x =>
                        x.VendorId ==
                            vendorId &&
                        x.DeletedAt ==
                            null &&
                        x.InvoiceNumber
                            .ToLower() ==
                        invoiceNumber
                            .ToLower() &&
                        (
                            !resubmitId
                                .HasValue ||
                            x.Id !=
                                resubmitId
                                    .Value
                        ),
                    ct);

        if (
            invoiceNumber.Length >
                0 &&
            duplicate)
        {
            throw new ApiException(
                409,
                "This invoice number already exists for the vendor.");
        }

        // ========================================================
        // FILES
        // ========================================================

        var invoiceFiles =
            form.Files
                .GetFiles(
                    "invoiceFiles")
                .ToList();

        var deliveryChallanFiles =
            form.Files
                .GetFiles(
                    "deliveryChallanFiles")
                .ToList();

        // Backward compatibility.
        var legacyInvoiceFile =
            form.Files
                .GetFile(
                    "invoiceFile");

        if (
            legacyInvoiceFile is
                not null &&
            invoiceFiles.Count ==
                0)
        {
            invoiceFiles.Add(
                legacyInvoiceFile);
        }

        var legacyDeliveryChallanFile =
            form.Files
                .GetFile(
                    "deliveryChallanFile");

        if (
            legacyDeliveryChallanFile is
                not null &&
            deliveryChallanFiles.Count ==
                0)
        {
            deliveryChallanFiles.Add(
                legacyDeliveryChallanFile);
        }

        /*
         * Invoice Copy:
         * exactly one new file max.
         */
        if (
            invoiceFiles.Count >
            1)
        {
            throw new ApiException(
                400,
                "Only one Invoice Copy can be uploaded.");
        }

        if (
            !draft &&
            invoiceFiles.Count ==
                0 &&
            resubmitId is
                null)
        {
            throw new ApiException(
                400,
                "Invoice Copy is required.");
        }

        /*
         * DC:
         * multiple files allowed.
         */
        if (
            !draft &&
            invoiceType ==
                "GOODS" &&
            deliveryChallanFiles.Count ==
                0 &&
            resubmitId is
                null)
        {
            throw new ApiException(
                400,
                "At least one Receipted Delivery Challan is mandatory for goods invoices.");
        }

        foreach (
            var file in
            invoiceFiles)
        {
            ValidateFile(
                file,
                config);
            await BusinessPartnerPortal.Api.Services.FileSignatureValidator.ValidateAsync(file, ct);
        }

        foreach (
            var file in
            deliveryChallanFiles)
        {
            ValidateFile(
                file,
                config);
            await BusinessPartnerPortal.Api.Services.FileSignatureValidator.ValidateAsync(file, ct);
        }

        // ========================================================
        // DATABASE TRANSACTION
        // ========================================================

        await using var transaction =
            await db.Database
                .BeginTransactionAsync(ct);

        var now =
            DateTimeOffset
                .UtcNow;

        if (!draft && !string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var operation = resubmitId.HasValue ? "INVOICE_RESUBMIT" : "INVOICE_SUBMIT";
            var existingRequest = await db.IdempotencyRequests.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == current.UserId && x.Operation == operation && x.IdempotencyKey == idempotencyKey, ct);
            if (existingRequest?.AggregateId is Guid existingInvoiceId)
            {
                await transaction.RollbackAsync(ct);
                return Results.Ok(new { id = existingInvoiceId, duplicateRequest = true });
            }
        }

        Invoice invoice;

        string? oldStatus =
            null;

        var storedPoNumbers =
            string.Join(
                ',',
                poNumbers);

        var storedGrnNumbers =
            string.Join(
                ',',
                grnNumbers);

        // ========================================================
        // RESUBMIT
        // ========================================================

        if (
            resubmitId
                .HasValue)
        {
            invoice =
                await db.Invoices
                    .FirstOrDefaultAsync(
                        x =>
                            x.Id ==
                                resubmitId
                                    .Value &&
                            x.VendorId ==
                                vendorId,
                        ct)
                ??
                throw new ApiException(
                    404,
                    "Invoice not found.");

            if (
                invoice.Status !=
                "RETURNED")
            {
                throw new ApiException(
                    409,
                    "Only returned invoices can be resubmitted.");
            }

            oldStatus =
                invoice.Status;

            invoice.InvoiceNumber =
                invoiceNumber.Length >
                0
                    ?
                    invoiceNumber
                    :
                    invoice.InvoiceNumber;

            invoice.InvoiceDate =
                invoiceDate
                ??
                invoice.InvoiceDate;

            if (
                invoiceAmount >
                0)
            {
                invoice.InvoiceAmount =
                    invoiceAmount;
            }

            if (
                poNumbers.Count >
                0)
            {
                invoice.PoNumber =
                    storedPoNumbers;
            }

            if (
                grnNumbers.Count >
                0)
            {
                invoice.GrnNumbers =
                    storedGrnNumbers;
            }

            invoice.InvoiceType =
                invoiceType;

            invoice.Remarks =
                description.Length >
                0
                    ?
                    description
                    :
                    null;

            invoice.Status =
                "RESUBMITTED";

            invoice.IntegrationStatus =
                "PENDING";

            invoice.SubmissionDate =
                now;

            invoice.UpdatedAt =
                now;
        }

        // ========================================================
        // NEW
        // ========================================================

        else
        {
            invoice =
                new Invoice
                {
                    Id =
                        Guid.NewGuid(),

                    VendorId =
                        vendorId,

                    InvoiceNumber =
                        invoiceNumber,

                    InvoiceDate =
                        invoiceDate,

                    InvoiceAmount =
                        invoiceAmount,

                    CurrencyCode =
                        "PKR",

                    /*
                     * Multiple POs stored
                     * comma-separated for
                     * backward compatibility.
                     */
                    PoNumber =
                        storedPoNumbers,

                    GrnNumbers =
                        storedGrnNumbers,

                    InvoiceType =
                        invoiceType,

                    Remarks =
                        description.Length >
                        0
                            ?
                            description
                            :
                            null,

                    Status =
                        draft
                            ?
                            "DRAFT"
                            :
                            "SUBMITTED",

                    IntegrationStatus =
                        draft
                            ?
                            "NOT_STARTED"
                            :
                            "PENDING",

                    SubmissionDate =
                        draft
                            ?
                            null
                            :
                            now,

                    CreatedBy =
                        current.UserId,

                    CreatedAt =
                        now,

                    UpdatedAt =
                        now
                };

            db.Invoices.Add(
                invoice);
        }

        // ========================================================
        // SAVE PARENT INVOICE FIRST
        // ========================================================
        //
        // Documents, GRN allocations, status history and
        // idempotency records all reference invoice.invoices.
        // Saving the parent row first prevents FK violations such
        // as fk_document_invoice. This is still inside the same
        // database transaction, so any later failure rolls back
        // the invoice as well.
        // ========================================================

        await db.SaveChangesAsync(ct);

        if (!draft)
        {
            foreach (var line in selectedReceiptLines.OrderBy(x => x.RcvTransactionId))
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({line.RcvTransactionId})", ct);
                var alreadyAllocated = await db.InvoiceLineGrnAllocations
                    .Where(x => x.RcvTransactionId == line.RcvTransactionId && x.InvoiceId != invoice.Id)
                    .Join(db.Invoices.Where(i => i.DeletedAt == null && i.Status != "CANCELLED" && i.Status != "INTEGRATION_FAILED"), a => a.InvoiceId, i => i.Id, (a, i) => a.AllocatedQuantity)
                    .SumAsync(ct);
                var requestedQty = line.AvailableQuantity;
                if (alreadyAllocated + requestedQty > line.AvailableQuantity)
                    throw new ApiException(409, $"GRN {line.GrnNumber} line {line.PoLineNumber} no longer has enough available quantity.");
            }

            var oldAllocations = await db.InvoiceLineGrnAllocations.Where(x => x.InvoiceId == invoice.Id).ToListAsync(ct);
            db.InvoiceLineGrnAllocations.RemoveRange(oldAllocations);
            foreach (var line in selectedReceiptLines)
            {
                db.InvoiceLineGrnAllocations.Add(new InvoiceLineGrnAllocation
                {
                    Id = Guid.NewGuid(), InvoiceId = invoice.Id, VendorId = vendorId, PoNumber = line.PoNumber, GrnNumber = line.GrnNumber,
                    RcvTransactionId = line.RcvTransactionId, PoHeaderId = line.PoHeaderId, PoLineId = line.PoLineId, PoLineNumber = line.PoLineNumber,
                    PoLineLocationId = line.PoLineLocationId, ReceivedQuantity = line.ReceivedQuantity, AvailableQuantityAtSubmit = line.AvailableQuantity,
                    AllocatedQuantity = line.AvailableQuantity, UnitPrice = line.UnitPrice, AllocatedAmount = line.ExtendedAmount, MatchOption = line.MatchOption, CreatedAt = now
                });
            }
        }

        // ========================================================
        // STATUS HISTORY
        // ========================================================

        db.InvoiceStatusHistory.Add(
            new InvoiceStatusHistory
            {
                InvoiceId =
                    invoice.Id,

                OldStatus =
                    oldStatus,

                NewStatus =
                    invoice.Status,

                ChangedBy =
                    current.UserId,

                ChangedAt =
                    now,

                Source =
                    "PORTAL"
            });

        // ========================================================
        // DOCUMENTS
        // ========================================================

        foreach (
            var file in
            invoiceFiles)
        {
            await AddDoc(
                db,
                invoice.Id,
                file,
                "INVOICE",
                current.UserId,
                now,
                ct);
        }

        foreach (
            var file in
            deliveryChallanFiles)
        {
            await AddDoc(
                db,
                invoice.Id,
                file,
                "DELIVERY_CHALLAN",
                current.UserId,
                now,
                ct);
        }

        if (!draft && !string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var operation = resubmitId.HasValue ? "INVOICE_RESUBMIT" : "INVOICE_SUBMIT";
            var hashInput = $"{vendorId}|{invoiceNumber.Trim().ToUpperInvariant()}|{invoiceDate}|{invoiceAmount}|{string.Join(",", rcvTransactionIds.OrderBy(x => x))}";
            var requestHash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(hashInput))).ToLowerInvariant();
            db.IdempotencyRequests.Add(new IdempotencyRequest { Id = Guid.NewGuid(), UserId = current.UserId, VendorId = vendorId, IdempotencyKey = idempotencyKey!, Operation = operation, RequestHash = requestHash, AggregateId = invoice.Id, ResponseStatus = 200, CreatedAt = now, ExpiresAt = now.AddHours(24) });
        }

        await db
            .SaveChangesAsync(ct);

        await db.Database.ExecuteSqlInterpolatedAsync($@"INSERT INTO audit.audit_logs (user_id, action, entity_type, entity_id, correlation_id, created_at) VALUES ({current.UserId}, {(draft ? "INVOICE_DRAFT_CREATED" : resubmitId.HasValue ? "INVOICE_RESUBMITTED" : "INVOICE_SUBMITTED")}, {"Invoice"}, {invoice.Id}, {Guid.NewGuid()}, {now})", ct);

        // ========================================================
        // OUTBOX
        // ========================================================

        if (!draft)
        {
            var eventType =
                resubmitId.HasValue
                    ? "InvoiceResubmitted"
                    : "InvoiceSubmitted";

            var payload =
                System.Text.Json
                    .JsonSerializer
                    .Serialize(
                        new
                        {
                            invoiceId =
                                invoice.Id,

                            vendorId,

                            oracleVendorId,

                            /*
                             * New multi PO payload.
                             */
                            poNumbers,

                            /*
                             * Legacy field remains available.
                             */
                            poNumber =
                                storedPoNumbers,

                            grnNumbers,

                            /*
                             * Exact Oracle receiving transaction IDs
                             * selected by the vendor.
                             */
                            rcvTransactionIds,

                            invoiceNumber =
                                invoice.InvoiceNumber,

                            invoiceDate =
                                invoice.InvoiceDate,

                            invoiceAmount =
                                invoice.InvoiceAmount,

                            invoiceType =
                                invoice.InvoiceType,

                            description =
                                invoice.Remarks
                        });

            var outboxId =
                Guid.NewGuid();

            var outboxCorrelationId =
                Guid.NewGuid();

            await db.Database
                .ExecuteSqlInterpolatedAsync(
                    $@"INSERT INTO integration.outbox_messages
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
                           {outboxId},
                           {eventType},
                           {"Invoice"},
                           {invoice.Id},
                           {payload}::jsonb,
                           {"PENDING"},
                           0,
                           {outboxCorrelationId},
                           {now}
                       )",
                    ct);
        }

        await transaction
            .CommitAsync(ct);

        log.LogInformation(
            "Invoice {InvoiceId} saved. Draft={Draft}, POs={PoNumbers}, GRNs={GrnNumbers}",
            invoice.Id,
            draft,
            storedPoNumbers,
            storedGrnNumbers);

        return Results.Ok(
            new
            {
                id =
                    invoice.Id,

                status =
                    invoice.Status,

                integrationStatus =
                    invoice.IntegrationStatus,

                poNumbers,

                grnNumbers
            });
    }

    // ============================================================
    // FILE VALIDATION
    // ============================================================

    private static void ValidateFile(
        IFormFile? file,
        IConfiguration config)
    {
        if (
            file is null)
        {
            return;
        }

        var configuredMaxMb =
            int.TryParse(
                config[
                    "MAX_UPLOAD_SIZE_MB"],
                out var configuredMax)
                ?
                configuredMax
                :
                1;

        /*
         * Current requirement:
         * max 1 MB.
         */
        var maxMb =
            Math.Min(
                configuredMaxMb,
                1);

        if (
            file.Length <=
                0 ||
            file.Length >
                maxMb *
                1024L *
                1024L)
        {
            throw new ApiException(
                400,
                $"Attachment must be between 1 byte and {maxMb} MB.");
        }

        var extension =
            Path.GetExtension(
                    file.FileName)
                .ToLowerInvariant();

        if (
            !new[]
            {
                ".pdf",
                ".png",
                ".jpg",
                ".jpeg"
            }
            .Contains(
                extension))
        {
            throw new ApiException(
                400,
                "Only PDF, PNG, JPG and JPEG files are allowed.");
        }
    }

    // ============================================================
    // ADD DOCUMENT
    // ============================================================

    private static async Task AddDoc(
        AppDbContext db,
        Guid invoiceId,
        IFormFile file,
        string type,
        Guid userId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await using var stream =
            new MemoryStream();

        await file
            .CopyToAsync(
                stream,
                ct);

        var bytes =
            stream.ToArray();

        db.Documents.Add(
            new Document
            {
                Id =
                    Guid.NewGuid(),

                InvoiceId =
                    invoiceId,

                DocumentType =
                    type,

                OriginalFileName =
                    Path.GetFileName(
                        file.FileName),

                ContentType =
                    file.ContentType,

                FileExtension =
                    Path.GetExtension(
                            file.FileName)
                        .ToLowerInvariant(),

                FileSize =
                    file.Length,

                FileHashSha256 =
                    Convert
                        .ToHexString(
                            SHA256.HashData(
                                bytes))
                        .ToLowerInvariant(),

                FileContent =
                    bytes,

                UploadedBy =
                    userId,

                UploadedAt =
                    now
            });
    }

    // ============================================================
    // CSV HELPER
    // ============================================================

    private static string[] SplitCsv(
        string? value)
    {
        return (
            value ??
            string.Empty
        )
        .Split(
            ',',
            StringSplitOptions
                .RemoveEmptyEntries |
            StringSplitOptions
                .TrimEntries)
        .Distinct(
            StringComparer
                .OrdinalIgnoreCase)
        .ToArray();
    }
}