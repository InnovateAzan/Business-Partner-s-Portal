using System.Data;
using BusinessPartnerPortal.Api.Data;
using BusinessPartnerPortal.Api.Oracle;
using BusinessPartnerPortal.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace BusinessPartnerPortal.Api.Features.Dashboard;

public static class DashboardEndpoints
{
    public static IEndpointRouteBuilder MapDashboardEndpoints(
        this IEndpointRouteBuilder app)
    {
        app.MapGet(
            "/api/v1/dashboard",
            async (
                CurrentUser current,
                AppDbContext db,
                CancellationToken ct) =>
            {
                var vendorId =
                    await current.GetVendorIdAsync(ct);

                var invoices =
                    db.Invoices.Where(
                        x => x.DeletedAt == null);

                if (
                    !current.IsAdmin &&
                    current.UserType == "VENDOR")
                {
                    invoices = invoices.Where(
                        x => x.VendorId == vendorId);
                }

                var data = new
                {
                    totalSubmittedInvoices =
                        await invoices.CountAsync(
                            x => x.Status != "DRAFT",
                            ct),

                    drafts =
                        await invoices.CountAsync(
                            x => x.Status == "DRAFT",
                            ct),

                    inProcess =
                        await invoices.CountAsync(
                            x =>
                                x.Status == "SUBMITTED" ||
                                x.Status == "SENT_TO_ORACLE" ||
                                x.Status == "UNDER_FINANCE_REVIEW" ||
                                x.Status == "RESUBMITTED",
                            ct),

                    validated =
                        await invoices.CountAsync(
                            x => x.Status == "ACCEPTED",
                            ct),

                    rejected =
                        await invoices.CountAsync(
                            x => x.Status == "RETURNED",
                            ct),

                    // NOTE:
                    // Database status is still PAID.
                    // Portal display mapping converts this to
                    // "Pending Payment" in the live dashboard.
                    paid =
                        await invoices.CountAsync(
                            x => x.Status == "PAID",
                            ct),

                    poOutstanding =
                        vendorId.HasValue
                            ? await db.PurchaseOrders.CountAsync(
                                x =>
                                    x.VendorId == vendorId &&
                                    (
                                        x.Status == "OPEN" ||
                                        x.Status == "PARTIAL"
                                    ),
                                ct)
                            : await db.PurchaseOrders.CountAsync(
                                x =>
                                    x.Status == "OPEN" ||
                                    x.Status == "PARTIAL",
                                ct),

                    grnOutstanding =
                        vendorId.HasValue
                            ? await (
                                from g in db.Grns
                                join p in db.PurchaseOrders
                                    on g.PurchaseOrderId equals p.Id
                                where
                                    p.VendorId == vendorId &&
                                    (
                                        g.Status == "OPEN" ||
                                        g.Status == "PARTIAL"
                                    )
                                select g
                            ).CountAsync(ct)
                            : await db.Grns.CountAsync(
                                x =>
                                    x.Status == "OPEN" ||
                                    x.Status == "PARTIAL",
                                ct)
                };

                return Results.Ok(data);
            })
            .RequireAuthorization();

        app.MapGet(
            "/api/v1/dashboard/live",
            async (
                CurrentUser current,
                AppDbContext db,
                OracleService oracleService,
                CancellationToken ct) =>
            {
                if (
                    !current.IsAdmin &&
                    current.UserType == "VENDOR")
                {
                    return Results.Forbid();
                }

                var permissions =
                    await current.GetPermissionsAsync(ct);

                var canFinance =
                    current.IsAdmin ||
                    permissions.Contains("INVOICE.VIEW_ALL");

                var canSupplyChain =
                    current.IsAdmin ||
                    permissions.Contains("VENDOR.MANAGE");

                var now =
                    DateTimeOffset.UtcNow;

                var monthStart =
                    new DateTimeOffset(
                        now.Year,
                        now.Month,
                        1,
                        0,
                        0,
                        0,
                        TimeSpan.Zero);

                var sixMonthStart =
                    monthStart.AddMonths(-5);

                var vendors =
                    await db.Vendors
                        .AsNoTracking()
                        .OrderBy(x => x.VendorName)
                        .ToListAsync(ct);

                // =====================================================
                // TOTAL UNIQUE VENDORS FROM ORACLE
                // =====================================================

                var oracleVendorCount =
                    await oracleService
                        .GetOracleVendorCountAsync(ct);

                var users =
                    await db.Users
                        .AsNoTracking()
                        .ToListAsync(ct);

                var roles =
                    await db.Roles
                        .AsNoTracking()
                        .Where(x => x.IsActive)
                        .ToListAsync(ct);

                var userRoles =
                    await db.UserRoles
                        .AsNoTracking()
                        .ToListAsync(ct);

                var vendorUsers =
                    await db.VendorUsers
                        .AsNoTracking()
                        .ToListAsync(ct);

                var purchaseOrders =
                    await db.PurchaseOrders
                        .AsNoTracking()
                        .ToListAsync(ct);

                var grns =
                    await db.Grns
                        .AsNoTracking()
                        .ToListAsync(ct);

                var invoices =
                    await db.Invoices
                        .AsNoTracking()
                        .Where(
                            x =>
                                x.DeletedAt == null &&
                                x.Status != "DRAFT")
                        .ToListAsync(ct);

                var payments =
                    await db.Payments
                        .AsNoTracking()
                        .ToListAsync(ct);

                static string[] SplitCsv(string? value) =>
                    string.IsNullOrWhiteSpace(value)
                        ? Array.Empty<string>()
                        : value.Split(
                            ',',
                            StringSplitOptions.RemoveEmptyEntries |
                            StringSplitOptions.TrimEntries);

                // =====================================================
                // NORMALIZE INVOICE STATUS FOR PORTAL DISPLAY
                // =====================================================

                static string NormalizeInvoiceStatus(
                    string status,
                    string integrationStatus,
                    string? oracleInvoiceId)
                {
                    if (
                        !string.IsNullOrWhiteSpace(oracleInvoiceId) ||
                        integrationStatus.Equals(
                            "PROCESSED",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return "Integrated";
                    }

                    if (
                        status.Equals(
                            "RETURNED",
                            StringComparison.OrdinalIgnoreCase) ||
                        status.Equals(
                            "INTEGRATION_FAILED",
                            StringComparison.OrdinalIgnoreCase) ||
                        integrationStatus.Equals(
                            "FAILED",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return "Rejected";
                    }

                    if (
                        status.Equals(
                            "UNDER_FINANCE_REVIEW",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return "Under Review";
                    }

                    // =================================================
                    // CHANGED:
                    // DB status PAID is currently the final stage
                    // before actual payment completion.
                    // Therefore portal shows "Pending Payment".
                    // =================================================

                    if (
                        status.Equals(
                            "PAID",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return "Pending Payment";
                    }

                    return "Pending";
                }

                var vendorById =
                    vendors.ToDictionary(
                        x => x.Id,
                        x => x);

                var userById =
                    users.ToDictionary(
                        x => x.Id,
                        x => x);

                var roleById =
                    roles.ToDictionary(
                        x => x.Id,
                        x => x);

                var grnsByPo =
                    grns.GroupBy(
                            x => x.PurchaseOrderId)
                        .ToDictionary(
                            g => g.Key,
                            g => g.ToList());

                var paymentsByInvoice =
                    payments.GroupBy(
                            x => x.InvoiceId)
                        .ToDictionary(
                            g => g.Key,
                            g => g.ToList());

                var invoiceRows =
                    invoices
                        .OrderByDescending(x => x.UpdatedAt)
                        .Select(x =>
                        {
                            vendorById.TryGetValue(
                                x.VendorId,
                                out var vendor);

                            paymentsByInvoice.TryGetValue(
                                x.Id,
                                out var invoicePayments);

                            var paidAmount =
                                invoicePayments?
                                    .Where(
                                        p =>
                                            !p.Status.Equals(
                                                "FAILED",
                                                StringComparison.OrdinalIgnoreCase))
                                    .Sum(p => p.Amount)
                                ?? 0m;

                            return new
                            {
                                id = x.Id,
                                invoiceNumber = x.InvoiceNumber,
                                vendorId = x.VendorId,
                                vendorName =
                                    vendor?.VendorName ?? "-",
                                invoiceDate = x.InvoiceDate,
                                invoiceAmount = x.InvoiceAmount,
                                currencyCode = x.CurrencyCode,
                                poNumbers = SplitCsv(x.PoNumber),
                                grnNumbers = SplitCsv(x.GrnNumbers),

                                status =
                                    NormalizeInvoiceStatus(
                                        x.Status,
                                        x.IntegrationStatus,
                                        x.OracleInvoiceId),

                                rawStatus = x.Status,
                                integrationStatus = x.IntegrationStatus,
                                oracleInvoiceNo = x.OracleInvoiceId,
                                submissionDate = x.SubmissionDate,
                                updatedAt = x.UpdatedAt,
                                paidAmount,

                                outstandingAmount =
                                    Math.Max(
                                        0m,
                                        x.InvoiceAmount - paidAmount)
                            };
                        })
                        .ToList();

                var invoiceStatus = new
                {
                    total =
                        invoiceRows.Count,

                    integrated =
                        invoiceRows.Count(
                            x => x.status == "Integrated"),

                    pending =
                        invoiceRows.Count(
                            x => x.status == "Pending"),

                    rejected =
                        invoiceRows.Count(
                            x => x.status == "Rejected"),

                    underReview =
                        invoiceRows.Count(
                            x => x.status == "Under Review"),

                    // Keep property name "paid" for frontend/API
                    // compatibility, but it now counts rows displayed
                    // as "Pending Payment".
                    paid =
                        invoiceRows.Count(
                            x => x.status == "Pending Payment")
                };

                var invoiceTrend =
                    Enumerable.Range(0, 6)
                        .Select(
                            offset =>
                                sixMonthStart.AddMonths(offset))
                        .Select(month => new
                        {
                            month =
                                month.ToString("MMM"),

                            submitted =
                                invoices.Count(
                                    x =>
                                        (x.SubmissionDate ?? x.CreatedAt).Year ==
                                            month.Year &&
                                        (x.SubmissionDate ?? x.CreatedAt).Month ==
                                            month.Month),

                            integrated =
                                invoices.Count(
                                    x =>
                                        (x.SubmissionDate ?? x.CreatedAt).Year ==
                                            month.Year &&
                                        (x.SubmissionDate ?? x.CreatedAt).Month ==
                                            month.Month &&
                                        NormalizeInvoiceStatus(
                                            x.Status,
                                            x.IntegrationStatus,
                                            x.OracleInvoiceId) ==
                                        "Integrated")
                        })
                        .ToList();

                var topInvoiceVendors =
                    invoices
                        .GroupBy(x => x.VendorId)
                        .Select(g => new
                        {
                            vendorId = g.Key,

                            vendorName =
                                vendorById.TryGetValue(
                                    g.Key,
                                    out var v)
                                    ? v.VendorName
                                    : "-",

                            amount =
                                g.Sum(x => x.InvoiceAmount)
                        })
                        .OrderByDescending(x => x.amount)
                        .Take(5)
                        .ToList();

                var purchaseOrderRows =
                    purchaseOrders
                        .OrderByDescending(x => x.PoDate)
                        .ThenByDescending(x => x.PoNumber)
                        .Select(po =>
                        {
                            vendorById.TryGetValue(
                                po.VendorId,
                                out var vendor);

                            grnsByPo.TryGetValue(
                                po.Id,
                                out var poGrns);

                            poGrns ??=
                                new List<
                                    BusinessPartnerPortal.Api.Domain.Grn>();

                            var relatedInvoices =
                                invoices
                                    .Where(
                                        inv =>
                                            SplitCsv(inv.PoNumber)
                                                .Contains(
                                                    po.PoNumber,
                                                    StringComparer.OrdinalIgnoreCase))
                                    .ToList();

                            var grnStatus =
                                poGrns.Count == 0
                                    ? "Pending"
                                    : poGrns.All(
                                        g =>
                                            g.Status.Equals(
                                                "CLOSED",
                                                StringComparison.OrdinalIgnoreCase))
                                        ? "Received"
                                        : poGrns.Any(
                                            g =>
                                                g.Status.Equals(
                                                    "PARTIAL",
                                                    StringComparison.OrdinalIgnoreCase))
                                            ? "Partially Received"
                                            : "Received";

                            var invoiceStatusText =
                                relatedInvoices.Count == 0
                                    ? "Not Invoiced"
                                    : relatedInvoices.Any(
                                        inv =>
                                            NormalizeInvoiceStatus(
                                                inv.Status,
                                                inv.IntegrationStatus,
                                                inv.OracleInvoiceId) ==
                                            "Integrated")
                                        ? "Invoiced"
                                        : "Pending";

                            return new
                            {
                                id = po.Id,
                                poNumber = po.PoNumber,
                                vendorId = po.VendorId,
                                vendorName =
                                    vendor?.VendorName ?? "-",
                                poDate = po.PoDate,
                                amount = po.TotalAmount,
                                remainingAmount = po.RemainingAmount,
                                currencyCode = po.CurrencyCode,
                                status = po.Status,
                                grnStatus,
                                invoiceStatus = invoiceStatusText
                            };
                        })
                        .ToList();

                var grnRows =
                    (
                        from g in grns
                        join po in purchaseOrders
                            on g.PurchaseOrderId equals po.Id

                        let vendor =
                            vendorById.TryGetValue(
                                po.VendorId,
                                out var v)
                                ? v
                                : null

                        orderby
                            g.GrnDate descending,
                            g.GrnNumber descending

                        select new
                        {
                            id = g.Id,
                            grnNumber = g.GrnNumber,
                            grnDate = g.GrnDate,
                            status = g.Status,
                            qcStatus = g.QcStatus,
                            poNumber = po.PoNumber,
                            vendorId = po.VendorId,
                            vendorName =
                                vendor?.VendorName ?? "-",
                            poAmount = po.TotalAmount
                        }
                    ).ToList();

                var poTrend =
                    Enumerable.Range(0, 6)
                        .Select(
                            offset =>
                                sixMonthStart.AddMonths(offset))
                        .Select(month => new
                        {
                            month =
                                month.ToString("MMM"),

                            purchaseOrders =
                                purchaseOrders.Count(
                                    x =>
                                        x.PoDate.HasValue &&
                                        x.PoDate.Value.Year == month.Year &&
                                        x.PoDate.Value.Month == month.Month),

                            grns =
                                grns.Count(
                                    x =>
                                        x.GrnDate.HasValue &&
                                        x.GrnDate.Value.Year == month.Year &&
                                        x.GrnDate.Value.Month == month.Month)
                        })
                        .ToList();

                var topPoVendors =
                    purchaseOrders
                        .GroupBy(x => x.VendorId)
                        .Select(g => new
                        {
                            vendorId = g.Key,

                            vendorName =
                                vendorById.TryGetValue(
                                    g.Key,
                                    out var v)
                                    ? v.VendorName
                                    : "-",

                            amount =
                                g.Sum(x => x.TotalAmount)
                        })
                        .OrderByDescending(x => x.amount)
                        .Take(5)
                        .ToList();

                var activePortalUserIds =
                    vendorUsers
                        .Where(x => x.IsActive)
                        .Select(x => x.UserId)
                        .Distinct()
                        .ToHashSet();

                var vendorAccessRows =
                    vendors
                        .Select(v =>
                        {
                            var mappings =
                                vendorUsers
                                    .Where(
                                        x => x.VendorId == v.Id)
                                    .ToList();

                            var mappedUsers =
                                mappings
                                    .Select(
                                        m =>
                                            userById.TryGetValue(
                                                m.UserId,
                                                out var u)
                                                ? u
                                                : null)
                                    .Where(u => u != null)
                                    .ToList();

                            var activeUsers =
                                mappings.Count(
                                    m =>
                                        m.IsActive &&
                                        userById.TryGetValue(
                                            m.UserId,
                                            out var u) &&
                                        u.IsActive);

                            var lastAccess =
                                mappedUsers
                                    .Select(u => u!.LastLoginAt)
                                    .Where(x => x.HasValue)
                                    .OrderByDescending(x => x)
                                    .FirstOrDefault();

                            return new
                            {
                                vendorId = v.Id,
                                vendorCode = v.VendorCode,
                                vendorName = v.VendorName,
                                oracleVendorId = v.OracleVendorId,
                                portalUsers = mappings.Count,
                                activePortalUsers = activeUsers,

                                accessStatus =
                                    activeUsers > 0
                                        ? "Enabled"
                                        : "Not Enabled",

                                lastAccess,
                                isActive = v.IsActive
                            };
                        })
                        .OrderByDescending(x => x.lastAccess)
                        .ThenBy(x => x.vendorName)
                        .ToList();

                var pendingVendorRequests =
                    vendorAccessRows
                        .Where(
                            x =>
                                x.isActive &&
                                x.activePortalUsers == 0)
                        .ToList();

                var recentlyOnboardedVendors =
                    vendorUsers
                        .Where(x => x.IsActive)
                        .Select(x => new
                        {
                            mapping = x,

                            vendor =
                                vendorById.TryGetValue(
                                    x.VendorId,
                                    out var v)
                                    ? v
                                    : null,

                            user =
                                userById.TryGetValue(
                                    x.UserId,
                                    out var u)
                                    ? u
                                    : null
                        })
                        .Where(
                            x =>
                                x.vendor != null &&
                                x.user != null)
                        .OrderByDescending(
                            x => x.user!.CreatedAt)
                        .Select(x => new
                        {
                            vendorId = x.vendor!.Id,
                            vendorName = x.vendor.VendorName,
                            oracleVendorId = x.vendor.OracleVendorId,
                            userName = x.user!.FullName,
                            email = x.user.Email,

                            active =
                                x.mapping.IsActive &&
                                x.user.IsActive,

                            onboardedAt = x.user.CreatedAt,
                            lastAccess = x.user.LastLoginAt
                        })
                        .Take(100)
                        .ToList();

                var recentUsers =
                    users
                        .OrderByDescending(x => x.CreatedAt)
                        .Take(100)
                        .Select(u => new
                        {
                            id = u.Id,
                            name = u.FullName,
                            email = u.Email,
                            userType = u.UserType,

                            status =
                                u.IsActive
                                    ? "Active"
                                    : "Inactive",

                            createdOn = u.CreatedAt,
                            lastLoginAt = u.LastLoginAt,

                            roles =
                                userRoles
                                    .Where(
                                        ur => ur.UserId == u.Id)
                                    .Select(
                                        ur =>
                                            roleById.TryGetValue(
                                                ur.RoleId,
                                                out var r)
                                                ? r.Name
                                                : null)
                                    .Where(x => x != null)
                                    .ToArray()
                        })
                        .ToList();

                var roleDistribution =
                    roles
                        .Select(r => new
                        {
                            code = r.Code,
                            name = r.Name,

                            count =
                                userRoles.Count(
                                    ur => ur.RoleId == r.Id)
                        })
                        .Where(x => x.count > 0)
                        .OrderByDescending(x => x.count)
                        .ToList();

                var userTrend =
                    Enumerable.Range(0, 6)
                        .Select(
                            offset =>
                                sixMonthStart.AddMonths(offset))
                        .Select(month => new
                        {
                            month =
                                month.ToString("MMM"),

                            newUsers =
                                users.Count(
                                    x =>
                                        x.CreatedAt.Year == month.Year &&
                                        x.CreatedAt.Month == month.Month),

                            activeUsers =
                                users.Count(
                                    x =>
                                        x.IsActive &&
                                        x.CreatedAt <= month.AddMonths(1))
                        })
                        .ToList();

                var integrationRows =
                    new List<IntegrationQueueItem>();

                var auditRows =
                    new List<AuditDashboardItem>();

                var connection =
                    db.Database.GetDbConnection();

                if (
                    connection.State != ConnectionState.Open)
                {
                    await connection.OpenAsync(ct);
                }

                await using (
                    var command =
                        connection.CreateCommand())
                {
                    command.CommandText =
                        """
                        SELECT id, event_type, aggregate_id, status, attempt_count, last_error,
                               created_at, processed_at, external_reference, oracle_request_id, oracle_group_id
                        FROM integration.outbox_messages
                        ORDER BY created_at DESC
                        LIMIT 500
                        """;

                    await using var reader =
                        await command.ExecuteReaderAsync(ct);

                    while (
                        await reader.ReadAsync(ct))
                    {
                        integrationRows.Add(
                            new IntegrationQueueItem(
                                reader.GetGuid(0),
                                reader.GetString(1),
                                reader.GetGuid(2),
                                reader.GetString(3),
                                reader.GetInt32(4),

                                reader.IsDBNull(5)
                                    ? null
                                    : reader.GetString(5),

                                reader.GetFieldValue<DateTimeOffset>(6),

                                reader.IsDBNull(7)
                                    ? null
                                    : reader.GetFieldValue<DateTimeOffset>(7),

                                reader.IsDBNull(8)
                                    ? null
                                    : reader.GetString(8),

                                reader.IsDBNull(9)
                                    ? null
                                    : reader.GetInt64(9),

                                reader.IsDBNull(10)
                                    ? null
                                    : reader.GetString(10)));
                    }
                }

                if (current.IsAdmin)
                {
                    await using var command =
                        connection.CreateCommand();

                    command.CommandText =
                        """
                        SELECT id, user_id, action, entity_type, entity_id, created_at
                        FROM audit.audit_logs
                        ORDER BY created_at DESC
                        LIMIT 100
                        """;

                    await using var reader =
                        await command.ExecuteReaderAsync(ct);

                    while (
                        await reader.ReadAsync(ct))
                    {
                        auditRows.Add(
                            new AuditDashboardItem(
                                reader.GetInt64(0),

                                reader.IsDBNull(1)
                                    ? null
                                    : reader.GetGuid(1),

                                reader.GetString(2),
                                reader.GetString(3),

                                reader.IsDBNull(4)
                                    ? null
                                    : reader.GetGuid(4),

                                reader.GetFieldValue<DateTimeOffset>(5)));
                    }
                }

                var recentActivities =
                    auditRows
                        .Select(a => new
                        {
                            id = a.Id,
                            action = a.Action,
                            entityType = a.EntityType,
                            entityId = a.EntityId,
                            createdAt = a.CreatedAt,

                            userName =
                                a.UserId.HasValue &&
                                userById.TryGetValue(
                                    a.UserId.Value,
                                    out var u)
                                    ? u.FullName
                                    : null,

                            userEmail =
                                a.UserId.HasValue &&
                                userById.TryGetValue(
                                    a.UserId.Value,
                                    out var ue)
                                    ? ue.Email
                                    : null
                        })
                        .ToList();

                var integrationSummary =
                    integrationRows
                        .GroupBy(x => x.EventType)
                        .Select(g => new
                        {
                            process = g.Key,
                            total = g.Count(),

                            success =
                                g.Count(
                                    x =>
                                        x.Status.Equals(
                                            "PROCESSED",
                                            StringComparison.OrdinalIgnoreCase) ||
                                        x.Status.Equals(
                                            "COMPLETED",
                                            StringComparison.OrdinalIgnoreCase)),

                            failed =
                                g.Count(
                                    x =>
                                        x.Status.Equals(
                                            "FAILED",
                                            StringComparison.OrdinalIgnoreCase)),

                            pending =
                                g.Count(
                                    x =>
                                        x.Status.Equals(
                                            "PENDING",
                                            StringComparison.OrdinalIgnoreCase) ||
                                        x.Status.Equals(
                                            "PROCESSING",
                                            StringComparison.OrdinalIgnoreCase))
                        })
                        .OrderByDescending(x => x.total)
                        .ToList();

                return Results.Ok(
                    new
                    {
                        generatedAt = now,

                        finance =
                            canFinance
                                ? new
                                {
                                    kpis = new
                                    {
                                        totalInvoices =
                                            invoiceStatus.total,

                                        integratedInOracle =
                                            invoiceStatus.integrated,

                                        pendingInOracle =
                                            invoiceStatus.pending,

                                        rejectedInOracle =
                                            invoiceStatus.rejected,

                                        underReview =
                                            invoiceStatus.underReview,

                                        // Existing API property retained
                                        // so frontend does not break.
                                        // Count now represents
                                        // Pending Payment rows.
                                        recentlyPaid =
                                            invoiceStatus.paid,

                                        payments =
                                            payments.Count,

                                        purchaseOrders =
                                            purchaseOrders.Count,

                                        grns =
                                            grns.Count
                                    },

                                    status =
                                        invoiceStatus,

                                    trend =
                                        invoiceTrend,

                                    topVendors =
                                        topInvoiceVendors,

                                    invoices =
                                        invoiceRows
                                }
                                : null,

                        supplyChain =
                            canSupplyChain
                                ? new
                                {
                                    kpis = new
                                    {
                                        totalVendors =
                                            oracleVendorCount,

                                        activePortalUsers =
                                            activePortalUserIds.Count,

                                        purchaseOrders =
                                            purchaseOrders.Count,

                                        grnsReceived =
                                            grns.Count,

                                        openVendorRequests =
                                            pendingVendorRequests.Count
                                    },

                                    accessStatus = new
                                    {
                                        total =
                                            vendors.Count,

                                        portalEnabled =
                                            vendorAccessRows.Count(
                                                x =>
                                                    x.activePortalUsers > 0),

                                        notEnabled =
                                            vendorAccessRows.Count(
                                                x =>
                                                    x.activePortalUsers == 0),

                                        pendingApproval =
                                            0
                                    },

                                    trend =
                                        poTrend,

                                    topVendors =
                                        topPoVendors,

                                    purchaseOrders =
                                        purchaseOrderRows,

                                    grns =
                                        grnRows,

                                    pendingVendorRequests,

                                    recentlyOnboardedVendors,

                                    vendorAccess =
                                        vendorAccessRows
                                }
                                : null,

                        admin =
                            current.IsAdmin
                                ? new
                                {
                                    kpis = new
                                    {
                                        totalUsers =
                                            users.Count,

                                        userRoles =
                                            roles.Count,

                                        totalVendors =
                                            oracleVendorCount,

                                        totalInvoices =
                                            invoices.Count,

                                        integrationErrors =
                                            integrationRows.Count(
                                                x =>
                                                    x.Status.Equals(
                                                        "FAILED",
                                                        StringComparison.OrdinalIgnoreCase))
                                    },

                                    roleDistribution,

                                    userTrend,

                                    recentActivities,

                                    recentUsers,

                                    recentVendors =
                                        vendorAccessRows.Take(100),

                                    integrationQueue =
                                        integrationSummary
                                }
                                : null
                    });
            })
            .RequireAuthorization();

        return app;
    }

    private sealed record IntegrationQueueItem(
        Guid Id,
        string EventType,
        Guid AggregateId,
        string Status,
        int AttemptCount,
        string? LastError,
        DateTimeOffset CreatedAt,
        DateTimeOffset? ProcessedAt,
        string? ExternalReference,
        long? OracleRequestId,
        string? OracleGroupId);

    private sealed record AuditDashboardItem(
        long Id,
        Guid? UserId,
        string Action,
        string EntityType,
        Guid? EntityId,
        DateTimeOffset CreatedAt);
}