namespace BusinessPartnerPortal.Api.Oracle;

public sealed record OracleApPortalInvoice(
    Guid PortalInvoiceId,

    // Portal auto-increment invoice reference.
    long PkId,

    decimal VendorId,
    string PoNumber,
    IReadOnlyList<string> GrnNumbers,
    IReadOnlyList<long> RcvTransactionIds,
    string InvoiceNumber,
    DateOnly InvoiceDate,
    decimal InvoiceAmount,
    string CurrencyCode,
    string InvoiceType,
    string? Description
);

public sealed record OracleApDocument(
    Guid DocumentId,
    string DocumentType,
    string FileName,
    string ContentType,
    byte[] Content
);

public sealed record OracleApProcessResult(
    long OracleInvoiceId,
    long? ConcurrentRequestId,
    string BatchName,
    string Source,
    IReadOnlyList<string> Rejections
);

public sealed record OracleReceiptLine(
    long RcvTransactionId,
    string GrnNumber,

    long PoHeaderId,
    string PoNumber,

    long PoLineId,
    int PoLineNumber,

    long PoLineLocationId,

    long? PoDistributionId,

    decimal ReceivedQuantity,
    decimal AvailableQuantity,

    decimal UnitPrice,

    string MatchOption
)
{
    public decimal ExtendedAmount =>
        AvailableQuantity *
        UnitPrice;
}

public class OracleApBusinessException
    : Exception
{
    public OracleApBusinessException(
        string message)
        : base(message)
    {
    }

    public OracleApBusinessException(
        string message,
        Exception innerException)
        : base(
            message,
            innerException
        )
    {
    }
}

public sealed class OracleApRejectedException
    : OracleApBusinessException
{
    public IReadOnlyList<string> Rejections
    {
        get;
    }

    public OracleApRejectedException(
        IReadOnlyList<string> rejections)
        : base(
            rejections.Count ==
            0
                ?
                "Oracle rejected the invoice interface row."
                :
                $"Oracle rejected the invoice: {string.Join("; ", rejections)}"
        )
    {
        Rejections =
            rejections;
    }
}