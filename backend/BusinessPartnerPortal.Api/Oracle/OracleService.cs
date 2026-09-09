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
    // GET SUPPLIERS
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
            FROM
            (
                SELECT *
                FROM APPS.PORTAL_SUPPLIERS_V
                ORDER BY VENDOR_NAME
            )
            WHERE ROWNUM <= 1000
            """;

        var rows =
            await Read(command, ct);

        return rows
            .Select(MapSupplier)
            .ToList();
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
            FROM
            (
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
            )
            WHERE ROWNUM <= 500
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

        return rows
            .Select(MapPoGrn)
            .ToList();
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
                    "QC_STATUS",
                    "INSPECTION_STATUS"
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
                )
                ?? 0,

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