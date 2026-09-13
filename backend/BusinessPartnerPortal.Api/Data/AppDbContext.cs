using BusinessPartnerPortal.Api.Domain;

using Microsoft.EntityFrameworkCore;

namespace BusinessPartnerPortal.Api.Data;

public sealed class AppDbContext(
    DbContextOptions<AppDbContext> options)
    : DbContext(options)
{
    public DbSet<User> Users =>
        Set<User>();

    public DbSet<Role> Roles =>
        Set<Role>();

    public DbSet<UserRole> UserRoles =>
        Set<UserRole>();

    public DbSet<Feature> Features =>
        Set<Feature>();

    public DbSet<Permission> Permissions =>
        Set<Permission>();

    public DbSet<RolePermission> RolePermissions =>
        Set<RolePermission>();

    public DbSet<UserPermissionOverride> UserPermissionOverrides =>
        Set<UserPermissionOverride>();

    public DbSet<Vendor> Vendors =>
        Set<Vendor>();

    public DbSet<VendorUser> VendorUsers =>
        Set<VendorUser>();

    public DbSet<PurchaseOrder> PurchaseOrders =>
        Set<PurchaseOrder>();

    public DbSet<Grn> Grns =>
        Set<Grn>();

    public DbSet<Invoice> Invoices =>
        Set<Invoice>();

    public DbSet<InvoiceGrn> InvoiceGrns =>
        Set<InvoiceGrn>();

    public DbSet<Document> Documents =>
        Set<Document>();

    public DbSet<InvoiceStatusHistory> InvoiceStatusHistory =>
        Set<InvoiceStatusHistory>();

    public DbSet<Payment> Payments =>
        Set<Payment>();

    public DbSet<InvoiceLineGrnAllocation> InvoiceLineGrnAllocations =>
        Set<InvoiceLineGrnAllocation>();

    public DbSet<IdempotencyRequest> IdempotencyRequests =>
        Set<IdempotencyRequest>();

    public DbSet<PasswordResetToken> PasswordResetTokens =>
        Set<PasswordResetToken>();

    public DbSet<LoginOtpChallenge> LoginOtpChallenges =>
        Set<LoginOtpChallenge>();

    public DbSet<TrustedDevice> TrustedDevices =>
        Set<TrustedDevice>();

    protected override void OnModelCreating(
        ModelBuilder b)
    {
        b.Entity<User>(
            e =>
            {
                e.ToTable(
                    "users",
                    "security"
                );

                e.HasKey(
                    x => x.Id
                );

                Map(e, "id", x => x.Id);
                Map(e, "full_name", x => x.FullName);
                Map(e, "email", x => x.Email);
                Map(e, "password_hash", x => x.PasswordHash);
                Map(e, "user_type", x => x.UserType);
                Map(e, "department_id", x => x.DepartmentId);
                Map(e, "is_super_admin", x => x.IsSuperAdmin);
                Map(e, "is_active", x => x.IsActive);
                Map(e, "mfa_enabled", x => x.MfaEnabled);
                Map(e, "failed_login_attempts", x => x.FailedLoginAttempts);
                Map(e, "lockout_until", x => x.LockoutUntil);
                Map(e, "last_login_at", x => x.LastLoginAt);
                Map(e, "created_at", x => x.CreatedAt);
                Map(e, "updated_at", x => x.UpdatedAt);
            });

        b.Entity<Role>(
            e =>
            {
                e.ToTable(
                    "roles",
                    "security"
                );

                e.HasKey(
                    x => x.Id
                );

                Map(e, "id", x => x.Id);
                Map(e, "code", x => x.Code);
                Map(e, "name", x => x.Name);
                Map(e, "is_active", x => x.IsActive);
            });

        b.Entity<UserRole>(
            e =>
            {
                e.ToTable(
                    "user_roles",
                    "security"
                );

                e.HasKey(
                    x =>
                        new
                        {
                            x.UserId,
                            x.RoleId
                        }
                );

                Map(e, "user_id", x => x.UserId);
                Map(e, "role_id", x => x.RoleId);
            });

        b.Entity<Feature>(
            e =>
            {
                e.ToTable(
                    "features",
                    "security"
                );

                e.HasKey(
                    x => x.Id
                );

                Map(e, "id", x => x.Id);
                Map(e, "code", x => x.Code);
                Map(e, "name", x => x.Name);
                Map(e, "route_path", x => x.RoutePath);
            });

        b.Entity<Permission>(
            e =>
            {
                e.ToTable(
                    "permissions",
                    "security"
                );

                e.HasKey(
                    x => x.Id
                );

                Map(e, "id", x => x.Id);
                Map(e, "feature_id", x => x.FeatureId);
                Map(e, "code", x => x.Code);
                Map(e, "name", x => x.Name);
            });

        b.Entity<RolePermission>(
            e =>
            {
                e.ToTable(
                    "role_permissions",
                    "security"
                );

                e.HasKey(
                    x =>
                        new
                        {
                            x.RoleId,
                            x.PermissionId
                        }
                );

                Map(e, "role_id", x => x.RoleId);
                Map(e, "permission_id", x => x.PermissionId);
            });

        b.Entity<UserPermissionOverride>(
            e =>
            {
                e.ToTable(
                    "user_permission_overrides",
                    "security"
                );

                e.HasKey(
                    x =>
                        new
                        {
                            x.UserId,
                            x.PermissionId
                        }
                );

                Map(e, "user_id", x => x.UserId);
                Map(e, "permission_id", x => x.PermissionId);
                Map(e, "is_allowed", x => x.IsAllowed);
            });

        b.Entity<Vendor>(
            e =>
            {
                e.ToTable(
                    "vendors",
                    "master"
                );

                e.HasKey(
                    x => x.Id
                );

                Map(e, "id", x => x.Id);
                Map(e, "vendor_code", x => x.VendorCode);
                Map(e, "vendor_name", x => x.VendorName);
                Map(e, "oracle_vendor_id", x => x.OracleVendorId);
                Map(e, "is_active", x => x.IsActive);
            });

        b.Entity<VendorUser>(
            e =>
            {
                e.ToTable(
                    "vendor_users",
                    "master"
                );

                e.HasKey(
                    x =>
                        new
                        {
                            x.VendorId,
                            x.UserId
                        }
                );

                Map(e, "vendor_id", x => x.VendorId);
                Map(e, "user_id", x => x.UserId);
                Map(e, "is_primary", x => x.IsPrimary);
                Map(e, "is_active", x => x.IsActive);
            });

        b.Entity<PurchaseOrder>(
            e =>
            {
                e.ToTable(
                    "purchase_orders",
                    "procurement"
                );

                e.HasKey(
                    x => x.Id
                );

                Map(e, "id", x => x.Id);
                Map(e, "vendor_id", x => x.VendorId);
                Map(e, "po_number", x => x.PoNumber);
                Map(e, "po_date", x => x.PoDate);
                Map(e, "currency_code", x => x.CurrencyCode);
                Map(e, "total_amount", x => x.TotalAmount);
                Map(e, "remaining_amount", x => x.RemainingAmount);
                Map(e, "status", x => x.Status);
            });

        b.Entity<Grn>(
            e =>
            {
                e.ToTable(
                    "grns",
                    "procurement"
                );

                e.HasKey(
                    x => x.Id
                );

                Map(e, "id", x => x.Id);
                Map(e, "purchase_order_id", x => x.PurchaseOrderId);
                Map(e, "grn_number", x => x.GrnNumber);
                Map(e, "grn_date", x => x.GrnDate);
                Map(e, "status", x => x.Status);
                Map(e, "qc_status", x => x.QcStatus);
            });

        b.Entity<Invoice>(
            e =>
            {
                e.ToTable(
                    "invoices",
                    "invoice"
                );

                e.HasKey(
                    x => x.Id
                );

                Map(e, "id", x => x.Id);

                e.Property(
                        x => x.PkId
                    )
                    .HasColumnName(
                        "pk_id"
                    )
                    .ValueGeneratedOnAdd();

                Map(e, "vendor_id", x => x.VendorId);
                Map(e, "invoice_number", x => x.InvoiceNumber);
                Map(e, "invoice_date", x => x.InvoiceDate);
                Map(e, "invoice_amount", x => x.InvoiceAmount);
                Map(e, "currency_code", x => x.CurrencyCode);
                Map(e, "po_number", x => x.PoNumber);
                Map(e, "grn_numbers", x => x.GrnNumbers);
                Map(e, "invoice_type", x => x.InvoiceType);
                Map(e, "status", x => x.Status);
                Map(e, "integration_status", x => x.IntegrationStatus);
                Map(e, "oracle_invoice_id", x => x.OracleInvoiceId);
                Map(e, "submission_date", x => x.SubmissionDate);
                Map(e, "remarks", x => x.Remarks);
                Map(e, "created_by", x => x.CreatedBy);
                Map(e, "created_at", x => x.CreatedAt);
                Map(e, "updated_at", x => x.UpdatedAt);
                Map(e, "deleted_at", x => x.DeletedAt);
            });

        b.Entity<InvoiceGrn>(
            e =>
            {
                e.ToTable(
                    "invoice_grns",
                    "invoice"
                );

                e.HasKey(
                    x =>
                        new
                        {
                            x.InvoiceId,
                            x.GrnId
                        }
                );

                Map(e, "invoice_id", x => x.InvoiceId);
                Map(e, "grn_id", x => x.GrnId);
            });

        b.Entity<Document>(
            e =>
            {
                e.ToTable(
                    "documents",
                    "invoice"
                );

                e.HasKey(
                    x => x.Id
                );

                Map(e, "id", x => x.Id);
                Map(e, "invoice_id", x => x.InvoiceId);
                Map(e, "document_type", x => x.DocumentType);
                Map(e, "original_file_name", x => x.OriginalFileName);
                Map(e, "content_type", x => x.ContentType);
                Map(e, "file_extension", x => x.FileExtension);
                Map(e, "file_size", x => x.FileSize);
                Map(e, "file_hash_sha256", x => x.FileHashSha256);
                Map(e, "file_content", x => x.FileContent);
                Map(e, "uploaded_by", x => x.UploadedBy);
                Map(e, "uploaded_at", x => x.UploadedAt);
            });

        b.Entity<InvoiceStatusHistory>(
            e =>
            {
                e.ToTable(
                    "invoice_status_history",
                    "invoice"
                );

                e.HasKey(
                    x => x.Id
                );

                Map(e, "id", x => x.Id);
                Map(e, "invoice_id", x => x.InvoiceId);
                Map(e, "old_status", x => x.OldStatus);
                Map(e, "new_status", x => x.NewStatus);
                Map(e, "remarks", x => x.Remarks);
                Map(e, "source", x => x.Source);
                Map(e, "changed_by", x => x.ChangedBy);
                Map(e, "changed_at", x => x.ChangedAt);
                Map(e, "correlation_id", x => x.CorrelationId);
            });

        b.Entity<Payment>(
            e =>
            {
                e.ToTable(
                    "payments",
                    "payment"
                );

                e.HasKey(
                    x => x.Id
                );

                Map(e, "id", x => x.Id);
                Map(e, "invoice_id", x => x.InvoiceId);
                Map(e, "payment_reference", x => x.PaymentReference);
                Map(e, "payment_date", x => x.PaymentDate);
                Map(e, "amount", x => x.Amount);
                Map(e, "status", x => x.Status);
            });

        b.Entity<InvoiceLineGrnAllocation>(
            e =>
            {
                e.ToTable(
                    "invoice_line_grn_allocations",
                    "invoice"
                );

                e.HasKey(
                    x => x.Id
                );

                Map(e, "id", x => x.Id);
                Map(e, "invoice_id", x => x.InvoiceId);
                Map(e, "vendor_id", x => x.VendorId);
                Map(e, "po_number", x => x.PoNumber);
                Map(e, "grn_number", x => x.GrnNumber);
                Map(e, "rcv_transaction_id", x => x.RcvTransactionId);
                Map(e, "po_header_id", x => x.PoHeaderId);
                Map(e, "po_line_id", x => x.PoLineId);
                Map(e, "po_line_number", x => x.PoLineNumber);
                Map(e, "po_line_location_id", x => x.PoLineLocationId);
                Map(e, "received_quantity", x => x.ReceivedQuantity);
                Map(e, "available_quantity_at_submit", x => x.AvailableQuantityAtSubmit);
                Map(e, "allocated_quantity", x => x.AllocatedQuantity);
                Map(e, "unit_price", x => x.UnitPrice);
                Map(e, "allocated_amount", x => x.AllocatedAmount);
                Map(e, "match_option", x => x.MatchOption);
                Map(e, "created_at", x => x.CreatedAt);
            });

        b.Entity<IdempotencyRequest>(
            e =>
            {
                e.ToTable(
                    "idempotency_requests",
                    "integration"
                );

                e.HasKey(
                    x => x.Id
                );

                Map(e, "id", x => x.Id);
                Map(e, "user_id", x => x.UserId);
                Map(e, "vendor_id", x => x.VendorId);
                Map(e, "idempotency_key", x => x.IdempotencyKey);
                Map(e, "operation", x => x.Operation);
                Map(e, "request_hash", x => x.RequestHash);
                Map(e, "aggregate_id", x => x.AggregateId);
                Map(e, "response_status", x => x.ResponseStatus);
                Map(e, "created_at", x => x.CreatedAt);
                Map(e, "expires_at", x => x.ExpiresAt);
            });

        b.Entity<PasswordResetToken>(
            e =>
            {
                e.ToTable(
                    "password_reset_tokens",
                    "security"
                );

                e.HasKey(
                    x => x.Id
                );

                Map(e, "id", x => x.Id);
                Map(e, "user_id", x => x.UserId);
                Map(e, "token_hash", x => x.TokenHash);
                Map(e, "expires_at", x => x.ExpiresAt);
                Map(e, "used_at", x => x.UsedAt);
                Map(e, "created_at", x => x.CreatedAt);
            });

        b.Entity<LoginOtpChallenge>(
            e =>
            {
                e.ToTable(
                    "login_otp_challenges",
                    "security"
                );

                e.HasKey(
                    x => x.Id
                );

                Map(e, "id", x => x.Id);
                Map(e, "user_id", x => x.UserId);
                Map(e, "otp_hash", x => x.OtpHash);
                Map(e, "expires_at", x => x.ExpiresAt);
                Map(e, "attempts", x => x.Attempts);
                Map(e, "used_at", x => x.UsedAt);
                Map(e, "created_at", x => x.CreatedAt);
            });

        // ========================================================
        // TRUSTED DEVICE
        //
        // IMPORTANT:
        // C# property TokenHash maps to the EXISTING PostgreSQL
        // column security.trusted_devices.device_token_hash.
        // ========================================================

        b.Entity<TrustedDevice>(
            e =>
            {
                e.ToTable(
                    "trusted_devices",
                    "security"
                );

                e.HasKey(
                    x => x.Id
                );

                Map(e, "id", x => x.Id);

                Map(
                    e,
                    "user_id",
                    x => x.UserId
                );

                // FIX:
                // Previously this was "token_hash".
                // Actual database column is "device_token_hash".
                Map(
                    e,
                    "device_token_hash",
                    x => x.TokenHash
                );

                Map(
                    e,
                    "expires_at",
                    x => x.ExpiresAt
                );

                Map(
                    e,
                    "last_used_at",
                    x => x.LastUsedAt
                );

                Map(
                    e,
                    "revoked_at",
                    x => x.RevokedAt
                );

                Map(
                    e,
                    "created_at",
                    x => x.CreatedAt
                );
            });
    }

    private static void Map<T, TProp>(
        Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<T> entity,
        string columnName,
        System.Linq.Expressions.Expression<Func<T, TProp>> property)
        where T : class
    {
        entity
            .Property(
                property
            )
            .HasColumnName(
                columnName
            );
    }
}