using Oracle.ManagedDataAccess.Client;

namespace BusinessPartnerPortal.Api.Oracle;

public sealed class OracleService(
    OracleOptions options,
    ILogger<OracleService> logger)
{
    // ============================================================
    // OPEN ORACLE CONNECTION
    // ============================================================

    private async Task<OracleConnection> Open(
        CancellationToken ct)
    {
        var connection =
            new OracleConnection(
                options.ConnectionString
            );

        await connection.OpenAsync(ct);

        return connection;
    }

    // ============================================================
    // TEST CONNECTIVITY
    // ============================================================

    public async Task<bool> TestConnectivityAsync(
        CancellationToken ct)
    {
        await using var connection =
            await Open(ct);

        await using var command =
            connection.CreateCommand();

        command.CommandText =
            "SELECT 1 FROM DUAL";

        var result =
            await command.ExecuteScalarAsync(ct);

        return Convert.ToInt32(result) == 1;
    }

    // ============================================================
    // GET SUPPLIER BY ORACLE VENDOR ID
    // ============================================================

    public async Task<OracleSupplierDto?>
        GetSupplierByVendorIdAsync(
            decimal vendorId,
            CancellationToken ct)
    {
        await using var connection =
            await Open(ct);

        await using var command =
            connection.CreateCommand();

        command.BindByName = true;

        command.CommandText =
            """
            SELECT *
            FROM APPS.PORTAL_SUPPLIERS_V
            WHERE VENDOR_ID = :vendorId
              AND ROWNUM = 1
            """;

        command.Parameters.Add(
            "vendorId",
            OracleDbType.Decimal
        ).Value = vendorId;

        var rows =
            await Read(command, ct);

        if (rows.Count == 0)
        {
            return null;
        }

        return MapSupplier(
            rows[0]
        );
    }

    // ============================================================
    // GET SUPPLIER BY SUPPLIER NUMBER
    // ============================================================

    public async Task<OracleSupplierDto?>
        GetSupplierByNumberAsync(
            string supplierNumber,
            CancellationToken ct)
    {
        await using var connection =
            await Open(ct);

        await using var command =
            connection.CreateCommand();

        command.BindByName = true;

        command.CommandText =
            """
            SELECT *
            FROM APPS.PORTAL_SUPPLIERS_V
            WHERE TRIM(
                CAST(
                    SUPPLIER_NUMBER AS VARCHAR2(100)
                )
            ) = :supplierNumber
              AND ROWNUM = 1
            """;

        command.Parameters.Add(
            "supplierNumber",
            OracleDbType.Varchar2
        ).Value = supplierNumber.Trim();

        var rows =
            await Read(command, ct);

        if (rows.Count == 0)
        {
            return null;
        }

        return MapSupplier(
            rows[0]
        );
    }

    // ============================================================
    // GET ALL SUPPLIERS
    // ============================================================

    public async Task<List<OracleSupplierDto>>
        GetSuppliersAsync(
            CancellationToken ct)
    {
        await using var connection =
            await Open(ct);

        await using var command =
            connection.CreateCommand();

        command.CommandText =
            """
            SELECT *
            FROM APPS.PORTAL_SUPPLIERS_V
            ORDER BY VENDOR_NAME
            """;

        var rows =
            await Read(command, ct);

        return rows
            .Select(MapSupplier)
            .ToList();
    }

    // ============================================================
    // GET TOTAL ORACLE VENDOR COUNT
    // ============================================================

    public async Task<int> GetOracleVendorCountAsync(
        CancellationToken ct)
    {
        await using var connection =
            await Open(ct);

        await using var command =
            connection.CreateCommand();

        command.CommandText =
            """
            SELECT COUNT(DISTINCT VENDOR_ID)
            FROM APPS.PORTAL_SUPPLIERS_V
            WHERE VENDOR_ID IS NOT NULL
            """;

        try
        {
            var result =
                await command.ExecuteScalarAsync(ct);

            if (
                result is null ||
                result == DBNull.Value
            )
            {
                return 0;
            }

            return Convert.ToInt32(
                result
            );
        }
        catch (OracleException ex)
        {
            logger.LogError(
                ex,
                "Failed to get total Oracle vendor count. OracleError={OracleErrorNumber}",
                ex.Number
            );

            throw;
        }
    }

    // ============================================================
    // SEARCH SUPPLIERS
    // ============================================================

    public async Task<List<OracleSupplierDto>>
        SearchSuppliersAsync(
            string? search,
            CancellationToken ct)
    {
        if (
            string.IsNullOrWhiteSpace(
                search
            )
        )
        {
            return await GetSuppliersAsync(ct);
        }

        await using var connection =
            await Open(ct);

        await using var command =
            connection.CreateCommand();

        command.BindByName = true;

        var searchText =
            search
                .Trim()
                .ToUpperInvariant();

        var pattern =
            $"%{searchText}%";

        command.CommandText =
            """
            SELECT *
            FROM APPS.PORTAL_SUPPLIERS_V
            WHERE
                UPPER(
                    NVL(
                        VENDOR_NAME,
                        ''
                    )
                ) LIKE :pattern

                OR UPPER(
                    TRIM(
                        CAST(
                            VENDOR_ID AS VARCHAR2(100)
                        )
                    )
                ) LIKE :pattern

                OR UPPER(
                    TRIM(
                        CAST(
                            SUPPLIER_NUMBER AS VARCHAR2(100)
                        )
                    )
                ) LIKE :pattern

                OR UPPER(
                    NVL(
                        CONTACT_PERSON,
                        ''
                    )
                ) LIKE :pattern

                OR UPPER(
                    NVL(
                        CONTACT_EMAIL,
                        ''
                    )
                ) LIKE :pattern

                OR UPPER(
                    NVL(
                        CONTACT_PHONE,
                        ''
                    )
                ) LIKE :pattern

            ORDER BY VENDOR_NAME
            """;

        command.Parameters.Add(
            "pattern",
            OracleDbType.Varchar2
        ).Value = pattern;

        var rows =
            await Read(command, ct);

        return rows
            .Select(MapSupplier)
            .ToList();
    }

    // ============================================================
    // GET PO / GRN DATA
    // ============================================================

    public async Task<List<OraclePoGrnDto>>
        GetPoGrnsAsync(
            decimal vendorId,
            string? poNumber,
            CancellationToken ct)
    {
        await using var connection =
            await Open(ct);

        await using var command =
            connection.CreateCommand();

        command.BindByName = true;

        command.CommandText =
            """
            SELECT *
            FROM APPS.PORTAL_PO_GRN_V
            WHERE VENDOR_ID = :vendorId

              AND COALESCE(
                    RECEIPT_DATE,
                    PO_CREATION_DATE,
                    PO_APPROVED_DATE
                  ) >= :fiscalWindowStart
            """
            +
            (
                string.IsNullOrWhiteSpace(
                    poNumber
                )
                    ? ""
                    :
                    """
                     AND TRIM(
                         CAST(
                             PO_NUMBER AS VARCHAR2(100)
                         )
                     ) = :poNumber
                    """
            )
            +
            """
             ORDER BY
                 PO_NUMBER DESC,
                 RECEIPT_DATE DESC NULLS LAST
            """;

        command.Parameters.Add(
            "vendorId",
            OracleDbType.Decimal
        ).Value = vendorId;

        command.Parameters.Add(
            "fiscalWindowStart",
            OracleDbType.Date
        ).Value = PakistanFiscalWindow.Start();

        if (
            !string.IsNullOrWhiteSpace(
                poNumber
            )
        )
        {
            command.Parameters.Add(
                "poNumber",
                OracleDbType.Varchar2
            ).Value =
                poNumber.Trim();
        }

        var rows =
            await Read(command, ct);

        var mappedRows =
            rows
                .Select(MapPoGrn)
                .ToList();

        /*
         * IMPORTANT:
         *
         * Oracle view APPS.PORTAL_PO_GRN_V can return more than
         * one transaction record for the same logical GRN line.
         *
         * Example:
         *
         * Same:
         *   PO
         *   GRN
         *   PO Line
         *   Shipment Line
         *   Item
         *   Qty
         *   Unit Price
         *
         * But different RCV_TRANSACTION_ID.
         *
         * If we deduplicate only on RCV_TRANSACTION_ID, the same
         * GRN line appears multiple times and:
         *
         *   Received Qty is duplicated
         *   Available Qty is duplicated
         *   GRN Amount is duplicated
         *   Available To Invoice is duplicated
         *
         * For portal display/calculation the correct logical grain
         * is primarily:
         *
         *   GRN_NUMBER + SHIPMENT_LINE_ID
         *
         * RCV_TRANSACTION_ID is still preserved in the selected
         * DTO row for Oracle AP invoice integration.
         */
        return mappedRows
            .GroupBy(
                GetPoGrnBusinessLineKey,
                StringComparer.OrdinalIgnoreCase
            )
            .Select(
                group =>
                    SelectBestPoGrnRow(
                        group
                    )
            )
            .OrderByDescending(
                x => x.PoNumber
            )
            .ThenByDescending(
                x => x.ReceiptDate
            )
            .ToList();
    }

    // ============================================================
    // SELECT BEST ROW FROM DUPLICATE GRN RECORDS
    // ============================================================

    private static OraclePoGrnDto SelectBestPoGrnRow(
        IEnumerable<OraclePoGrnDto> rows)
    {
        /*
         * When the same logical GRN line is returned multiple
         * times by Oracle, prefer the row carrying the greatest
         * available quantity.
         *
         * If quantities are equal, prefer the latest receipt date.
         *
         * This keeps one canonical row for the portal.
         */
        return rows
            .OrderByDescending(
                x =>
                    x.QuantityAvailableToInvoice
                    ?? 0
            )
            .ThenByDescending(
                x =>
                    x.GrnReceivedQuantity
                    ?? x.ReceivedQuantity
                    ?? 0
            )
            .ThenByDescending(
                x => x.ReceiptDate
            )
            .ThenByDescending(
                x =>
                    ParseNumericId(
                        x.RcvTransactionId
                    )
            )
            .First();
    }

    // ============================================================
    // PO / GRN BUSINESS LINE KEY
    // ============================================================

    private static string GetPoGrnBusinessLineKey(
        OraclePoGrnDto row)
    {
        var poNumber =
            NormalizeKeyPart(
                row.PoNumber
            );

        var grnNumber =
            NormalizeKeyPart(
                row.GrnNumber
            );

        var shipmentLineId =
            NormalizeKeyPart(
                row.ShipmentLineId
            );

        var poLineId =
            NormalizeKeyPart(
                row.PoLineId
            );

        var poLineNum =
            NormalizeKeyPart(
                row.PoLineNum
            );

        var itemId =
            NormalizeKeyPart(
                row.ItemId
            );

        var itemCode =
            NormalizeKeyPart(
                row.ItemCode
            );

        /*
         * BEST CASE
         *
         * SHIPMENT_LINE_ID identifies the physical Oracle
         * receiving shipment line.
         *
         * One GRN can legitimately have multiple lines, therefore
         * GRN number alone must never be used for deduplication.
         */
        if (
            !string.IsNullOrWhiteSpace(
                shipmentLineId
            )
        )
        {
            return string.Join(
                "|",
                "PO",
                poNumber,
                "GRN",
                grnNumber,
                "SHIPMENT_LINE",
                shipmentLineId
            );
        }

        /*
         * SECOND FALLBACK
         *
         * If shipment line is unavailable, use GRN + PO line +
         * item identity.
         *
         * This prevents identical Oracle transaction rows for the
         * same business line from being counted multiple times.
         */
        if (
            !string.IsNullOrWhiteSpace(
                poLineId
            )
        )
        {
            return string.Join(
                "|",
                "PO",
                poNumber,
                "GRN",
                grnNumber,
                "PO_LINE_ID",
                poLineId,
                "ITEM",
                !string.IsNullOrWhiteSpace(itemId)
                    ? itemId
                    : itemCode
            );
        }

        /*
         * THIRD FALLBACK
         *
         * Some Oracle views may not expose PO_LINE_ID but may expose
         * the human-readable PO line number.
         */
        if (
            !string.IsNullOrWhiteSpace(
                poLineNum
            )
        )
        {
            return string.Join(
                "|",
                "PO",
                poNumber,
                "GRN",
                grnNumber,
                "PO_LINE_NUM",
                poLineNum,
                "ITEM",
                !string.IsNullOrWhiteSpace(itemId)
                    ? itemId
                    : itemCode
            );
        }

        /*
         * FINAL FALLBACK
         *
         * Only used when Oracle does not provide shipment-line or
         * PO-line identifiers.
         *
         * Keep quantity, price and date in the key to avoid merging
         * genuinely different GRN receipt lines.
         */
        return string.Join(
            "|",
            "PO",
            poNumber,
            "GRN",
            grnNumber,
            "ITEM",
            !string.IsNullOrWhiteSpace(itemId)
                ? itemId
                : itemCode,
            "QTY",
            DecimalKey(
                row.GrnReceivedQuantity
                ?? row.ReceivedQuantity
            ),
            "PRICE",
            DecimalKey(
                row.UnitPrice
            ),
            "DATE",
            DateKey(
                row.ReceiptDate
            )
        );
    }

    // ============================================================
    // NORMALIZE BUSINESS KEY STRING
    // ============================================================

    private static string NormalizeKeyPart(
        string? value)
    {
        return string.IsNullOrWhiteSpace(
            value
        )
            ? ""
            : value
                .Trim()
                .ToUpperInvariant();
    }

    // ============================================================
    // DECIMAL KEY
    // ============================================================

    private static string DecimalKey(
        decimal? value)
    {
        return (
            value
            ?? 0
        ).ToString(
            System.Globalization.CultureInfo.InvariantCulture
        );
    }

    // ============================================================
    // DATE KEY
    // ============================================================

    private static string DateKey(
        DateTime? value)
    {
        return value.HasValue
            ? value.Value.ToString(
                "yyyyMMddHHmmss",
                System.Globalization.CultureInfo.InvariantCulture
            )
            : "";
    }

    // ============================================================
    // SAFE NUMERIC ID FOR SORTING
    // ============================================================

    private static decimal ParseNumericId(
        string? value)
    {
        if (
            decimal.TryParse(
                value,
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture,
                out var parsed
            )
        )
        {
            return parsed;
        }

        return 0;
    }

    // ============================================================
    // GET ORACLE INVOICES
    // ============================================================

    public async Task<List<OracleInvoiceDto>>
        GetInvoicesAsync(
            decimal vendorId,
            CancellationToken ct)
    {
        await using var connection =
            await Open(ct);

        await using var command =
            connection.CreateCommand();

        command.BindByName = true;

        command.CommandText =
            """
            SELECT *
            FROM APPS.PORTAL_INVOICES_V
            WHERE VENDOR_ID = :vendorId
            ORDER BY
                INVOICE_DATE DESC NULLS LAST
            """;

        command.Parameters.Add(
            "vendorId",
            OracleDbType.Decimal
        ).Value = vendorId;

        var rows =
            await Read(command, ct);

        return rows
            .Select(MapInvoice)
            .ToList();
    }

    // ============================================================
    // GENERIC ORACLE READER
    // ============================================================

    private async Task<
        List<Dictionary<string, object?>>>
        Read(
            OracleCommand command,
            CancellationToken ct)
    {
        try
        {
            var result =
                new List<
                    Dictionary<string, object?>>();

            await using var reader =
                await command.ExecuteReaderAsync(
                    ct
                );

            while (
                await reader.ReadAsync(
                    ct
                )
            )
            {
                var row =
                    new Dictionary<
                        string,
                        object?
                    >(
                        StringComparer
                            .OrdinalIgnoreCase
                    );

                for (
                    var i = 0;
                    i < reader.FieldCount;
                    i++
                )
                {
                    row[
                        reader.GetName(i)
                    ] =
                        reader.IsDBNull(i)
                            ? null
                            : reader.GetValue(i);
                }

                result.Add(row);
            }

            return result;
        }
        catch (OracleException ex)
        {
            logger.LogError(
                ex,
                "Oracle query failed. OracleError={OracleErrorNumber}",
                ex.Number
            );

            throw;
        }
    }

    // ============================================================
    // SAFE STRING READER
    // ============================================================

    private static string S(
        Dictionary<string, object?> row,
        params string[] names)
    {
        foreach (
            var name in names
        )
        {
            if (
                row.TryGetValue(
                    name,
                    out var value
                )
                &&
                value is not null
            )
            {
                var result =
                    Convert.ToString(
                        value
                    );

                if (
                    !string.IsNullOrWhiteSpace(
                        result
                    )
                )
                {
                    return result.Trim();
                }
            }
        }

        return "";
    }

    // ============================================================
    // SAFE NUMBER READER
    // ============================================================

    private static decimal? N(
        Dictionary<string, object?> row,
        params string[] names)
    {
        foreach (
            var name in names
        )
        {
            if (
                !row.TryGetValue(
                    name,
                    out var value
                )
                ||
                value is null
            )
            {
                continue;
            }

            try
            {
                return Convert.ToDecimal(
                    value
                );
            }
            catch
            {
                var text =
                    Convert.ToString(
                        value
                    );

                if (
                    decimal.TryParse(
                        text,
                        out var parsed
                    )
                )
                {
                    return parsed;
                }
            }
        }

        return null;
    }

    // ============================================================
    // SAFE DATE READER
    // ============================================================

    private static DateTime? D(
        Dictionary<string, object?> row,
        params string[] names)
    {
        foreach (
            var name in names
        )
        {
            if (
                !row.TryGetValue(
                    name,
                    out var value
                )
                ||
                value is null
            )
            {
                continue;
            }

            if (
                value is DateTime date
            )
            {
                return date;
            }

            if (
                DateTime.TryParse(
                    Convert.ToString(
                        value
                    ),
                    out var parsed
                )
            )
            {
                return parsed;
            }
        }

        return null;
    }

    // ============================================================
    // SUPPLIER MAPPER
    // ============================================================

    private static OracleSupplierDto MapSupplier(
        Dictionary<string, object?> row)
    {
        return new OracleSupplierDto(
            VendorId:
                S(
                    row,
                    "VENDOR_ID"
                ),

            SupplierNumber:
                S(
                    row,
                    "SUPPLIER_NUMBER",
                    "SEGMENT1"
                ),

            VendorName:
                S(
                    row,
                    "VENDOR_NAME"
                ),

            VendorSiteId:
                S(
                    row,
                    "VENDOR_SITE_ID"
                ),

            VendorSiteCode:
                S(
                    row,
                    "VENDOR_SITE_CODE"
                ),

            OrgId:
                S(
                    row,
                    "ORG_ID"
                ),

            OperatingUnit:
                S(
                    row,
                    "OPERATING_UNIT"
                ),

            ContactPerson:
                S(
                    row,
                    "CONTACT_PERSON",
                    "CONTACT_NAME"
                ),

            Email:
                S(
                    row,
                    "CONTACT_EMAIL",
                    "EMAIL_ADDRESS",
                    "EMAIL",
                    "VENDOR_EMAIL"
                ),

            Phone:
                S(
                    row,
                    "CONTACT_PHONE",
                    "PHONE",
                    "PHONE_NUMBER"
                ),

            TaxNumber:
                S(
                    row,
                    "TAX_REGISTRATION_NO",
                    "TAX_REGISTRATION_NUMBER",
                    "NTN",
                    "TAX_NUMBER"
                ),

            VendorType:
                S(
                    row,
                    "VENDOR_TYPE",
                    "VENDOR_TYPE_LOOKUP_CODE",
                    "SUPPLIER_TYPE"
                ),

            AddressLine1:
                S(
                    row,
                    "ADDRESS_LINE1"
                ),

            AddressLine2:
                S(
                    row,
                    "ADDRESS_LINE2"
                ),

            AddressLine3:
                S(
                    row,
                    "ADDRESS_LINE3"
                ),

            City:
                S(
                    row,
                    "CITY"
                ),

            State:
                S(
                    row,
                    "STATE"
                ),

            PostalCode:
                S(
                    row,
                    "ZIP",
                    "POSTAL_CODE"
                ),

            Country:
                S(
                    row,
                    "COUNTRY"
                )
        );
    }

    // ============================================================
    // PO / GRN MAPPER
    // ============================================================

    private static OraclePoGrnDto MapPoGrn(
        Dictionary<string, object?> row)
    {
        return new OraclePoGrnDto(
            PoHeaderId:
                S(
                    row,
                    "PO_HEADER_ID"
                ),

            PoNumber:
                S(
                    row,
                    "PO_NUMBER"
                ),

            PoType:
                S(
                    row,
                    "PO_TYPE"
                ),

            PoStatus:
                S(
                    row,
                    "PO_STATUS"
                ),

            InspectionStatus:
                S(
                    row,
                    "QC_STATUS"
                ),

            VendorId:
                S(
                    row,
                    "VENDOR_ID"
                ),

            VendorName:
                S(
                    row,
                    "VENDOR_NAME"
                ),

            VendorSiteCode:
                S(
                    row,
                    "VENDOR_SITE_CODE"
                ),

            PoLineId:
                S(
                    row,
                    "PO_LINE_ID"
                ),

            PoLineNum:
                S(
                    row,
                    "PO_LINE_NUM"
                ),

            ItemCode:
                S(
                    row,
                    "ITEM_CODE"
                ),

            ItemDescription:
                S(
                    row,
                    "ITEM_DESCRIPTION"
                ),

            Uom:
                S(
                    row,
                    "UOM"
                ),

            PoQuantity:
                N(
                    row,
                    "PO_QUANTITY"
                ),

            UnitPrice:
                N(
                    row,
                    "UNIT_PRICE"
                ),

            PoLineAmount:
                N(
                    row,
                    "PO_LINE_AMOUNT"
                ),

            OrderedQuantity:
                N(
                    row,
                    "ORDERED_QUANTITY"
                ),

            ReceivedQuantity:
                N(
                    row,
                    "RECEIVED_QUANTITY"
                ),

            BilledQuantity:
                N(
                    row,
                    "BILLED_QUANTITY"
                ),

            CancelledQuantity:
                N(
                    row,
                    "CANCELLED_QUANTITY"
                ),

            QuantityAvailableToInvoice:
                N(
                    row,
                    "QUANTITY_AVAILABLE_TO_INVOICE"
                ),

            GrnNumber:
                S(
                    row,
                    "GRN_NUMBER"
                ),

            RcvTransactionId:
                S(
                    row,
                    "RCV_TRANSACTION_ID"
                ),

            ShipmentLineId:
                S(
                    row,
                    "SHIPMENT_LINE_ID"
                ),

            ItemId:
                S(
                    row,
                    "ITEM_ID"
                ),

            GrnReceivedQuantity:
                N(
                    row,
                    "GRN_RECEIVED_QUANTITY"
                ),

            ReceiptDate:
                D(
                    row,
                    "RECEIPT_DATE"
                ),

            CurrencyCode:
                S(
                    row,
                    "CURRENCY_CODE"
                ),

            PoCreationDate:
                D(
                    row,
                    "PO_CREATION_DATE"
                ),

            PoApprovedDate:
                D(
                    row,
                    "PO_APPROVED_DATE"
                )
        );
    }

    // ============================================================
    // INVOICE MAPPER
    // ============================================================

    private static OracleInvoiceDto MapInvoice(
        Dictionary<string, object?> row)
    {
        return new OracleInvoiceDto(
            VendorId:
                S(
                    row,
                    "VENDOR_ID"
                ),

            SupplierNumber:
                S(
                    row,
                    "SUPPLIER_NUMBER"
                ),

            VendorName:
                S(
                    row,
                    "VENDOR_NAME"
                ),

            // IMPORTANT:
            // Actual numeric Oracle AP invoice ID.
            OracleInvoiceId:
                S(
                    row,
                    "INVOICE_ID"
                ),

            InvoiceNumber:
                S(
                    row,
                    "INVOICE_NUM",
                    "INVOICE_NUMBER"
                ),

            InvoiceDate:
                D(
                    row,
                    "INVOICE_DATE"
                ),

            InvoiceAmount:
                N(
                    row,
                    "INVOICE_AMOUNT"
                ) ?? 0,

            AmountPaid:
                N(
                    row,
                    "AMOUNT_PAID"
                ),

            OutstandingAmount:
                N(
                    row,
                    "OUTSTANDING_AMOUNT"
                ),

            PaymentStatusFlag:
                S(
                    row,
                    "PAYMENT_STATUS_FLAG"
                ),

            PaymentStatus:
                S(
                    row,
                    "PAYMENT_STATUS"
                ),

            ApprovalStatus:
                S(
                    row,
                    "APPROVAL_STATUS"
                ),

            PoNumber:
                S(
                    row,
                    "PO_NUMBER"
                ),

            GrnNumber:
                S(
                    row,
                    "GRN_NUMBER"
                ),

            ReceiptDate:
                D(
                    row,
                    "RECEIPT_DATE"
                )
        );
    }
}