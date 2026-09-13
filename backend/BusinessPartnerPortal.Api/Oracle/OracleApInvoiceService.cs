using System.Data;
using System.Globalization;

using Oracle.ManagedDataAccess.Client;

namespace BusinessPartnerPortal.Api.Oracle;

public sealed class OracleApInvoiceService(
    OracleOptions options,
    OracleService oracleReadService,
    IConfiguration config,
    ILogger<OracleApInvoiceService> logger)
{
    // ============================================================
    // MAIN ORACLE AP PROCESS
    // ============================================================

    public async Task<OracleApProcessResult> ProcessInvoiceAsync(
        OracleApPortalInvoice invoice,
        CancellationToken ct)
    {
        ValidateConfiguration();

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

        await RunOracleStepAsync(
            "PROCESS.APXIIMPT_WAIT",
            () => WaitForConcurrentRequestAsync(
                requestId,
                ct
            )
        );

        var rejections =
            await RunOracleStepAsync(
                "PROCESS.INTERFACE_REJECTIONS_LOOKUP",
                () => GetInterfaceRejectionsAsync(
                    interfaceInvoiceId,
                    ct
                )
            );

        if (rejections.Count > 0)
        {
            throw new OracleApRejectedException(
                rejections
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
            throw new OracleApBusinessException(
                "Payables Open Interface Import completed, " +
                "but the invoice was not found in AP_INVOICES_ALL."
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
        string source,
        IReadOnlyList<OracleReceiptLine> receiptLines,
        CancellationToken ct)
    {
        await using var connection =
            await OpenAsync(ct);

        using var transaction =
            connection.BeginTransaction();

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
                    DbValue(config["ORACLE_AP_PAYMENT_METHOD_LOOKUP_CODE"])
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

        while (
            DateTimeOffset.UtcNow <
            deadline
        )
        {
            await using var connection =
                await OpenAsync(ct);

            await using var command =
                connection.CreateCommand();

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

    private async Task<IReadOnlyList<string>>
        GetInterfaceRejectionsAsync(
            long interfaceInvoiceId,
            CancellationToken ct)
    {
        await using var connection =
            await OpenAsync(ct);

        await using var command =
            connection.CreateCommand();

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
                                "INVOICE COPY"
                            ),

                        "DELIVERY_CHALLAN" =>
                            Get(
                                "ORACLE_AP_DELIVERY_CHALLAN_CATEGORY",
                                "DELIVERY CHALLAN"
                            ),

                        _ =>
                            Get(
                                "ORACLE_AP_SUPPORTING_DOCUMENT_CATEGORY",
                                "MISCELLANEOUS"
                            )
                    };

                var exists =
                    await AttachmentExistsAsync(
                        connection,
                        transaction,
                        oracleInvoiceId,
                        document.FileName,
                        category,
                        ct
                    );

                if (exists)
                {
                    continue;
                }

                logger.LogInformation(
                    "Creating Oracle AP invoice attachment. " +
                    "OracleInvoiceId={OracleInvoiceId}, FileName={FileName}, " +
                    "DocumentType={DocumentType}, RequestedCategoryName={RequestedCategoryName}",
                    oracleInvoiceId,
                    document.FileName,
                    document.DocumentType,
                    category
                );

                await AttachSingleFileAsync(
                    connection,
                    transaction,
                    oracleInvoiceId,
                    document,
                    category,
                    ct
                );
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private async Task<bool> AttachmentExistsAsync(
        OracleConnection connection,
        OracleTransaction transaction,
        long oracleInvoiceId,
        string fileName,
        string categoryName,
        CancellationToken ct)
    {
        await using var command =
            connection.CreateCommand();

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

            JOIN
                fnd_document_categories_vl fdc
              ON
                fdc.category_id =
                fad.category_id

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

            AND
                (
                    UPPER(
                        TRIM(
                            fdc.name
                        )
                    )
                    =
                    UPPER(
                        TRIM(
                            :category_name
                        )
                    )
                OR
                    UPPER(
                        TRIM(
                            fdc.user_name
                        )
                    )
                    =
                    UPPER(
                        TRIM(
                            :category_name
                        )
                    )
                )

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
            "category_name",
            OracleDbType.Varchar2,
            categoryName
        );

        return
            Convert.ToInt32(
                await command.ExecuteScalarAsync(ct)
            )
            >
            0;
    }

    private async Task AttachSingleFileAsync(
        OracleConnection connection,
        OracleTransaction transaction,
        long oracleInvoiceId,
        OracleApDocument document,
        string categoryName,
        CancellationToken ct)
    {
        var fileFormat =
            Get(
                "ORACLE_AP_ATTACHMENT_FILE_FORMAT",
                "binary"
            );

        await using var command =
            connection.CreateCommand();

        command.Transaction =
            transaction;

        command.BindByName =
            true;

        command.CommandText =
            """
            DECLARE
                l_media_id NUMBER;
                l_doc_id NUMBER;
                l_category_id NUMBER;
                l_seq_num NUMBER;

            BEGIN
                BEGIN
                    SELECT
                        category_id
                    INTO
                        l_category_id
                    FROM
                        fnd_document_categories_vl
                    WHERE
                        (
                            UPPER(TRIM(name)) =
                            UPPER(TRIM(:p_category_name))
                        OR
                            UPPER(TRIM(user_name)) =
                            UPPER(TRIM(:p_category_name))
                        )
                    AND
                        ROWNUM = 1;

                EXCEPTION
                    WHEN NO_DATA_FOUND THEN
                        RAISE_APPLICATION_ERROR(
                            -20001,
                            'Attachment category not found in Oracle: ' ||
                            :p_category_name
                        );
                END;

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
                    program_tag
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
                    'AP_INVOICE_ATTACH'
                );

                l_doc_id :=
                    fnd_documents_s.NEXTVAL;

                INSERT INTO fnd_documents
                (
                    document_id,
                    category_id,
                    security_type,
                    publish_flag,
                    datatype_id,
                    usage_type,
                    media_id,
                    creation_date,
                    created_by,
                    last_update_date,
                    last_updated_by,
                    last_update_login
                )
                VALUES
                (
                    l_doc_id,
                    l_category_id,
                    1,
                    'Y',
                    5,
                    'O',
                    l_media_id,
                    SYSDATE,
                    fnd_global.user_id,
                    SYSDATE,
                    fnd_global.user_id,
                    fnd_global.login_id
                );

                INSERT INTO fnd_documents_tl
                (
                    document_id,
                    language,
                    source_lang,
                    description,
                    file_name,
                    media_id,
                    creation_date,
                    created_by,
                    last_update_date,
                    last_updated_by,
                    last_update_login
                )
                SELECT
                    l_doc_id,
                    l.language_code,
                    USERENV('LANG'),
                    :p_description,
                    :p_file_name,
                    l_media_id,
                    SYSDATE,
                    fnd_global.user_id,
                    SYSDATE,
                    fnd_global.user_id,
                    fnd_global.login_id
                FROM
                    fnd_languages l
                WHERE
                    l.installed_flag IN ('B', 'I');

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

                INSERT INTO fnd_attached_documents
                (
                    attached_document_id,
                    document_id,
                    entity_name,
                    pk1_value,
                    category_id,
                    seq_num,
                    automatically_added_flag,
                    creation_date,
                    created_by,
                    last_update_date,
                    last_updated_by,
                    last_update_login
                )
                VALUES
                (
                    fnd_attached_documents_s.NEXTVAL,
                    l_doc_id,
                    'AP_INVOICES',
                    TO_CHAR(:p_invoice_id),
                    l_category_id,
                    l_seq_num,
                    'N',
                    SYSDATE,
                    fnd_global.user_id,
                    SYSDATE,
                    fnd_global.user_id,
                    fnd_global.login_id
                );

            END;
            """;

        Add(command, "p_category_name", OracleDbType.Varchar2, categoryName);
        Add(command, "p_file_name", OracleDbType.Varchar2, document.FileName);
        Add(command, "p_content_type", OracleDbType.Varchar2, document.ContentType);
        Add(command, "p_file_format", OracleDbType.Varchar2, fileFormat);
        Add(command, "p_blob_data", OracleDbType.Blob, document.Content);

        Add(
            command,
            "p_description",
            OracleDbType.Varchar2,
            document.DocumentType == "INVOICE"
                ? "Business Portal Invoice Copy"
                : "Business Portal Receipted Delivery Challan"
        );

        Add(command, "p_invoice_id", OracleDbType.Int64, oracleInvoiceId);

        logger.LogInformation(
            "Inserting Oracle FND_LOBS attachment. " +
            "OracleInvoiceId={OracleInvoiceId}, FileName={FileName}, " +
            "ContentType={ContentType}, FileFormat={FileFormat}",
            oracleInvoiceId,
            document.FileName,
            document.ContentType,
            fileFormat
        );

        await command.ExecuteNonQueryAsync(ct);
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
        logger.LogInformation(
            "Oracle AP step started: {Step}",
            step
        );

        try
        {
            await action();

            logger.LogInformation(
                "Oracle AP step completed: {Step}",
                step
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
        logger.LogInformation(
            "Oracle AP step started: {Step}",
            step
        );

        try
        {
            var result =
                await action();

            logger.LogInformation(
                "Oracle AP step completed: {Step}",
                step
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
}