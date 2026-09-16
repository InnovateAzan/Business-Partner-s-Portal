using BusinessPartnerPortal.Api.Common;
using BusinessPartnerPortal.Api.Data;
using BusinessPartnerPortal.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace BusinessPartnerPortal.Api.Features.Documents;

public static class DocumentEndpoints
{
    public static IEndpointRouteBuilder MapDocumentEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/v1/documents")
            .RequireAuthorization()
            .WithTags("Documents");

        // Vendor-facing document list.
        group.MapGet(
            "/my",
            async (
                CurrentUser current,
                AppDbContext db,
                CancellationToken ct) =>
            {
                await current.DemandAsync("DOCUMENT.DOWNLOAD", ct);

                var vendorId =
                    await current.GetVendorIdAsync(ct)
                    ?? throw new ApiException(
                        StatusCodes.Status403Forbidden,
                        "Vendor mapping missing."
                    );

                var rows = await (
                    from document in db.Documents
                    join invoice in db.Invoices
                        on document.InvoiceId equals invoice.Id
                    where
                        invoice.VendorId == vendorId
                        && invoice.DeletedAt == null
                    orderby document.UploadedAt descending
                    select new
                    {
                        document.Id,
                        document.InvoiceId,
                        invoice.InvoiceNumber,
                        document.DocumentType,
                        document.OriginalFileName,
                        document.ContentType,
                        document.FileSize,
                        document.UploadedAt
                    }
                ).ToListAsync(ct);

                return Results.Ok(rows);
            }
        );

        // Finance/Admin invoice-document list.
        // Internal users with DOCUMENT.DOWNLOAD can see documents for all
        // non-deleted invoices. Vendor users remain restricted to their own
        // vendor invoices.
        group.MapGet(
            "/invoices",
            async (
                CurrentUser current,
                AppDbContext db,
                CancellationToken ct) =>
            {
                await current.DemandAsync("DOCUMENT.DOWNLOAD", ct);

                var query =
                    from document in db.Documents
                    join invoice in db.Invoices
                        on document.InvoiceId equals invoice.Id
                    where invoice.DeletedAt == null
                    select new
                    {
                        document.Id,
                        document.InvoiceId,
                        invoice.InvoiceNumber,
                        invoice.VendorId,
                        document.DocumentType,
                        document.OriginalFileName,
                        document.ContentType,
                        document.FileSize,
                        document.UploadedAt
                    };

                if (
                    string.Equals(
                        current.UserType,
                        "VENDOR",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    var vendorId =
                        await current.GetVendorIdAsync(ct)
                        ?? throw new ApiException(
                            StatusCodes.Status403Forbidden,
                            "Vendor mapping missing."
                        );

                    query = query.Where(x => x.VendorId == vendorId);
                }

                var rows = await query
                    .OrderByDescending(x => x.UploadedAt)
                    .Select(x => new
                    {
                        x.Id,
                        x.InvoiceId,
                        x.InvoiceNumber,
                        x.DocumentType,
                        x.OriginalFileName,
                        x.ContentType,
                        x.FileSize,
                        x.UploadedAt
                    })
                    .ToListAsync(ct);

                return Results.Ok(rows);
            }
        );

        // Authenticated/authorized download endpoint.
        // Finance/internal users with DOCUMENT.DOWNLOAD can download any
        // invoice document. Vendor users can only download documents that
        // belong to their own vendor account.
        group.MapGet(
            "/{id:guid}",
            async (
                Guid id,
                CurrentUser current,
                AppDbContext db,
                CancellationToken ct) =>
            {
                await current.DemandAsync("DOCUMENT.DOWNLOAD", ct);

                var vendorId =
                    string.Equals(
                        current.UserType,
                        "VENDOR",
                        StringComparison.OrdinalIgnoreCase
                    )
                        ? await current.GetVendorIdAsync(ct)
                        : null;

                var query =
                    from document in db.Documents
                    join invoice in db.Invoices
                        on document.InvoiceId equals invoice.Id
                    where
                        document.Id == id
                        && invoice.DeletedAt == null
                    select new
                    {
                        Document = document,
                        invoice.VendorId
                    };

                if (
                    string.Equals(
                        current.UserType,
                        "VENDOR",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    if (vendorId is null)
                    {
                        throw new ApiException(
                            StatusCodes.Status403Forbidden,
                            "Vendor mapping missing."
                        );
                    }

                    query = query.Where(x => x.VendorId == vendorId.Value);
                }

                var row =
                    await query.FirstOrDefaultAsync(ct)
                    ?? throw new ApiException(
                        StatusCodes.Status404NotFound,
                        "Document not found."
                    );

                return Results.File(
                    row.Document.FileContent,
                    row.Document.ContentType,
                    row.Document.OriginalFileName
                );
            }
        );

        return app;
    }
}
