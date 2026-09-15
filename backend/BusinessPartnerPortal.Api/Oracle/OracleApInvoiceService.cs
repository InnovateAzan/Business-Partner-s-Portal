using System.Data;
using System.Diagnostics;
using System.Globalization;

using Oracle.ManagedDataAccess.Client;

namespace BusinessPartnerPortal.Api.Oracle;

public sealed class OracleApInvoiceService(
    OracleOptions options,
    OracleService oracleReadService,
    IConfiguration config,
    ILogger<OracleApInvoiceService> logger)
{
    // Oracle EBS attachment category: Quick Invoices.
    // Shared by Invoice, Delivery Challan and Supporting Document attachments.
    private const long OracleApQuickInvoicesCategoryId = 1000474;

    // ============================================================
    // MAIN ORACLE AP PROCESS
    // ============================================================

    public async Task<OracleApProcessResult> ProcessInvoiceAsync(
        OracleApPortalInvoice invoice,
        CancellationToken ct)
    {
        ValidateConfiguration();

        // Keep the identifiers on every step log emitted for this submission.
        // This is especially important when a process dies after Oracle commits
        // but before the PostgreSQL outbox can be finalized.
        using var logScope = logger.BeginScope(
            new Dictionary<string, object?>
            {
                ["InvoiceNumber"] = invoice.InvoiceNumber,
                ["PortalInvoiceId"] = invoice.PortalInvoiceId,
                ["OracleVendorId"] = invoice.VendorId
            });

        logger.LogInformation("Starting Oracle AP submission.");

        var source =
            Get(
                "ORACLE_AP_SOURCE",
                "BUSINESS PARTNER PORTAL"
            )
            .Trim()
            .ToUpperInvariant();

        var batchName =
            $"BUSINESS PARTNER PORTAL - " +
            $"{invoice.InvoiceDate.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture)}";

        batchName =
            batchName.ToUpperInvariant();

        var existingInvoiceId =
            await RunOracleStepAsync(
                "PROCESS.EXISTING_INVOICE_LOOKUP",
                () => FindImportedInvoiceIdAsync(
                    invoice.VendorId,
                    invoice.InvoiceNumber,
                    ct
                )
            );

        if (existingInvoiceId.HasValue)
        {
            return new OracleApProcessResult(
                existingInvoiceId.Value,
                null,
                batchName,
                source,
                Array.Empty<string>()
            );
        }

        // A host can stop after staging the interface row but before the portal
        // records the result.  Never create a second interface row in that case.
        var existingInterface = await GetInterfaceOutcomeAsync(invoice.VendorId, invoice.InvoiceNumber, ct);
        if (existingInterface.InterfaceInvoiceId.HasValue)
        {
            if (string.Equals(existingInterface.InterfaceStatus, "REJECTED", StringComparison.OrdinalIgnoreCase) || existingInterface.Rejections.Count > 0)
                throw new OracleApRejectedException(existingInterface.Rejections.Count > 0 ? existingInterface.Rejections : new[] { "Oracle AP interface status is REJECTED." }, null, existingInterface.InterfaceInvoiceId);

            throw new OracleApPendingException(existingInterface.InterfaceInvoiceId.Value, existingInterface.InterfaceStatus);
        }

        var supplier =
            await RunOracleStepAsync(
                "PROCESS.SUPPLIER_LOOKUP",
                async () =>
                    await oracleReadService
                        .GetSupplierByVendorIdAsync(
                            invoice.VendorId,
                            ct
                        )
            )
            ??
            throw new OracleApBusinessException(
                $"Oracle supplier {invoice.VendorId} was not found."
            );

        if (
            !long.TryParse(
                supplier.VendorSiteId,
                out var vendorSiteId
            )
            ||
            vendorSiteId <= 0
        )
        {
            throw new OracleApBusinessException(
                "A valid Oracle VENDOR_SITE_ID is required for AP invoice import."
            );
        }

        if (
            !long.TryParse(
                supplier.OrgId,
                out var orgId
            )
            ||
            orgId <= 0
        )
        {
            throw new OracleApBusinessException(
                "A valid Oracle ORG_ID is required for AP invoice import."
            );
        }

        var paymentMethod =
            await RunOracleStepAsync(
                "PROCESS.PAYMENT_METHOD_RESOLUTION",
                () => ResolvePaymentMethodAsync(
                    vendorSiteId,
                    orgId,
                    ct
                )
            );

        var receiptLines =
            await RunOracleStepAsync(
                "PROCESS.RECEIPT_LINE_RESOLUTION",
                () => ResolveReceiptLinesAsync(
                    invoice.VendorId,
                    invoice.PoNumber,
                    invoice.GrnNumbers,
                    ct
                )
            );

        if (invoice.RcvTransactionIds.Count > 0)
        {
            receiptLines =
                receiptLines
                    .Where(
                        x =>
                            invoice.RcvTransactionIds
                                .Contains(
                                    x.RcvTransactionId
                                )
                    )
                    .ToList();
        }

        ValidateReceiptLines(
            invoice,
            receiptLines
        );

        var interfaceInvoiceId =
            await RunOracleStepAsync(
                "PROCESS.STAGE_INVOICE",
                () => StageInvoiceAsync(
                    invoice,
                    vendorSiteId,
                    orgId,
                    paymentMethod,
                    source,
                    receiptLines,
                    ct
                )
            );

        var requestId =
            await RunOracleStepAsync(
                "PROCESS.APXIIMPT_SUBMIT",
                () => SubmitPayablesImportAsync(
                    source,
                    batchName,
                    ct
                )
            );

        try
        {
            await RunOracleStepAsync(
                "PROCESS.APXIIMPT_WAIT",
                () => WaitForConcurrentRequestAsync(requestId, ct)
            );
        }
        catch (OracleApBusinessException)
        {
            // An APXIIMPT Error commonly leaves the actionable explanation in
            // AP_INTERFACE_REJECTIONS. Surface it as a permanent rejection
            // instead of retrying an already rejected interface row.
            var rejected = await GetInterfaceRejectionsAsync(interfaceInvoiceId, ct);
            if (rejected.Count > 0)
                throw new OracleApRejectedException(rejected, requestId, interfaceInvoiceId);

            throw;
        }

        var rejections =
            await RunOracleStepAsync(
                "PROCESS.INTERFACE_REJECTIONS_LOOKUP",
                () => GetInterfaceRejectionsAsync(
                    interfaceInvoiceId,
                    ct
                )
            );

        var interfaceStatus =
            await RunOracleStepAsync(
                "PROCESS.INTERFACE_STATUS_LOOKUP",
                () => GetInterfaceStatusAsync(
                    interfaceInvoiceId,
                    ct
                )
            );

        if (
            string.Equals(
                interfaceStatus,
                "REJECTED",
                StringComparison.OrdinalIgnoreCase
            )
            ||
            rejections.Count > 0
        )
        {
            throw new OracleApRejectedException(
                rejections.Count > 0
                    ? rejections
                    : new[]
                    {
                        "Oracle AP interface status is REJECTED."
                    },
                requestId,
                interfaceInvoiceId
            );
        }

        var importedInvoiceId =
            await RunOracleStepAsync(
                "PROCESS.IMPORTED_INVOICE_LOOKUP",
                () => FindImportedInvoiceIdAsync(
                    invoice.VendorId,
                    invoice.InvoiceNumber,
                    ct
                )
            );

        if (!importedInvoiceId.HasValue)
        {
            // Normal completion means the concurrent program ended normally; it
            // does not prove this particular interface row was imported.
            if (!string.IsNullOrWhiteSpace(interfaceStatus))
                throw new OracleApPendingException(interfaceInvoiceId, interfaceStatus);

            throw new OracleApBusinessException(
                "Payables Open Interface Import completed, but the invoice was " +
                "not imported and no remaining interface row was found."
            );
        }

        return new OracleApProcessResult(
            importedInvoiceId.Value,
            requestId,
            batchName,
            source,
            rejections
        );
    }

    // ============================================================
    // STAGE AP_INVOICES_INTERFACE + AP_INVOICE_LINES_INTERFACE
    // ============================================================

    private async Task<long> StageInvoiceAsync(
        OracleApPortalInvoice invoice,
        long vendorSiteId,
        long orgId,
        string? paymentMethod,
        string source,
        IReadOnlyList<OracleReceiptLine> receiptLines,
        CancellationToken ct)
    {
        await using var connection =
            await OpenAsync(ct);

        using var transaction =
            connection.BeginTransaction();

        var goodsReceivedDate =
            await RunOracleStepAsync(
                "STAGE.GOODS_RECEIVED_DATE_RESOLUTION",
                () => ResolveGoodsReceivedDateAsync(
                    connection,
                    transaction,
                    receiptLines,
                    ct
                )
            );

        try
        {
            await RunOracleStepAsync(
                "STAGE.APPS_INITIALIZE",
                async () =>
                {
                    await InitializeAppsContextAsync(
                        connection,
                        transaction,
                        ct
                    );
                }
            );

            await RunOracleStepAsync(
                "STAGE.CLEANUP_PREVIOUS_INTERFACE",
                async () =>
                {
                    await CleanupPreviousInterfaceAttemptAsync(
                        connection,
                        transaction,
                        invoice.VendorId,
                        invoice.InvoiceNumber,
                        source,
                        ct
                    );
                }
            );

            var invoiceId =
                await RunOracleStepAsync(
                    "STAGE.NEXT_HEADER_SEQUENCE",
                    async () =>
                        await NextValueAsync(
                            connection,
                            transaction,
                            "AP_INVOICES_INTERFACE_S",
                            ct
                        )
                );

            await using (
                var command =
                    connection.CreateCommand()
            )
            {
                Configure(command);
                command.Transaction =
                    transaction;

                command.BindByName =
                    true;

                command.CommandText =
                    """
                    INSERT INTO AP_INVOICES_INTERFACE
                    (
                        INVOICE_ID,
                        INVOICE_NUM,
                        VENDOR_ID,
                        VENDOR_SITE_ID,
                        INVOICE_AMOUNT,
                        INVOICE_DATE,
                        GOODS_RECEIVED_DATE,
                        INVOICE_CURRENCY_CODE,
                        TERMS_ID,
                        PAYMENT_METHOD_LOOKUP_CODE,
                        GL_DATE,
                        REQUEST_ID,
                        ORG_ID,
                        SOURCE,
                        GROUP_ID,
                        INVOICE_TYPE_LOOKUP_CODE,
                        DESCRIPTION,
                        CREATION_DATE,
                        CREATED_BY,
                        LAST_UPDATE_DATE,
                        LAST_UPDATED_BY,
                        LAST_UPDATE_LOGIN,
                        ATTRIBUTE_CATEGORY,
                        ATTRIBUTE10,
                        ATTRIBUTE11
                    )
                    VALUES
                    (
                        :invoice_id,
                        :invoice_num,
                        :vendor_id,
                        :vendor_site_id,
                        :invoice_amount,
                        :invoice_date,
                        :goods_received_date,
                        :currency_code,
                        :terms_id,
                        :payment_method,
                        :gl_date,
                        NULL,
                        :org_id,
                        :source,
                        NULL,
                        :invoice_type,
                        :description,
                        SYSDATE,
                        :created_by,
                        SYSDATE,
                        :updated_by,
                        :last_update_login,
                        'BUSINESS PARTNER PORTAL',
                        :pk_id,
                        'Pending'
                    )
                    """;

                Add(command, "invoice_id", OracleDbType.Int64, invoiceId);
                Add(command, "invoice_num", OracleDbType.Varchar2, invoice.InvoiceNumber);
                Add(command, "vendor_id", OracleDbType.Decimal, invoice.VendorId);
                Add(command, "vendor_site_id", OracleDbType.Int64, vendorSiteId);
                Add(command, "invoice_amount", OracleDbType.Decimal, invoice.InvoiceAmount);

                Add(
                    command,
                    "invoice_date",
                    OracleDbType.Date,
                    invoice.InvoiceDate.ToDateTime(TimeOnly.MinValue)
                );

                Add(
                    command,
                    "goods_received_date",
                    OracleDbType.Date,
                    goodsReceivedDate
                );

                Add(command, "currency_code", OracleDbType.Varchar2, invoice.CurrencyCode);

                Add(
                    command,
                    "terms_id",
                    OracleDbType.Int64,
                    GetOptionalLong("ORACLE_AP_TERMS_ID")
                );

                Add(
                    command,
                    "payment_method",
                    OracleDbType.Varchar2,
                    string.IsNullOrWhiteSpace(paymentMethod)
                        ? DBNull.Value
                        : paymentMethod
                );

                Add(
                    command,
                    "gl_date",
                    OracleDbType.Date,
                    invoice.InvoiceDate.ToDateTime(TimeOnly.MinValue)
                );

                Add(command, "org_id", OracleDbType.Int64, orgId);
                Add(command, "source", OracleDbType.Varchar2, source);
                Add(command, "pk_id", OracleDbType.Int64, invoice.PkId);

                Add(
                    command,
                    "invoice_type",
                    OracleDbType.Varchar2,
                    MapInvoiceType(invoice.InvoiceType)
                );

                Add(
                    command,
                    "description",
                    OracleDbType.Varchar2,
                    DbValue(invoice.Description)
                );

                Add(
                    command,
                    "created_by",
                    OracleDbType.Int64,
                    GetLong("ORACLE_APPS_USER_ID")
                );

                Add(
                    command,
                    "updated_by",
                    OracleDbType.Int64,
                    GetLong("ORACLE_APPS_USER_ID")
                );

                Add(
                    command,
                    "last_update_login",
                    OracleDbType.Int64,
                    GetLong("ORACLE_APPS_USER_ID")
                );

                await RunOracleStepAsync(
                    "STAGE.HEADER_INTERFACE_INSERT",
                    async () =>
                    {
                        await command.ExecuteNonQueryAsync(ct);
                    }
                );
            }

            var lineNumber = 1;

            foreach (
                var line in receiptLines
            )
            {
                var lineId =
                    await RunOracleStepAsync(
                        $"STAGE.NEXT_LINE_SEQUENCE.RCV={line.RcvTransactionId}",
                        async () =>
                            await NextValueAsync(
                                connection,
                                transaction,
                                "AP_INVOICE_LINES_INTERFACE_S",
                                ct
                            )
                    );

                await using var lineCommand =
                    connection.CreateCommand();

                Configure(lineCommand);

                lineCommand.Transaction =
                    transaction;

                lineCommand.BindByName =
                    true;

                lineCommand.CommandText =
                    """
                    INSERT INTO AP_INVOICE_LINES_INTERFACE
                    (
                        INVOICE_ID,
                        INVOICE_LINE_ID,
                        LINE_NUMBER,
                        LINE_TYPE_LOOKUP_CODE,
                        AMOUNT,
                        PO_NUMBER,
                        PO_HEADER_ID,
                        PO_LINE_NUMBER,
                        PO_LINE_ID,
                        PO_LINE_LOCATION_ID,
                        PO_DISTRIBUTION_ID,
                        RECEIPT_NUMBER,
                        RCV_TRANSACTION_ID,
                        QUANTITY_INVOICED,
                        UNIT_PRICE,
                        DESCRIPTION,
                        ACCOUNTING_DATE,
                        ORG_ID,
                        CREATION_DATE,
                        CREATED_BY,
                        LAST_UPDATE_DATE,
                        LAST_UPDATED_BY,
                        LAST_UPDATE_LOGIN
                    )
                    VALUES
                    (
                        :invoice_id,
                        :invoice_line_id,
                        :line_number,
                        'ITEM',
                        :amount,
                        :po_number,
                        :po_header_id,
                        :po_line_number,
                        :po_line_id,
                        :po_line_location_id,
                        :po_distribution_id,
                        :receipt_number,
                        :rcv_transaction_id,
                        :quantity_invoiced,
                        :unit_price,
                        :description,
                        :accounting_date,
                        :org_id,
                        SYSDATE,
                        :created_by,
                        SYSDATE,
                        :updated_by,
                        :last_update_login
                    )
                    """;

                Add(lineCommand, "invoice_id", OracleDbType.Int64, invoiceId);
                Add(lineCommand, "invoice_line_id", OracleDbType.Int64, lineId);
                Add(lineCommand, "line_number", OracleDbType.Int32, lineNumber++);
                Add(lineCommand, "amount", OracleDbType.Decimal, line.ExtendedAmount);
                Add(lineCommand, "po_number", OracleDbType.Varchar2, line.PoNumber);
                Add(lineCommand, "po_header_id", OracleDbType.Int64, line.PoHeaderId);
                Add(lineCommand, "po_line_number", OracleDbType.Int32, line.PoLineNumber);
                Add(lineCommand, "po_line_id", OracleDbType.Int64, line.PoLineId);
                Add(lineCommand, "po_line_location_id", OracleDbType.Int64, line.PoLineLocationId);

                Add(
                    lineCommand,
                    "po_distribution_id",
                    OracleDbType.Int64,
                    line.PoDistributionId.HasValue
                        ? line.PoDistributionId.Value
                        : DBNull.Value
                );

                Add(lineCommand, "receipt_number", OracleDbType.Varchar2, line.GrnNumber);
                Add(lineCommand, "rcv_transaction_id", OracleDbType.Int64, line.RcvTransactionId);
                Add(lineCommand, "quantity_invoiced", OracleDbType.Decimal, line.AvailableQuantity);
                Add(lineCommand, "unit_price", OracleDbType.Decimal, line.UnitPrice);

                Add(
                    lineCommand,
                    "description",
                    OracleDbType.Varchar2,
                    DbValue(invoice.Description)
                );

                Add(
                    lineCommand,
                    "accounting_date",
                    OracleDbType.Date,
                    invoice.InvoiceDate.ToDateTime(TimeOnly.MinValue)
                );

                Add(lineCommand, "org_id", OracleDbType.Int64, orgId);
                Add(lineCommand, "created_by", OracleDbType.Int64, GetLong("ORACLE_APPS_USER_ID"));
                Add(lineCommand, "updated_by", OracleDbType.Int64, GetLong("ORACLE_APPS_USER_ID"));
                Add(lineCommand, "last_update_login", OracleDbType.Int64, GetLong("ORACLE_APPS_USER_ID"));

                await RunOracleStepAsync(
                    $"STAGE.LINE_INTERFACE_INSERT.RCV={line.RcvTransactionId}",
                    async () =>
                    {
                        await lineCommand.ExecuteNonQueryAsync(ct);
                    }
                );
            }

            await RunOracleStepAsync(
                "STAGE.TRANSACTION_COMMIT",
                () =>
                {
                    transaction.Commit();
                    return Task.CompletedTask;
                }
            );

            logger.LogInformation(
                "Staged portal invoice {PortalInvoiceId} into Oracle AP interface. " +
                "InterfaceInvoiceId={InterfaceInvoiceId}, " +
                "PkId={PkId}, Source={Source}, GroupId=NULL",
                invoice.PortalInvoiceId,
                invoiceId,
                invoice.PkId,
                source
            );

            return invoiceId;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    // ============================================================
    // GOODS RECEIVED DATE
    // ============================================================

    private async Task<DateTime> ResolveGoodsReceivedDateAsync(
        OracleConnection connection,
        OracleTransaction transaction,
        IReadOnlyList<OracleReceiptLine> receiptLines,
        CancellationToken ct)
    {
        if (receiptLines.Count == 0)
        {
            throw new OracleApBusinessException(
                "At least one Oracle receipt line is required to resolve GOODS_RECEIVED_DATE."
            );
        }

        await using var command =
            connection.CreateCommand();

        Configure(command);
        command.Transaction = transaction;
        command.BindByName = true;

        var transactionParameters =
            new List<string>();

        for (var i = 0; i < receiptLines.Count; i++)
        {
            var name = $"rcv_transaction_id_{i}";
            transactionParameters.Add($":{name}");

            Add(
                command,
                name,
                OracleDbType.Int64,
                receiptLines[i].RcvTransactionId
            );
        }

        command.CommandText =
            $"""
            SELECT MAX(rt.transaction_date)
            FROM rcv_transactions rt
            WHERE rt.transaction_id IN
            (
                {string.Join(",", transactionParameters)}
            )
            """;

        var value =
            await command.ExecuteScalarAsync(ct);

        if (value is null || value == DBNull.Value)
        {
            throw new OracleApBusinessException(
                "Oracle could not resolve GOODS_RECEIVED_DATE from the selected receipt transaction(s)."
            );
        }

        var goodsReceivedDate =
            Convert.ToDateTime(
                value,
                CultureInfo.InvariantCulture
            );

        logger.LogInformation(
            "Resolved Oracle GOODS_RECEIVED_DATE={GoodsReceivedDate} from {ReceiptLineCount} selected receipt line(s).",
            goodsReceivedDate,
            receiptLines.Count
        );

        return goodsReceivedDate;
    }

    // ============================================================
    // PUBLIC RECEIPT LINES
    // ============================================================

    public Task<IReadOnlyList<OracleReceiptLine>>
        GetReceiptLinesForPortalAsync(
            decimal vendorId,
            string poNumber,
            IReadOnlyList<string> grnNumbers,
            CancellationToken ct)
        =>
            ResolveReceiptLinesAsync(
                vendorId,
                poNumber,
                grnNumbers,
                ct
            );

    // ============================================================
    // RESOLVE RECEIPT / GRN TRANSACTIONS
    // ============================================================

    private async Task<IReadOnlyList<OracleReceiptLine>>
        ResolveReceiptLinesAsync(
            decimal vendorId,
            string poNumber,
            IReadOnlyList<string> grnNumbers,
            CancellationToken ct)
    {
        if (grnNumbers.Count == 0)
        {
            throw new OracleApBusinessException(
                "At least one GRN is required for Oracle receipt matching."
            );
        }

        await using var connection =
            await OpenAsync(ct);

        await using var command =
            connection.CreateCommand();

        Configure(command);

        command.BindByName =
            true;

        var grnParameters =
            new List<string>();

        for (
            var i = 0;
            i < grnNumbers.Count;
            i++
        )
        {
            var name =
                $"grn_{i}";

            grnParameters.Add(
                $":{name}"
            );

            Add(
                command,
                name,
                OracleDbType.Varchar2,
                grnNumbers[i]
            );
        }

        /*
         * IMPORTANT COLUMN ORDER
         *
         *  0  transaction_id
         *  1  receipt_num
         *  2  po_header_id
         *  3  po_number
         *  4  po_line_id
         *  5  po_line_number
         *  6  po_line_location_id
         *  7  po_distribution_id
         *  8  shipment_line_id
         *  9  item_id
         * 10  item_description
         * 11  received_quantity
         * 12  available_quantity
         * 13  unit_price
         * 14  match_option
         *
         * Reader mapping below MUST follow this exact order.
         */
        command.CommandText =
            $"""
            SELECT DISTINCT
                rt.transaction_id,
                rsh.receipt_num,
                pha.po_header_id,
                pha.segment1 AS po_number,
                pol.po_line_id,
                pol.line_num AS po_line_number,
                pll.line_location_id AS po_line_location_id,
                rt.po_distribution_id,
                rsl.shipment_line_id,
                rsl.item_id,
                rsl.item_description,

                NVL(
                    rt.quantity,
                    0
                ) AS received_quantity,

                GREATEST(
                    NVL(
                        rt.quantity,
                        0
                    )
                    -
                    NVL(
                        (
                            SELECT
                                SUM(
                                    NVL(
                                        ail.quantity_invoiced,
                                        0
                                    )
                                )
                            FROM
                                ap_invoice_lines_all ail

                            JOIN
                                ap_invoices_all aia
                              ON
                                aia.invoice_id =
                                ail.invoice_id

                            WHERE
                                ail.rcv_transaction_id =
                                rt.transaction_id

                            AND
                                aia.cancelled_date
                                IS NULL
                        ),
                        0
                    ),
                    0
                ) AS available_quantity,

                NVL(
                    pol.unit_price,
                    0
                ) AS unit_price,

                NVL(
                    pll.match_option,
                    'P'
                ) AS match_option

            FROM
                rcv_transactions rt

            JOIN
                rcv_shipment_lines rsl
              ON
                rsl.shipment_line_id =
                rt.shipment_line_id

            JOIN
                rcv_shipment_headers rsh
              ON
                rsh.shipment_header_id =
                rsl.shipment_header_id

            JOIN
                po_headers_all pha
              ON
                pha.po_header_id =
                rsl.po_header_id

            JOIN
                po_lines_all pol
              ON
                pol.po_line_id =
                rsl.po_line_id

            JOIN
                po_line_locations_all pll
              ON
                pll.line_location_id =
                rsl.po_line_location_id

            WHERE
                pha.vendor_id =
                :vendor_id

            AND
                TRIM(
                    pha.segment1
                )
                =
                TRIM(
                    :po_number
                )

            AND
                rsh.receipt_num IN
                (
                    {string.Join(
                        ",",
                        grnParameters
                    )}
                )

            AND
                rt.transaction_type =
                'RECEIVE'

            AND
                rt.transaction_date >=
                :fiscal_window_start

            ORDER BY
                rsh.receipt_num,
                pol.line_num,
                rt.transaction_id
            """;

        Add(
            command,
            "vendor_id",
            OracleDbType.Decimal,
            vendorId
        );

        Add(
            command,
            "po_number",
            OracleDbType.Varchar2,
            poNumber.Trim()
        );

        Add(
            command,
            "fiscal_window_start",
            OracleDbType.Date,
            PakistanFiscalWindow.Start()
        );

        /*
         * Keep one record per RCV transaction.
         * RCV_TRANSACTION_ID is required later for AP receipt matching.
         */
        var result =
            new Dictionary<long, OracleReceiptLine>();

        await using var reader =
            await command.ExecuteReaderAsync(ct);

        while (
            await reader.ReadAsync(ct)
        )
        {
            /*
             * FIX:
             *
             * Previous code was reading columns 2-10 using incorrect
             * ordinals.
             *
             * Example:
             *
             * SELECT ordinal 4 = PO_LINE_ID
             *
             * but old code was reading ordinal 7 as PO_LINE_ID.
             *
             * Ordinal 7 is actually PO_DISTRIBUTION_ID.
             *
             * That caused:
             *
             * "Oracle receipt resolution returned NULL for required
             *  field PO_LINES_ALL.PO_LINE_ID."
             */
            var rcvTransactionId =
                ReadRequiredInt64(
                    reader,
                    0,
                    "RCV_TRANSACTIONS.TRANSACTION_ID"
                );

            var grnNumber =
                Convert.ToString(
                    reader.GetValue(1)
                )
                ?? string.Empty;

            var poHeaderId =
                ReadRequiredInt64(
                    reader,
                    2,
                    "PO_HEADERS_ALL.PO_HEADER_ID"
                );

            var resolvedPoNumber =
                Convert.ToString(
                    reader.GetValue(3)
                )
                ?? string.Empty;

            var poLineId =
                ReadRequiredInt64(
                    reader,
                    4,
                    "PO_LINES_ALL.PO_LINE_ID"
                );

            var poLineNumber =
                ReadRequiredInt32(
                    reader,
                    5,
                    "PO_LINES_ALL.LINE_NUM"
                );

            var poLineLocationId =
                ReadRequiredInt64(
                    reader,
                    6,
                    "PO_LINE_LOCATIONS_ALL.LINE_LOCATION_ID"
                );

            long? poDistributionId =
                reader.IsDBNull(7)
                    ? null
                    : Convert.ToInt64(
                        reader.GetValue(7)
                    );

            var shipmentLineId =
                ReadRequiredInt64(
                    reader,
                    8,
                    "RCV_SHIPMENT_LINES.SHIPMENT_LINE_ID"
                );

            long? itemId =
                reader.IsDBNull(9)
                    ? null
                    : Convert.ToInt64(
                        reader.GetValue(9)
                    );

            var itemDescription =
                reader.IsDBNull(10)
                    ? null
                    : Convert.ToString(
                        reader.GetValue(10)
                    );

            var receivedQuantity =
                ReadRequiredDecimal(
                    reader,
                    11,
                    "RECEIVED_QUANTITY"
                );

            var availableQuantity =
                ReadRequiredDecimal(
                    reader,
                    12,
                    "AVAILABLE_QUANTITY"
                );

            var unitPrice =
                ReadRequiredDecimal(
                    reader,
                    13,
                    "UNIT_PRICE"
                );

            var matchOption =
                reader.IsDBNull(14)
                    ? "P"
                    : Convert.ToString(
                        reader.GetValue(14)
                    )
                    ?? "P";

            var line =
                new OracleReceiptLine(
                    rcvTransactionId,
                    grnNumber,
                    shipmentLineId,
                    itemId,
                    itemDescription,
                    poHeaderId,
                    resolvedPoNumber,
                    poLineId,
                    poLineNumber,
                    poLineLocationId,
                    poDistributionId,
                    receivedQuantity,
                    availableQuantity,
                    unitPrice,
                    matchOption
                );

            if (
                line.AvailableQuantity > 0
                &&
                !result.ContainsKey(
                    line.RcvTransactionId
                )
            )
            {
                result.Add(
                    line.RcvTransactionId,
                    line
                );
            }
        }

        logger.LogInformation(
            "Resolved {Count} eligible Oracle receipt line(s). VendorId={VendorId}, PO={PoNumber}, GRNs={Grns}",
            result.Count,
            vendorId,
            poNumber,
            string.Join(", ", grnNumbers)
        );

        return result.Values.ToList();
    }

    // ============================================================
    // RECEIPT VALIDATION
    // ============================================================

    private void ValidateReceiptLines(
        OracleApPortalInvoice invoice,
        IReadOnlyList<OracleReceiptLine> lines)
    {
        if (lines.Count == 0)
        {
            throw new OracleApBusinessException(
                "The selected GRN(s) do not contain any received quantity available for invoicing."
            );
        }

        var missingGrns =
            invoice.GrnNumbers
                .Where(
                    grn =>
                        !lines.Any(
                            x =>
                                string.Equals(
                                    x.GrnNumber,
                                    grn,
                                    StringComparison.OrdinalIgnoreCase
                                )
                        )
                )
                .ToList();

        if (
            missingGrns.Count > 0
        )
        {
            throw new OracleApBusinessException(
                $"No eligible RECEIVE transaction was found for GRN(s): " +
                $"{string.Join(", ", missingGrns)}."
            );
        }

        var poMatched =
            lines
                .Where(
                    x =>
                        !string.Equals(
                            x.MatchOption,
                            "R",
                            StringComparison.OrdinalIgnoreCase
                        )
                )
                .ToList();

        if (
            poMatched.Count > 0
        )
        {
            throw new OracleApBusinessException(
                "One or more selected PO shipments use MATCH_OPTION='P'. " +
                "The current Business Portal integration is configured " +
                "for GRN/receipt matching and cannot send those lines " +
                "as receipt-matched AP invoice lines."
            );
        }

        /*
         * Multiple receipt lines under the same GRN are valid when the
         * portal has explicitly captured the selected RCV_TRANSACTION_ID
         * values. ProcessInvoiceAsync filters the resolved Oracle lines by
         * invoice.RcvTransactionIds before this validation runs, so the
         * collection here already represents only the vendor-selected lines.
         *
         * Do not reject a GRN merely because more than one selected receipt
         * line belongs to it. The old validation caused valid multi-line GRNs
         * to fail before APXIIMPT was even submitted.
         */

        if (
            lines.Any(
                x =>
                    x.UnitPrice <= 0
            )
        )
        {
            throw new OracleApBusinessException(
                "Oracle returned a zero/invalid unit price for one or more selected receipt lines."
            );
        }

        var calculatedAmount =
            lines.Sum(
                x =>
                    x.ExtendedAmount
            );

        var tolerance =
            GetDecimal(
                "ORACLE_AP_AMOUNT_TOLERANCE",
                1.00m
            );

        var difference =
            Math.Abs(
                invoice.InvoiceAmount -
                calculatedAmount
            );

        if (
            difference >
            tolerance
        )
        {
            throw new OracleApBusinessException(
                $"Invoice amount {invoice.InvoiceAmount:N2} does not match " +
                $"the selected GRN receipt-line value {calculatedAmount:N2}. " +
                $"Difference {difference:N2} exceeds configured tolerance " +
                $"{tolerance:N2}. Line-level allocation is required instead " +
                $"of guessing the split."
            );
        }
    }

    // ============================================================
    // SUBMIT APXIIMPT
    // ============================================================

    private async Task<long> SubmitPayablesImportAsync(
        string source,
        string batchName,
        CancellationToken ct)
    {
        await using var connection =
            await OpenAsync(ct);

        await RunOracleStepAsync(
            "APXIIMPT.APPS_INITIALIZE",
            () => InitializeAppsContextAsync(
                connection,
                null,
                ct
            )
        );

        await using var command =
            connection.CreateCommand();

        Configure(command);

        command.BindByName =
            true;

        command.CommandText =
            """
            BEGIN
                MO_GLOBAL.INIT('SQLAP');
                MO_GLOBAL.SET_POLICY_CONTEXT('S', 82);

                :request_id :=
                    fnd_request.submit_request
                    (
                        application => 'SQLAP',
                        program     => 'APXIIMPT',
                        description => NULL,
                        start_time  => NULL,
                        sub_request => FALSE,

                        argument1   => NULL,
                        argument2   => :source,
                        argument3   => NULL,
                        argument4   => :batch_name,
                        argument5   => NULL,
                        argument6   => NULL,
                        argument7   => NULL,
                        argument8   => 'N',
                        argument9   => NULL,
                        argument10  => NULL,
                        argument11  => 'N',
                        argument12  => NULL,
                        argument13  => NULL,
                        argument14  => NULL,
                        argument15  => NULL
                    );

                COMMIT;

            END;
            """;

        var requestIdParameter =
            new OracleParameter
            {
                ParameterName =
                    "request_id",

                OracleDbType =
                    OracleDbType.Decimal,

                Direction =
                    ParameterDirection.Output
            };

        command.Parameters.Add(
            requestIdParameter
        );

        Add(
            command,
            "source",
            OracleDbType.Varchar2,
            source
                .Trim()
                .ToUpperInvariant()
        );

        Add(
            command,
            "batch_name",
            OracleDbType.Varchar2,
            batchName
                .Trim()
                .ToUpperInvariant()
        );

        await RunOracleStepAsync(
            "APXIIMPT.SUBMIT_REQUEST",
            () => command.ExecuteNonQueryAsync(ct)
        );

        var raw =
            requestIdParameter
                .Value?
                .ToString();

        if (
            !long.TryParse(
                raw,
                out var requestId
            )
            ||
            requestId <= 0
        )
        {
            throw new OracleApBusinessException(
                "FND_REQUEST.SUBMIT_REQUEST did not return a valid " +
                "concurrent request ID. Confirm APXIIMPT arguments " +
                "and EBS responsibility context."
            );
        }

        logger.LogInformation(
            "Submitted APXIIMPT. " +
            "RequestId={RequestId}, " +
            "Source={Source}, " +
            "GroupId=NULL, " +
            "BatchName={BatchName}",
            requestId,
            source,
            batchName
        );

        return requestId;
    }

    // ============================================================
    // POLL FND_CONCURRENT_REQUESTS
    // ============================================================

    private async Task WaitForConcurrentRequestAsync(
        long requestId,
        CancellationToken ct)
    {
        var pollSeconds =
            GetInt(
                "ORACLE_AP_POLL_SECONDS",
                5
            );

        var timeoutSeconds =
            GetInt(
                "ORACLE_AP_TIMEOUT_SECONDS",
                300
            );

        var deadline =
            DateTimeOffset.UtcNow
                .AddSeconds(
                    timeoutSeconds
                );

        var polls = 0;
        while (DateTimeOffset.UtcNow < deadline)
        {
            polls++;
            await using var connection =
                await OpenAsync(ct);

            await using var command =
                connection.CreateCommand();

            Configure(command);

            command.BindByName =
                true;

            command.CommandText =
                """
                SELECT
                    phase_code,
                    status_code

                FROM
                    fnd_concurrent_requests

                WHERE
                    request_id =
                    :request_id
                """;

            Add(
                command,
                "request_id",
                OracleDbType.Int64,
                requestId
            );

            await using var reader =
                await command.ExecuteReaderAsync(ct);

            if (
                await reader.ReadAsync(ct)
            )
            {
                var phase =
                    Convert.ToString(
                        reader.GetValue(0)
                    )
                    ?? string.Empty;

                var status =
                    Convert.ToString(
                        reader.GetValue(1)
                    )
                    ?? string.Empty;

                if (
                    string.Equals(
                        phase,
                        "C",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    if (
                        !string.Equals(
                            status,
                            "C",
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        throw new OracleApBusinessException(
                            $"APXIIMPT request {requestId} completed " +
                            $"with Oracle status code '{status}', " +
                            "not Normal/Complete."
                        );
                    }

                    return;
                }

                if (string.Equals(phase, "C", StringComparison.OrdinalIgnoreCase))
                {
                    // Explicitly name the EBS terminal states in the error.
                    // C/E = Error, C/X = Cancelled, and C/T = Terminated.
                    throw new OracleApBusinessException(
                        $"APXIIMPT request {requestId} finished in terminal " +
                        $"state phase '{phase}', status '{status}' (poll {polls}).");
                }
            }

            await Task.Delay(
                TimeSpan.FromSeconds(
                    pollSeconds
                ),
                ct
            );
        }

        throw new TimeoutException(
            $"Timed out waiting for APXIIMPT request {requestId} " +
            $"after {timeoutSeconds} seconds."
        );
    }

    // ============================================================
    // AP_INTERFACE_REJECTIONS
    // ============================================================

    public async Task<OracleApInterfaceOutcome> GetInterfaceOutcomeAsync(decimal vendorId, string invoiceNumber, CancellationToken ct)
    {
        var importedInvoiceId = await FindImportedInvoiceIdAsync(vendorId, invoiceNumber, ct);
        if (importedInvoiceId.HasValue)
            return new OracleApInterfaceOutcome(importedInvoiceId, null, null, Array.Empty<string>());

        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();

        Configure(command);
        command.BindByName = true;
        command.CommandText = """
            SELECT invoice_id, status
            FROM ap_invoices_interface
            WHERE vendor_id = :vendor_id
              AND UPPER(TRIM(invoice_num)) = UPPER(TRIM(:invoice_num))
              AND ROWNUM = 1
            """;
        Add(command, "vendor_id", OracleDbType.Decimal, vendorId);
        Add(command, "invoice_num", OracleDbType.Varchar2, invoiceNumber);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return new OracleApInterfaceOutcome(null, null, null, Array.Empty<string>());

        var interfaceInvoiceId = Convert.ToInt64(reader.GetValue(0));
        var interfaceStatus = Convert.ToString(reader.GetValue(1))?.Trim();
        var rejections = await GetInterfaceRejectionsAsync(interfaceInvoiceId, ct);
        logger.LogInformation("Oracle AP outcome resolved. InvoiceNumber={InvoiceNumber} InterfaceInvoiceId={InterfaceInvoiceId} InterfaceStatus={InterfaceStatus} RejectionReason={RejectionReason}", invoiceNumber, interfaceInvoiceId, interfaceStatus ?? "<null>", rejections.Count == 0 ? "<none>" : string.Join("; ", rejections));
        return new OracleApInterfaceOutcome(null, interfaceInvoiceId, interfaceStatus, rejections);
    }

    private async Task<string?> GetInterfaceStatusAsync(
        long interfaceInvoiceId,
        CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();

        Configure(command);

        command.BindByName = true;
        command.CommandText =
            """
            SELECT status
            FROM ap_invoices_interface
            WHERE invoice_id = :invoice_id
            """;

        Add(command, "invoice_id", OracleDbType.Int64, interfaceInvoiceId);

        var value = await command.ExecuteScalarAsync(ct);
        var status = value is null || value == DBNull.Value
            ? null
            : Convert.ToString(value)?.Trim();

        logger.LogInformation(
            "Oracle AP interface status resolved. InterfaceInvoiceId={InterfaceInvoiceId} Status={InterfaceStatus}",
            interfaceInvoiceId,
            status ?? "<null>");

        return status;
    }

    // ============================================================
    // PAYMENT METHOD RESOLUTION
    // ============================================================

    private async Task<string?> ResolvePaymentMethodAsync(
        long vendorSiteId,
        long orgId,
        CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();

        Configure(command);

        command.BindByName = true;
        command.CommandText =
            """
            SELECT payment_method_code
            FROM
            (
                SELECT
                    COALESCE(
                        NULLIF(TRIM(pvs.payment_method_lookup_code), ''),
                        NULLIF(TRIM(iep.default_payment_method_code), '')
                    ) AS payment_method_code
                FROM po_vendor_sites_all pvs
                LEFT JOIN iby_external_payees_v iep
                    ON iep.supplier_site_id = pvs.vendor_site_id
                    AND iep.org_id = pvs.org_id
                    AND iep.payment_function = 'PAYABLES_DISBURSEMENTS'
                    AND iep.inactive_date IS NULL
                WHERE pvs.vendor_site_id = :vendor_site_id
                  AND pvs.org_id = :org_id
            ) candidate
            WHERE payment_method_code IS NOT NULL
              AND EXISTS
              (
                  SELECT 1
                  FROM iby_payment_methods_vl method
                  WHERE method.payment_method_code = candidate.payment_method_code
                    AND method.inactive_date IS NULL
              )
            """;

        Add(command, "vendor_site_id", OracleDbType.Int64, vendorSiteId);
        Add(command, "org_id", OracleDbType.Int64, orgId);

        var value = await command.ExecuteScalarAsync(ct);
        var paymentMethod = value is null || value == DBNull.Value
            ? null
            : Convert.ToString(value)?.Trim();

        if (string.IsNullOrWhiteSpace(paymentMethod))
        {
            logger.LogWarning(
                "No valid Oracle payment method resolved. VendorSiteId={VendorSiteId} OrgId={OrgId}. " +
                "PAYMENT_METHOD_LOOKUP_CODE will be sent as NULL so Oracle AP can apply its configured defaults.",
                vendorSiteId,
                orgId
            );

            return null;
        }

        logger.LogInformation(
            "Oracle payment method resolved. VendorSiteId={VendorSiteId} OrgId={OrgId} PaymentMethod={PaymentMethod}",
            vendorSiteId,
            orgId,
            paymentMethod);

        return paymentMethod;
    }

    private async Task<IReadOnlyList<string>>
        GetInterfaceRejectionsAsync(
            long interfaceInvoiceId,
            CancellationToken ct)
    {
        await using var connection =
            await OpenAsync(ct);

        await using var command =
            connection.CreateCommand();

        Configure(command);

        command.BindByName =
            true;

        command.CommandText =
            """
            SELECT DISTINCT
                NVL(
                    reject_lookup_code,
                    'UNKNOWN'
                ) AS rejection_code

            FROM
                ap_interface_rejections

            WHERE
                parent_id =
                :invoice_id

            OR
                parent_id IN
                (
                    SELECT
                        invoice_line_id

                    FROM
                        ap_invoice_lines_interface

                    WHERE
                        invoice_id =
                        :invoice_id
                )

            ORDER BY
                1
            """;

        Add(
            command,
            "invoice_id",
            OracleDbType.Int64,
            interfaceInvoiceId
        );

        var result =
            new List<string>();

        await using var reader =
            await command.ExecuteReaderAsync(ct);

        while (
            await reader.ReadAsync(ct)
        )
        {
            result.Add(
                Convert.ToString(
                    reader.GetValue(0)
                )
                ??
                "UNKNOWN"
            );
        }

        return result;
    }

    // ============================================================
    // VERIFY AP_INVOICES_ALL
    // ============================================================

    private async Task<long?> FindImportedInvoiceIdAsync(
        decimal vendorId,
        string invoiceNumber,
        CancellationToken ct)
    {
        await using var connection =
            await OpenAsync(ct);

        await using var command =
            connection.CreateCommand();

        Configure(command);

        command.BindByName =
            true;

        command.CommandText =
            """
            SELECT
                invoice_id

            FROM
                ap_invoices_all

            WHERE
                vendor_id =
                :vendor_id

            AND
                UPPER(
                    TRIM(
                        invoice_num
                    )
                )
                =
                UPPER(
                    TRIM(
                        :invoice_num
                    )
                )

            AND
                cancelled_date
                IS NULL

            AND
                ROWNUM =
                1
            """;

        Add(
            command,
            "vendor_id",
            OracleDbType.Decimal,
            vendorId
        );

        Add(
            command,
            "invoice_num",
            OracleDbType.Varchar2,
            invoiceNumber
        );

        var result =
            await command.ExecuteScalarAsync(ct);

        if (
            result is null ||
            result ==
            DBNull.Value
        )
        {
            return null;
        }

        return Convert.ToInt64(
            result
        );
    }

    // ============================================================
    // FND ATTACHMENTS
    // ============================================================

    public async Task AttachDocumentsToInvoiceAsync(
        long oracleInvoiceId,
        IReadOnlyList<OracleApDocument> documents,
        CancellationToken ct)
    {
        if (
            documents.Count ==
            0
        )
        {
            return;
        }

        await using var connection =
            await OpenAsync(ct);

        using var transaction =
            connection.BeginTransaction();

        var verificationTargets = new List<(OracleApDocument Document, OracleApAttachmentMetadata Metadata)>();

        try
        {
            await InitializeAppsContextAsync(
                connection,
                transaction,
                ct
            );

            foreach (
                var document in documents
            )
            {
                var category =
                    document
                        .DocumentType
                        .ToUpperInvariant()
                    switch
                    {
                        "INVOICE" =>
                            Get(
                                "ORACLE_AP_INVOICE_COPY_CATEGORY",
                                "Quick Invoices"
                            ),

                        "DELIVERY_CHALLAN" =>
                            Get(
                                "ORACLE_AP_DELIVERY_CHALLAN_CATEGORY",
                                "Quick Invoices"
                            ),

                        _ =>
                            Get(
                                "ORACLE_AP_SUPPORTING_DOCUMENT_CATEGORY",
                                "Quick Invoices"
                            )
                    };

                var metadata =
                    await ResolveAttachmentMetadataAsync(
                        connection,
                        transaction,
                        oracleInvoiceId,
                        category,
                        ct
                    );

                var exists =
                    await AttachmentExistsAsync(
                        connection,
                        transaction,
                        oracleInvoiceId,
                        document.FileName,
                        metadata.CategoryId,
                        ct
                    );

                if (exists)
                {
                    logger.LogInformation("Existing Oracle AP attachment recovered. OracleInvoiceId={OracleInvoiceId}, FileName={FileName}, CategoryId={CategoryId}", oracleInvoiceId, document.FileName, metadata.CategoryId);
                    verificationTargets.Add((document, metadata));
                    continue;
                }

                logger.LogInformation(
                    "Creating Oracle AP invoice attachment. " +
                    "OracleInvoiceId={OracleInvoiceId}, FileName={FileName}, " +
                    "DocumentType={DocumentType}, ConfiguredCategory={ConfiguredCategory}, ResolvedCategoryId={ResolvedCategoryId}, " +
                    "ResolvedInternalName={ResolvedInternalName}, ResolvedUserName={ResolvedUserName}, " +
                    "AttachmentFunctionId={AttachmentFunctionId}, AttachmentFunctionName={AttachmentFunctionName}, " +
                    "AttachmentFunctionType={AttachmentFunctionType}, SecurityType={SecurityType}, SecurityId={SecurityId}, DatatypeId={DatatypeId}",
                    oracleInvoiceId,
                    document.FileName,
                    document.DocumentType,
                    metadata.ConfiguredCategory,
                    metadata.CategoryId, metadata.InternalName, metadata.UserName,
                    metadata.AttachmentFunctionId, metadata.AttachmentFunctionName, metadata.AttachmentFunctionType,
                    metadata.SecurityType,
                    metadata.SecurityId,
                    metadata.DatatypeId
                );

                await AttachSingleFileAsync(
                    connection,
                    transaction,
                    oracleInvoiceId,
                    document,
                    metadata,
                    ct
                );
                verificationTargets.Add((document, metadata));
            }

            transaction.Commit();
            logger.LogInformation("Oracle AP attachment creation transaction committed. OracleInvoiceId={OracleInvoiceId}", oracleInvoiceId);
        }
        catch
        {
            transaction.Rollback();
            throw;
        }

        // AOL package operations can use parallel/direct-path internals. Do
        // not read their modified objects until after the creating transaction
        // commits; ORA-12838 otherwise prevents the read and masks success.
        foreach (var target in verificationTargets)
        {
            logger.LogInformation("Starting post-commit Forms attachment verification. OracleInvoiceId={OracleInvoiceId}, FileName={FileName}", oracleInvoiceId, target.Document.FileName);
            await VerifyAttachmentAfterCommitAsync(oracleInvoiceId, target.Document, target.Metadata, ct);
            logger.LogInformation("Post-commit Forms attachment verification succeeded. OracleInvoiceId={OracleInvoiceId}, FileName={FileName}", oracleInvoiceId, target.Document.FileName);
        }
    }

    private async Task<bool> AttachmentExistsAsync(
        OracleConnection connection,
        OracleTransaction transaction,
        long oracleInvoiceId,
        string fileName,
        long categoryId,
        CancellationToken ct)
    {
        await using var command =
            connection.CreateCommand();

        Configure(command);

        command.Transaction =
            transaction;

        command.BindByName =
            true;

        command.CommandText =
            """
            SELECT
                COUNT(*)

            FROM
                fnd_attached_documents fad

            JOIN
                fnd_documents_tl fdt
              ON
                fdt.document_id =
                fad.document_id

            WHERE
                fad.entity_name =
                'AP_INVOICES'

            AND
                fad.pk1_value =
                TO_CHAR(
                    :invoice_id
                )

            AND
                UPPER(
                    NVL(
                        fdt.file_name,
                        ''
                    )
                )
                =
                UPPER(
                    :file_name
                )

            AND fad.category_id = :category_id

            AND
                fdt.language =
                USERENV(
                    'LANG'
                )
            """;

        Add(
            command,
            "invoice_id",
            OracleDbType.Int64,
            oracleInvoiceId
        );

        Add(
            command,
            "file_name",
            OracleDbType.Varchar2,
            fileName
        );

        Add(
            command,
            "category_id",
            OracleDbType.Int64,
            categoryId
        );

        return
            Convert.ToInt32(
                await command.ExecuteScalarAsync(ct)
            )
            >
            0;
    }

    private async Task VerifyAttachmentAfterCommitAsync(
        long oracleInvoiceId,
        OracleApDocument document,
        OracleApAttachmentMetadata metadata,
        CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        Configure(command);
        command.BindByName = true;
        command.CommandText =
            """
            SELECT fad.attached_document_id, fd.document_id, fd.media_id,
                   fd.file_name, fdt.language, fdt.source_lang,
                   fdfv.file_name
              FROM fnd_attached_documents fad
              JOIN fnd_documents fd ON fd.document_id = fad.document_id
              JOIN fnd_documents_tl fdt ON fdt.document_id = fd.document_id
              JOIN fnd_attached_docs_form_vl fdfv
                ON fdfv.attached_document_id = fad.attached_document_id
             WHERE fad.entity_name = 'AP_INVOICES'
               AND fad.pk1_value = TO_CHAR(:invoice_id)
               AND fad.category_id = :category_id
               AND UPPER(fd.file_name) = UPPER(:file_name)
               AND fd.datatype_id = :datatype_id
               AND fd.security_type = :security_type
               AND NVL(fd.security_id, -1) = NVL(:security_id, -1)
               AND fdt.language = :language
               AND fdfv.function_name = :function_name
               AND fdfv.function_type = :function_type
               AND fdfv.entity_name = 'AP_INVOICES'
               AND fdfv.pk1_value = TO_CHAR(:invoice_id)
               AND fdfv.category_id = :category_id
               AND fdfv.datatype_id = :datatype_id
               AND fdfv.security_type = :security_type
               AND NVL(fdfv.security_id, -1) = NVL(:security_id, -1)
               AND UPPER(fdfv.file_name) = UPPER(:file_name)
            """;
        Add(command, "invoice_id", OracleDbType.Int64, oracleInvoiceId);
        Add(command, "category_id", OracleDbType.Int64, metadata.CategoryId);
        Add(command, "file_name", OracleDbType.Varchar2, document.FileName);
        Add(command, "datatype_id", OracleDbType.Int32, metadata.DatatypeId);
        Add(command, "security_type", OracleDbType.Int32, metadata.SecurityType);
        Add(command, "security_id", OracleDbType.Int64, metadata.SecurityId);
        Add(command, "language", OracleDbType.Varchar2, metadata.Language);
        Add(command, "function_name", OracleDbType.Varchar2, metadata.AttachmentFunctionName);
        Add(command, "function_type", OracleDbType.Varchar2, metadata.AttachmentFunctionType);

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            throw new OracleApBusinessException(
                $"Oracle AP attachment verification failed after commit for invoice {oracleInvoiceId}, file '{document.FileName}'. " +
                "A matching APXINWKB Forms-view attachment was not found; retry will reconcile the existing attachment without inserting a duplicate.");

        logger.LogInformation(
            "Oracle AP attachment verification details. OracleInvoiceId={OracleInvoiceId}, FileName={FileName}, AttachedDocumentId={AttachedDocumentId}, DocumentId={DocumentId}, MediaId={MediaId}, DocumentFileName={DocumentFileName}, Language={Language}, SourceLanguage={SourceLanguage}, FormsFileName={FormsFileName}",
            oracleInvoiceId, document.FileName, reader.GetValue(0), reader.GetValue(1), reader.GetValue(2), reader.GetValue(3), reader.GetValue(4), reader.GetValue(5), reader.GetValue(6));
    }

    // The paperclip window does not display every FND_ATTACHED_DOCUMENTS row.
    // It only displays categories enabled on the attachment function/block that
    // owns AP_INVOICES.  Resolve that setup instead of guessing category,
    // datatype, or security values from an unrelated historical attachment.
    private async Task<OracleApAttachmentMetadata> ResolveAttachmentMetadataAsync(
        OracleConnection connection,
        OracleTransaction transaction,
        long oracleInvoiceId,
        string requestedCategory,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        Configure(command);
        command.Transaction = transaction;
        command.BindByName = true;
        command.CommandText =
            """
            SELECT DISTINCT
                fdc.category_id,
                fdc.name,
                fdc.user_name,
                faf.attachment_function_id,
                faf.function_name,
                faf.function_type,
                fab.security_type,
                ai.org_id,
                ai.set_of_books_id
            FROM fnd_attachment_functions faf
            JOIN fnd_attachment_blocks fab
              ON fab.attachment_function_id = faf.attachment_function_id
            JOIN fnd_attachment_blk_entities fabe
              ON fabe.attachment_blk_id = fab.attachment_blk_id
            JOIN fnd_doc_category_usages fdcu
              ON fdcu.attachment_function_id = faf.attachment_function_id
            JOIN fnd_document_categories_vl fdc
              ON fdc.category_id = fdcu.category_id
            JOIN ap_invoices_all ai
              ON ai.invoice_id = :invoice_id
            WHERE faf.function_name = :function_name
              AND faf.function_type = :function_type
              AND faf.enabled_flag = 'Y'
              AND fabe.data_object_code = 'AP_INVOICES'
              AND fabe.query_permission_type <> 'N'
              AND fdcu.enabled_flag = 'Y'
              AND NVL(fdc.start_date_active, TRUNC(SYSDATE)) <= TRUNC(SYSDATE)
              AND (fdc.end_date_active IS NULL OR fdc.end_date_active >= TRUNC(SYSDATE))
              AND fdc.category_id = :category_id
            """;

        Add(command, "invoice_id", OracleDbType.Int64, oracleInvoiceId);
        Add(command, "function_name", OracleDbType.Varchar2,
            Get("ORACLE_AP_INVOICE_ATTACHMENT_FUNCTION", "APXINWKB"));
        Add(command, "function_type", OracleDbType.Varchar2,
            Get("ORACLE_AP_INVOICE_ATTACHMENT_FUNCTION_TYPE", "O"));
        Add(command, "category_id", OracleDbType.Int64, OracleApQuickInvoicesCategoryId);

        await using var reader = await command.ExecuteReaderAsync(ct);
        var matches = new List<(long CategoryId, string InternalName, string UserName, long AttachmentFunctionId, string AttachmentFunctionName, string AttachmentFunctionType, int SecurityType, long? OrgId, long? SetOfBooksId)>();
        while (await reader.ReadAsync(ct))
        {
            matches.Add((
                Convert.ToInt64(reader.GetValue(0)),
                reader.GetString(1),
                reader.GetString(2),
                Convert.ToInt64(reader.GetValue(3)),
                reader.GetString(4),
                reader.GetString(5),
                Convert.ToInt32(reader.GetValue(6)),
                reader.IsDBNull(7) ? null : Convert.ToInt64(reader.GetValue(7)),
                reader.IsDBNull(8) ? null : Convert.ToInt64(reader.GetValue(8))));
        }

        if (matches.Count == 0)
            throw new OracleApBusinessException(
                $"Oracle attachment category ID {OracleApQuickInvoicesCategoryId} (Quick Invoices) is not enabled for AP_INVOICES on attachment function " +
                $"'{Get("ORACLE_AP_INVOICE_ATTACHMENT_FUNCTION", "APXINWKB")}'. Configure this category for the Payables Invoice Workbench.");

        var match = matches[0];
        if (matches.Any(x => x.CategoryId != match.CategoryId || x.SecurityType != match.SecurityType))
            throw new OracleApBusinessException(
                $"Oracle attachment configuration for AP_INVOICES category ID {OracleApQuickInvoicesCategoryId} (Quick Invoices) is ambiguous. " +
                "Resolve the duplicate enabled attachment-function/block setup before retrying.");

        var fileDatatypeId = await ResolveFileDatatypeIdAsync(connection, transaction, ct);

        long? securityId = match.SecurityType switch
        {
            1 => match.OrgId,
            2 => match.SetOfBooksId,
            3 => GetOptionalLong("ORACLE_AP_INVOICE_ATTACHMENT_SECURITY_ID"),
            4 => null,
            _ => throw new OracleApBusinessException(
                $"Unsupported AP_INVOICES attachment security type {match.SecurityType} on the configured Invoice Workbench attachment block.")
        };

        if (match.SecurityType != 4 && !securityId.HasValue)
            throw new OracleApBusinessException(
                $"AP_INVOICES attachment security type {match.SecurityType} requires a security ID, but none could be derived for Oracle invoice {oracleInvoiceId}. " +
                "For business-unit security, configure ORACLE_AP_INVOICE_ATTACHMENT_SECURITY_ID.");

        return new OracleApAttachmentMetadata(
            requestedCategory,
            match.CategoryId,
            match.InternalName,
            match.UserName,
            match.AttachmentFunctionId,
            match.AttachmentFunctionName,
            match.AttachmentFunctionType,
            fileDatatypeId,
            match.SecurityType,
            securityId,
            Get("ORACLE_AP_ATTACHMENT_LANGUAGE", "US").ToUpperInvariant());
    }

    // FILE is the AOL document datatype for a binary FND_LOBS payload. It is
    // configured independently from categories in FND_DOCUMENT_DATATYPES.
    private async Task<int> ResolveFileDatatypeIdAsync(
        OracleConnection connection,
        OracleTransaction transaction,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        Configure(command);
        command.Transaction = transaction;
        command.BindByName = true;
        command.CommandText =
            """
            SELECT datatype_id
              FROM fnd_document_datatypes
             WHERE UPPER(name) = 'FILE'
               AND NVL(start_date_active, TRUNC(SYSDATE)) <= TRUNC(SYSDATE)
               AND (end_date_active IS NULL OR end_date_active >= TRUNC(SYSDATE))
            """;

        var datatypeIds = new List<int>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            datatypeIds.Add(Convert.ToInt32(reader.GetValue(0)));

        if (datatypeIds.Count != 1)
            throw new OracleApBusinessException(
                $"Expected exactly one active Oracle AOL FILE document datatype; found {datatypeIds.Count}. " +
                "Correct FND_DOCUMENT_DATATYPES setup before retrying attachment synchronization.");

        return datatypeIds[0];
    }

    private async Task AttachSingleFileAsync(
        OracleConnection connection,
        OracleTransaction transaction,
        long oracleInvoiceId,
        OracleApDocument document,
        OracleApAttachmentMetadata metadata,
        CancellationToken ct)
    {
        var fileFormat =
            Get(
                "ORACLE_AP_ATTACHMENT_FILE_FORMAT",
                "binary"
            );

        await using var command =
            connection.CreateCommand();

        Configure(command);

        command.Transaction =
            transaction;

        command.BindByName =
            true;

        command.CommandText =
            """
            DECLARE
                l_media_id NUMBER;
                l_doc_id NUMBER;
                l_seq_num NUMBER;
                l_rowid VARCHAR2(64);
                l_attached_document_id NUMBER;

            BEGIN
                l_media_id :=
                    fnd_lobs_s.NEXTVAL;

                INSERT INTO fnd_lobs
                (
                    file_id,
                    file_name,
                    file_content_type,
                    file_format,
                    file_data,
                    upload_date,
                    expiration_date,
                    program_name,
                    program_tag,
                    language,
                    oracle_charset
                )
                VALUES
                (
                    l_media_id,
                    :p_file_name,
                    :p_content_type,
                    :p_file_format,
                    :p_blob_data,
                    SYSDATE,
                    NULL,
                    'BUSINESS_PORTAL',
                    'AP_INVOICE_ATTACH',
                    :p_language,
                    'UTF8'
                );

                SELECT
                    NVL(MAX(seq_num), 0) + 1
                INTO
                    l_seq_num
                FROM
                    fnd_attached_documents
                WHERE
                    entity_name = 'AP_INVOICES'
                AND
                    pk1_value = TO_CHAR(:p_invoice_id);

                l_doc_id := fnd_documents_s.NEXTVAL;
                l_attached_document_id := fnd_attached_documents_s.NEXTVAL;

                fnd_documents_pkg.insert_row(
                    x_rowid => l_rowid,
                    x_document_id => l_doc_id,
                    x_creation_date => SYSDATE,
                    x_created_by => fnd_global.user_id,
                    x_last_update_date => SYSDATE,
                    x_last_updated_by => fnd_global.user_id,
                    x_last_update_login => fnd_global.login_id,
                    x_datatype_id => :p_datatype_id,
                    x_category_id => :p_category_id,
                    x_security_type => :p_security_type,
                    x_security_id => :p_security_id,
                    x_publish_flag => 'Y',
                    x_usage_type => 'O',
                    x_language => :p_language,
                    x_description => :p_description,
                    x_file_name => :p_file_name,
                    x_media_id => l_media_id,
                    x_title => :p_title);

                fnd_documents_pkg.add_language;

                fnd_attached_documents_pkg.insert_row(
                    x_rowid => l_rowid,
                    x_attached_document_id => l_attached_document_id,
                    x_document_id => l_doc_id,
                    x_creation_date => SYSDATE,
                    x_created_by => fnd_global.user_id,
                    x_last_update_date => SYSDATE,
                    x_last_updated_by => fnd_global.user_id,
                    x_last_update_login => fnd_global.login_id,
                    x_seq_num => l_seq_num,
                    x_entity_name => 'AP_INVOICES',
                    x_column1 => NULL,
                    x_pk1_value => TO_CHAR(:p_invoice_id),
                    x_pk2_value => NULL,
                    x_pk3_value => NULL,
                    x_pk4_value => NULL,
                    x_pk5_value => NULL,
                    x_automatically_added_flag => 'N',
                    x_datatype_id => :p_datatype_id,
                    x_category_id => :p_category_id,
                    x_security_type => :p_security_type,
                    x_security_id => :p_security_id,
                    x_publish_flag => 'Y',
                    x_usage_type => 'O',
                    x_language => :p_language,
                    x_description => :p_description,
                    x_file_name => :p_file_name,
                    x_media_id => l_media_id,
                    x_doc_attribute_category => NULL,
                    x_doc_attribute1 => NULL,
                    x_doc_attribute2 => NULL,
                    x_doc_attribute3 => NULL,
                    x_doc_attribute4 => NULL,
                    x_doc_attribute5 => NULL,
                    x_doc_attribute6 => NULL,
                    x_doc_attribute7 => NULL,
                    x_doc_attribute8 => NULL,
                    x_doc_attribute9 => NULL,
                    x_doc_attribute10 => NULL,
                    x_doc_attribute11 => NULL,
                    x_doc_attribute12 => NULL,
                    x_doc_attribute13 => NULL,
                    x_doc_attribute14 => NULL,
                    x_doc_attribute15 => NULL,
                    x_create_doc => 'N',
                    x_title => :p_title);

                :p_media_id := l_media_id;
                :p_document_id := l_doc_id;
                :p_attached_document_id := l_attached_document_id;

            END;
            """;

        Add(command, "p_file_name", OracleDbType.Varchar2, document.FileName);
        Add(command, "p_content_type", OracleDbType.Varchar2, document.ContentType);
        Add(command, "p_file_format", OracleDbType.Varchar2, fileFormat);
        Add(command, "p_blob_data", OracleDbType.Blob, document.Content);
        Add(command, "p_category_id", OracleDbType.Int64, metadata.CategoryId);
        Add(command, "p_datatype_id", OracleDbType.Int32, metadata.DatatypeId);
        Add(command, "p_security_type", OracleDbType.Int32, metadata.SecurityType);
        Add(command, "p_security_id", OracleDbType.Int64, metadata.SecurityId);
        Add(command, "p_language", OracleDbType.Varchar2, metadata.Language);

        Add(
            command,
            "p_description",
            OracleDbType.Varchar2,
            document.DocumentType == "INVOICE"
                ? "Business Portal Invoice Copy"
                : document.DocumentType == "DELIVERY_CHALLAN"
                    ? "Business Portal Receipted Delivery Challan"
                    : "Business Portal Supporting Document"
        );

        Add(
            command,
            "p_title",
            OracleDbType.Varchar2,
            document.DocumentType == "INVOICE"
                ? "Invoice"
                : document.DocumentType == "DELIVERY_CHALLAN"
                    ? "Delivery Challan"
                    : "Supporting Document"
        );

        Add(command, "p_invoice_id", OracleDbType.Int64, oracleInvoiceId);
        var mediaId = command.Parameters.Add("p_media_id", OracleDbType.Int64, ParameterDirection.Output);
        var documentId = command.Parameters.Add("p_document_id", OracleDbType.Int64, ParameterDirection.Output);
        var attachedDocumentId = command.Parameters.Add("p_attached_document_id", OracleDbType.Int64, ParameterDirection.Output);

        logger.LogInformation(
            "Inserting Oracle FND_LOBS attachment. " +
            "OracleInvoiceId={OracleInvoiceId}, FileName={FileName}, " +
            "ContentType={ContentType}, FileFormat={FileFormat}, ConfiguredCategory={ConfiguredCategory}, ResolvedCategoryId={ResolvedCategoryId}, " +
            "ResolvedInternalName={ResolvedInternalName}, ResolvedUserName={ResolvedUserName}, AttachmentFunctionId={AttachmentFunctionId}, " +
            "AttachmentFunctionName={AttachmentFunctionName}, AttachmentFunctionType={AttachmentFunctionType}, SecurityType={SecurityType}, SecurityId={SecurityId}, DatatypeId={DatatypeId}",
            oracleInvoiceId,
            document.FileName,
            document.ContentType,
            fileFormat,
            metadata.ConfiguredCategory, metadata.CategoryId, metadata.InternalName, metadata.UserName,
            metadata.AttachmentFunctionId, metadata.AttachmentFunctionName, metadata.AttachmentFunctionType,
            metadata.SecurityType,
            metadata.SecurityId,
            metadata.DatatypeId
        );

        await command.ExecuteNonQueryAsync(ct);

        logger.LogInformation(
            "Oracle AP invoice attachment created. OracleInvoiceId={OracleInvoiceId}, DocumentType={DocumentType}, FileName={FileName}, TLFileName={TLFileName}, Language={Language}, ContentType={ContentType}, ConfiguredCategory={ConfiguredCategory}, ResolvedCategoryId={ResolvedCategoryId}, ResolvedInternalName={ResolvedInternalName}, ResolvedUserName={ResolvedUserName}, AttachmentFunctionId={AttachmentFunctionId}, AttachmentFunctionName={AttachmentFunctionName}, AttachmentFunctionType={AttachmentFunctionType}, SecurityType={SecurityType}, SecurityId={SecurityId}, DatatypeId={DatatypeId}, MediaId={MediaId}, DocumentId={DocumentId}, AttachedDocumentId={AttachedDocumentId}",
            oracleInvoiceId, document.DocumentType, document.FileName, null, metadata.Language, document.ContentType,
            metadata.ConfiguredCategory, metadata.CategoryId, metadata.InternalName, metadata.UserName,
            metadata.AttachmentFunctionId, metadata.AttachmentFunctionName, metadata.AttachmentFunctionType,
            metadata.SecurityType, metadata.SecurityId,
            metadata.DatatypeId, mediaId.Value, documentId.Value, attachedDocumentId.Value);
    }

    // ============================================================
    // CLEAN PREVIOUS FAILED INTERFACE ATTEMPT
    // ============================================================

    private async Task CleanupPreviousInterfaceAttemptAsync(
        OracleConnection connection,
        OracleTransaction transaction,
        decimal vendorId,
        string invoiceNumber,
        string source,
        CancellationToken ct)
    {
        await using (
            var lineCommand =
                connection.CreateCommand()
        )
        {
            Configure(lineCommand);
            lineCommand.Transaction =
                transaction;

            lineCommand.BindByName =
                true;

            lineCommand.CommandText =
                """
                DELETE FROM
                    ap_invoice_lines_interface

                WHERE
                    invoice_id IN
                    (
                        SELECT
                            invoice_id
                        FROM
                            ap_invoices_interface
                        WHERE
                            vendor_id = :vendor_id
                        AND
                            UPPER(TRIM(invoice_num)) =
                            UPPER(TRIM(:invoice_num))
                        AND
                            UPPER(TRIM(source)) =
                            UPPER(TRIM(:source))
                    )
                """;

            Add(lineCommand, "vendor_id", OracleDbType.Decimal, vendorId);
            Add(lineCommand, "invoice_num", OracleDbType.Varchar2, invoiceNumber);
            Add(lineCommand, "source", OracleDbType.Varchar2, source);

            await lineCommand.ExecuteNonQueryAsync(ct);
        }

        await using (
            var headerCommand =
                connection.CreateCommand()
        )
        {
            Configure(headerCommand);
            headerCommand.Transaction =
                transaction;

            headerCommand.BindByName =
                true;

            headerCommand.CommandText =
                """
                DELETE FROM
                    ap_invoices_interface
                WHERE
                    vendor_id = :vendor_id
                AND
                    UPPER(TRIM(invoice_num)) =
                    UPPER(TRIM(:invoice_num))
                AND
                    UPPER(TRIM(source)) =
                    UPPER(TRIM(:source))
                """;

            Add(headerCommand, "vendor_id", OracleDbType.Decimal, vendorId);
            Add(headerCommand, "invoice_num", OracleDbType.Varchar2, invoiceNumber);
            Add(headerCommand, "source", OracleDbType.Varchar2, source);

            await headerCommand.ExecuteNonQueryAsync(ct);
        }
    }

    // ============================================================
    // ORACLE APPS CONTEXT
    // ============================================================

    private async Task InitializeAppsContextAsync(
        OracleConnection connection,
        OracleTransaction? transaction,
        CancellationToken ct)
    {
        await using var command =
            connection.CreateCommand();

        Configure(command);

        command.Transaction =
            transaction;

        command.BindByName =
            true;

        command.CommandText =
            """
            BEGIN
                fnd_global.apps_initialize
                (
                    :user_id,
                    :resp_id,
                    :resp_appl_id
                );
            END;
            """;

        Add(
            command,
            "user_id",
            OracleDbType.Int64,
            GetLong("ORACLE_APPS_USER_ID")
        );

        Add(
            command,
            "resp_id",
            OracleDbType.Int64,
            GetLong("ORACLE_APPS_RESP_ID")
        );

        Add(
            command,
            "resp_appl_id",
            OracleDbType.Int64,
            GetLong("ORACLE_APPS_RESP_APPL_ID")
        );

        await command.ExecuteNonQueryAsync(ct);
    }

    // ============================================================
    // SEQUENCES
    // ============================================================

    private async Task<long> NextValueAsync(
        OracleConnection connection,
        OracleTransaction transaction,
        string sequence,
        CancellationToken ct)
    {
        if (
            sequence !=
            "AP_INVOICES_INTERFACE_S"
            &&
            sequence !=
            "AP_INVOICE_LINES_INTERFACE_S"
        )
        {
            throw new InvalidOperationException(
                "Unsupported Oracle sequence."
            );
        }

        await using var command =
            connection.CreateCommand();

        Configure(command);

        command.Transaction =
            transaction;

        command.CommandText =
            $"SELECT {sequence}.NEXTVAL FROM DUAL";

        var result =
            await command.ExecuteScalarAsync(ct);

        return Convert.ToInt64(
            result
        );
    }

    // ============================================================
    // ORACLE STEP DIAGNOSTICS
    // ============================================================

    private async Task RunOracleStepAsync(
        string step,
        Func<Task> action)
    {
        var stopwatch = Stopwatch.StartNew();
        logger.LogInformation(
            "Oracle AP step started: {Step}",
            step
        );

        try
        {
            await action();

            logger.LogInformation(
                "Oracle AP step completed: {Step}. DurationMs={DurationMs}",
                step,
                stopwatch.ElapsedMilliseconds
            );
        }
        catch (OracleException ex)
        {
            var message =
                CleanOracleMessage(ex);

            logger.LogError(
                ex,
                "Oracle AP step failed: {Step}. " +
                "ORA-{OracleNumber:D5}: {OracleMessage}",
                step,
                ex.Number,
                message
            );

            throw new OracleApBusinessException(
                $"Oracle AP step '{step}' failed: " +
                $"ORA-{ex.Number:D5}: {message}",
                ex
            );
        }
    }

    private async Task<T> RunOracleStepAsync<T>(
        string step,
        Func<Task<T>> action)
    {
        var stopwatch = Stopwatch.StartNew();
        logger.LogInformation(
            "Oracle AP step started: {Step}",
            step
        );

        try
        {
            var result =
                await action();

            logger.LogInformation(
                "Oracle AP step completed: {Step}. DurationMs={DurationMs}",
                step,
                stopwatch.ElapsedMilliseconds
            );

            return result;
        }
        catch (OracleException ex)
        {
            var message =
                CleanOracleMessage(ex);

            logger.LogError(
                ex,
                "Oracle AP step failed: {Step}. " +
                "ORA-{OracleNumber:D5}: {OracleMessage}",
                step,
                ex.Number,
                message
            );

            throw new OracleApBusinessException(
                $"Oracle AP step '{step}' failed: " +
                $"ORA-{ex.Number:D5}: {message}",
                ex
            );
        }
    }

    private static string CleanOracleMessage(
        OracleException ex)
    {
        return
            ex.Message
                .Split(
                    new[]
                    {
                        '\r',
                        '\n'
                    },
                    StringSplitOptions.RemoveEmptyEntries
                )
                .FirstOrDefault()
            ??
            ex.Message;
    }

    // ============================================================
    // CONNECTION
    // ============================================================

    private async Task<OracleConnection> OpenAsync(
        CancellationToken ct)
    {
        if (
            string.IsNullOrWhiteSpace(options.Host)
            ||
            string.IsNullOrWhiteSpace(options.ServiceName)
            ||
            string.IsNullOrWhiteSpace(options.User)
        )
        {
            throw new OracleApBusinessException(
                "Oracle connection configuration is incomplete."
            );
        }

        var connection =
            new OracleConnection(
                options.ConnectionString
            );

        await connection.OpenAsync(ct);

        return connection;
    }

    // OracleCommand defaults to an unlimited timeout.  A cancellation token is
    // not sufficient for every provider/network failure, so set a finite
    // provider-side timeout as well. Callers that create commands use this
    // helper before executing them.
    private void Configure(OracleCommand command)
    {
        command.CommandTimeout = GetInt("ORACLE_AP_COMMAND_TIMEOUT_SECONDS", 60);
    }

    // ============================================================
    // CONFIG
    // ============================================================

    private void ValidateConfiguration()
    {
        _ = GetLong("ORACLE_APPS_USER_ID");
        _ = GetLong("ORACLE_APPS_RESP_ID");
        _ = GetLong("ORACLE_APPS_RESP_APPL_ID");
    }

    private static string MapInvoiceType(
        string invoiceType)
    {
        return invoiceType
            .ToUpperInvariant()
        switch
        {
            "GOODS" => "STANDARD",
            "SERVICE" => "STANDARD",
            "CREDIT" => "CREDIT",
            "DEBIT" => "DEBIT",
            _ => "STANDARD"
        };
    }

    private string Get(
        string key,
        string defaultValue)
    {
        return
            string.IsNullOrWhiteSpace(
                config[key]
            )
                ? defaultValue
                : config[key]!.Trim();
    }

    private long GetLong(
        string key)
    {
        if (
            !long.TryParse(
                config[key],
                out var value
            )
            ||
            value <= 0
        )
        {
            throw new OracleApBusinessException(
                $"{key} must be configured with a valid Oracle EBS numeric ID."
            );
        }

        return value;
    }

    private long? GetOptionalLong(
        string key)
    {
        return
            long.TryParse(
                config[key],
                out var value
            )
            &&
            value > 0
                ? value
                : null;
    }

    private int GetInt(
        string key,
        int defaultValue)
    {
        return
            int.TryParse(
                config[key],
                out var value
            )
            &&
            value > 0
                ? value
                : defaultValue;
    }

    private decimal GetDecimal(
        string key,
        decimal defaultValue)
    {
        return
            decimal.TryParse(
                config[key],
                out var value
            )
            &&
            value >= 0
                ? value
                : defaultValue;
    }

    private static object DbValue(
        string? value)
    {
        return
            string.IsNullOrWhiteSpace(
                value
            )
                ? DBNull.Value
                : value.Trim();
    }

    private static long ReadRequiredInt64(
        OracleDataReader reader,
        int ordinal,
        string field)
    {
        if (
            reader.IsDBNull(
                ordinal
            )
        )
        {
            throw new OracleApBusinessException(
                $"Oracle receipt resolution returned NULL " +
                $"for required field {field}."
            );
        }

        return Convert.ToInt64(
            reader.GetValue(
                ordinal
            )
        );
    }

    private static int ReadRequiredInt32(
        OracleDataReader reader,
        int ordinal,
        string field)
    {
        if (
            reader.IsDBNull(
                ordinal
            )
        )
        {
            throw new OracleApBusinessException(
                $"Oracle receipt resolution returned NULL " +
                $"for required field {field}."
            );
        }

        return Convert.ToInt32(
            reader.GetValue(
                ordinal
            )
        );
    }

    private static decimal ReadRequiredDecimal(
        OracleDataReader reader,
        int ordinal,
        string field)
    {
        if (
            reader.IsDBNull(
                ordinal
            )
        )
        {
            throw new OracleApBusinessException(
                $"Oracle receipt resolution returned NULL " +
                $"for required field {field}."
            );
        }

        return Convert.ToDecimal(
            reader.GetValue(
                ordinal
            )
        );
    }

    private static void Add(
        OracleCommand command,
        string name,
        OracleDbType type,
        object? value)
    {
        var parameter =
            command.Parameters.Add(
                name,
                type
            );

        parameter.Value =
            value
            ??
            DBNull.Value;
    }

    private sealed record OracleApAttachmentMetadata(
        string ConfiguredCategory,
        long CategoryId,
        string InternalName,
        string UserName,
        long AttachmentFunctionId,
        string AttachmentFunctionName,
        string AttachmentFunctionType,
        int DatatypeId,
        int SecurityType,
        long? SecurityId,
        string Language);
}
