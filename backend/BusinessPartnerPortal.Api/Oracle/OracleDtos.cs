namespace BusinessPartnerPortal.Api.Oracle;

public sealed record OracleSupplierDto(
    string VendorId,
    string SupplierNumber,
    string VendorName,

    string? VendorSiteId,
    string? VendorSiteCode,

    string? OrgId,
    string? OperatingUnit,

    // Contact information from PORTAL_SUPPLIERS_V
    string? ContactPerson,
    string? Email,
    string? Phone,

    string? TaxNumber,
    string? VendorType,

    string? AddressLine1,
    string? AddressLine2,
    string? AddressLine3,

    string? City,
    string? State,
    string? PostalCode,
    string? Country
);

public sealed record OraclePoGrnDto(
    string? PoHeaderId,
    string PoNumber,
    string? PoType,
    string? PoStatus,
    string? InspectionStatus,

    string VendorId,
    string? VendorName,
    string? VendorSiteCode,

    string? PoLineId,
    string? PoLineNum,

    string? ItemCode,
    string? ItemDescription,
    string? Uom,

    decimal? PoQuantity,
    decimal? UnitPrice,
    decimal? PoLineAmount,

    decimal? OrderedQuantity,
    decimal? ReceivedQuantity,
    decimal? BilledQuantity,
    decimal? CancelledQuantity,
    decimal? QuantityAvailableToInvoice,

    string? GrnNumber,
    string? RcvTransactionId,
    string? ShipmentLineId,
    string? ItemId,
    decimal? GrnReceivedQuantity,

    DateTime? ReceiptDate,

    string? CurrencyCode,

    DateTime? PoCreationDate,
    DateTime? PoApprovedDate
);

public sealed record OracleInvoiceDto(
    string VendorId,

    string? OracleInvoiceId,

    string? SupplierNumber,
    string? VendorName,

    string InvoiceNumber,

    DateTime? InvoiceDate,

    decimal InvoiceAmount,
    decimal? AmountPaid,
    decimal? OutstandingAmount,

    string? PaymentStatusFlag,
    string? PaymentStatus,
    string? ApprovalStatus,

    string? PoNumber,
    string? GrnNumber,

    DateTime? ReceiptDate
);
