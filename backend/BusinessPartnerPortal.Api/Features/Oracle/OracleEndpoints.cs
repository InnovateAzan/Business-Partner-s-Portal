using System.Data;

using BusinessPartnerPortal.Api.Common;
using BusinessPartnerPortal.Api.Data;
using BusinessPartnerPortal.Api.Oracle;
using BusinessPartnerPortal.Api.Security;

using Microsoft.EntityFrameworkCore;

using Oracle.ManagedDataAccess.Client;

namespace BusinessPartnerPortal.Api.Features.Oracle;

public static class OracleEndpoints
{
    public static IEndpointRouteBuilder MapOracleEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/v1/oracle")
            .RequireAuthorization()
            .WithTags("Oracle");

        // ------------------------------------------------------------
        // GET: /api/v1/oracle/suppliers/my
        // ------------------------------------------------------------
        group.MapGet(
            "/suppliers/my",
            async (
                CurrentUser current,
                AppDbContext db,
                OracleService oracle,
                CancellationToken ct) =>
            {
                var oracleVendorId =
                    await ResolveOracleVendorId(
                        current,
                        db,
                        ct);

                var supplier =
                    await oracle
                        .GetSupplierByVendorIdAsync(
                            oracleVendorId,
                            ct);

                if (supplier is null)
                {
                    return Results.NotFound(
                        new
                        {
                            message =
                                "Oracle supplier not found."
                        });
                }

                return Results.Ok(
                    supplier);
            });

        // ------------------------------------------------------------
        // GET: /api/v1/oracle/po-grns/my
        // ------------------------------------------------------------
        group.MapGet(
            "/po-grns/my",
            async (
                string? poNumber,
                CurrentUser current,
                AppDbContext db,
                OracleService oracle,
                CancellationToken ct) =>
            {
                var oracleVendorId =
                    await ResolveOracleVendorId(
                        current,
                        db,
                        ct);

                var rows =
                    await oracle
                        .GetPoGrnsAsync(
                            oracleVendorId,
                            poNumber,
                            ct);

                return Results.Ok(
                    rows);
            });

        // ============================================================
        // GET: /api/v1/oracle/receipt-lines/my
        //
        // UPDATED:
        // - Exact Oracle error is now returned instead of generic 500.
        // - Adds logging for PO / GRN receipt-line resolution.
        // - Existing receipt-line logic remains unchanged.
        // ============================================================
        group.MapGet(
            "/receipt-lines/my",
            async (
                string poNumber,
                string? grnNumbers,
                CurrentUser current,
                AppDbContext db,
                OracleService oracleRead,
                OracleOptions options,
                IConfiguration config,
                ILoggerFactory loggerFactory,
                CancellationToken ct) =>
            {
                if (
                    string.IsNullOrWhiteSpace(
                        poNumber))
                {
                    throw new ApiException(
                        400,
                        "PO number is required.");
                }

                var logger =
                    loggerFactory
                        .CreateLogger(
                            "OracleReceiptLines");

                try
                {
                    var oracleVendorId =
                        await ResolveOracleVendorId(
                            current,
                            db,
                            ct);

                    var normalizedPoNumber =
                        poNumber.Trim();

                    var grns =
                        (
                            grnNumbers
                            ??
                            string.Empty
                        )
                        .Split(
                            ',',
                            StringSplitOptions
                                .RemoveEmptyEntries |
                            StringSplitOptions
                                .TrimEntries)
                        .Where(
                            x =>
                                !string.IsNullOrWhiteSpace(
                                    x))
                        .Distinct(
                            StringComparer
                                .OrdinalIgnoreCase)
                        .ToList();

                    // ------------------------------------------------
                    // If frontend did not provide GRNs,
                    // load all GRNs belonging to selected PO.
                    // ------------------------------------------------
                    if (
                        grns.Count == 0)
                    {
                        logger.LogInformation(
                            "Receipt line lookup: loading GRNs from Oracle. VendorId={VendorId}, PO={PoNumber}",
                            oracleVendorId,
                            normalizedPoNumber);

                        var poRows =
                            await oracleRead
                                .GetPoGrnsAsync(
                                    oracleVendorId,
                                    normalizedPoNumber,
                                    ct);

                        grns =
                            poRows
                                .Where(
                                    x =>
                                        !string
                                            .IsNullOrWhiteSpace(
                                                x.GrnNumber))
                                .Select(
                                    x =>
                                        x.GrnNumber!
                                            .Trim())
                                .Distinct(
                                    StringComparer
                                        .OrdinalIgnoreCase)
                                .ToList();
                    }

                    if (
                        grns.Count == 0)
                    {
                        logger.LogInformation(
                            "No GRNs found for VendorId={VendorId}, PO={PoNumber}",
                            oracleVendorId,
                            normalizedPoNumber);

                        return Results.Ok(
                            Array.Empty<
                                OracleReceiptLine>());
                    }

                    logger.LogInformation(
                        "Resolving Oracle receipt lines. VendorId={VendorId}, PO={PoNumber}, GRNs={Grns}",
                        oracleVendorId,
                        normalizedPoNumber,
                        string.Join(
                            ",",
                            grns));

                    var ap =
                        new OracleApInvoiceService(
                            options,
                            oracleRead,
                            config,
                            loggerFactory
                                .CreateLogger<
                                    OracleApInvoiceService>());

                    var lines =
                        await ap
                            .GetReceiptLinesForPortalAsync(
                                oracleVendorId,
                                normalizedPoNumber,
                                grns,
                                ct);

                    logger.LogInformation(
                        "Oracle receipt-line lookup successful. VendorId={VendorId}, PO={PoNumber}, Count={Count}",
                        oracleVendorId,
                        normalizedPoNumber,
                        lines.Count);

                    return Results.Ok(
                        lines);
                }
                catch (
                    OracleApBusinessException ex)
                {
                    logger.LogWarning(
                        ex,
                        "Oracle AP receipt-line validation failed. PO={PoNumber}, GRNs={Grns}",
                        poNumber,
                        grnNumbers);

                    return Results.Problem(
                        detail:
                            ex.Message,

                        statusCode:
                            StatusCodes
                                .Status409Conflict,

                        title:
                            "Oracle receipt-line validation failed");
                }
                catch (
                    OracleException ex)
                {
                    var oracleMessage =
                        CleanOracleMessage(
                            ex);

                    logger.LogError(
                        ex,
                        "Oracle receipt-line query failed. PO={PoNumber}, GRNs={Grns}, OracleError={OracleError}",
                        poNumber,
                        grnNumbers,
                        oracleMessage);

                    return Results.Problem(
                        detail:
                            $"ORA-{ex.Number:D5}: {oracleMessage}",

                        statusCode:
                            StatusCodes
                                .Status503ServiceUnavailable,

                        title:
                            "Oracle receipt-line query failed");
                }
                catch (
                    InvalidCastException ex)
                {
                    logger.LogError(
                        ex,
                        "Oracle receipt-line result mapping failed. PO={PoNumber}, GRNs={Grns}",
                        poNumber,
                        grnNumbers);

                    return Results.Problem(
                        detail:
                            "Oracle receipt-line data could not be mapped to the portal model. " +
                            "Check OracleReceiptLine field order and data types.",

                        statusCode:
                            StatusCodes
                                .Status500InternalServerError,

                        title:
                            "Oracle receipt-line mapping failed");
                }
                catch (
                    IndexOutOfRangeException ex)
                {
                    logger.LogError(
                        ex,
                        "Oracle receipt-line column mapping failed. PO={PoNumber}, GRNs={Grns}",
                        poNumber,
                        grnNumbers);

                    return Results.Problem(
                        detail:
                            "Oracle receipt-line query returned a different number/order of columns " +
                            "than OracleReceiptLine expects.",

                        statusCode:
                            StatusCodes
                                .Status500InternalServerError,

                        title:
                            "Oracle receipt-line column mapping failed");
                }
                catch (
                    Exception ex)
                {
                    logger.LogError(
                        ex,
                        "Unexpected Oracle receipt-line failure. PO={PoNumber}, GRNs={Grns}",
                        poNumber,
                        grnNumbers);

                    return Results.Problem(
                        detail:
                            ex.Message,

                        statusCode:
                            StatusCodes
                                .Status500InternalServerError,

                        title:
                            "Oracle receipt-line lookup failed");
                }
            });

        // ------------------------------------------------------------
        // GET: /api/v1/oracle/invoices/my
        // ------------------------------------------------------------
        group.MapGet(
            "/invoices/my",
            async (
                CurrentUser current,
                AppDbContext db,
                OracleService oracle,
                CancellationToken ct) =>
            {
                var oracleVendorId =
                    await ResolveOracleVendorId(
                        current,
                        db,
                        ct);

                var invoices =
                    await oracle
                        .GetInvoicesAsync(
                            oracleVendorId,
                            ct);

                return Results.Ok(
                    invoices);
            });

        // ------------------------------------------------------------
        // GET: /api/v1/oracle/suppliers
        // Internal/Admin supplier lookup.
        // ------------------------------------------------------------
        group.MapGet(
            "/suppliers",
            async (
                CurrentUser current,
                OracleService oracle,
                CancellationToken ct) =>
            {
                if (
                    string.Equals(
                        current.UserType,
                        "VENDOR",
                        StringComparison
                            .OrdinalIgnoreCase))
                {
                    throw new ApiException(
                        403,
                        "Internal access required.");
                }

                var suppliers =
                    await oracle
                        .GetSuppliersAsync(
                            ct);

                return Results.Ok(
                    suppliers);
            });

        // ------------------------------------------------------------
        // GET: /api/v1/oracle/diagnostics/connectivity
        // ------------------------------------------------------------
        group.MapGet(
            "/diagnostics/connectivity",
            async (
                IWebHostEnvironment env,
                OracleService oracle,
                CancellationToken ct) =>
            {
                if (
                    !env.IsDevelopment())
                {
                    return Results.NotFound();
                }

                try
                {
                    var connected =
                        await oracle
                            .TestConnectivityAsync(
                                ct);

                    return Results.Ok(
                        new
                        {
                            connected,

                            test =
                                "SELECT 1 FROM DUAL",

                            result =
                                1
                        });
                }
                catch (
                    OracleException ex)
                {
                    var oracleMessage =
                        CleanOracleMessage(
                            ex);

                    return Results.Problem(
                        detail:
                            $"ORA-{ex.Number:D5}: {oracleMessage}",

                        statusCode:
                            StatusCodes
                                .Status503ServiceUnavailable,

                        title:
                            "Oracle connectivity failed");
                }
                catch (
                    Exception ex)
                {
                    return Results.Problem(
                        detail:
                            ex.Message,

                        statusCode:
                            StatusCodes
                                .Status503ServiceUnavailable,

                        title:
                            "Oracle connectivity failed");
                }
            });

        // ============================================================
        // GET: /api/v1/oracle/diagnostics/apps-context
        //
        // TEMPORARY diagnostic endpoint.
        // Remove .AllowAnonymous() after Oracle diagnostics complete.
        // ============================================================
        group.MapGet(
            "/diagnostics/apps-context",
            async (
                OracleOptions options,
                IConfiguration config,
                CancellationToken ct) =>
            {
                var userId =
                    GetRequiredLong(
                        config,
                        "ORACLE_APPS_USER_ID");

                var respId =
                    GetRequiredLong(
                        config,
                        "ORACLE_APPS_RESP_ID");

                var respApplId =
                    GetRequiredLong(
                        config,
                        "ORACLE_APPS_RESP_APPL_ID");

                if (
                    string.IsNullOrWhiteSpace(
                        options.Host)
                    ||
                    string.IsNullOrWhiteSpace(
                        options.ServiceName)
                    ||
                    string.IsNullOrWhiteSpace(
                        options.User))
                {
                    return Results.Problem(
                        detail:
                            "Oracle connection configuration is incomplete.",

                        statusCode:
                            StatusCodes
                                .Status503ServiceUnavailable,

                        title:
                            "Oracle Apps context check failed");
                }

                try
                {
                    await using var connection =
                        new OracleConnection(
                            options.ConnectionString);

                    await connection
                        .OpenAsync(
                            ct);

                    // ====================================================
                    // STEP 1 - BASIC CONNECTION
                    // ====================================================

                    await using (
                        var connectionCommand =
                            connection
                                .CreateCommand())
                    {
                        connectionCommand.CommandText =
                            "SELECT 1 FROM DUAL";

                        var basicResult =
                            await connectionCommand
                                .ExecuteScalarAsync(
                                    ct);

                        if (
                            Convert.ToInt32(
                                basicResult) != 1)
                        {
                            return Results.Problem(
                                detail:
                                    "Oracle connection opened but SELECT 1 FROM DUAL returned an unexpected result.",

                                statusCode:
                                    StatusCodes
                                        .Status503ServiceUnavailable,

                                title:
                                    "Oracle Apps context check failed");
                        }
                    }

                    // ====================================================
                    // STEP 2 - INITIALIZE EBS APPS CONTEXT
                    // ====================================================

                    await using (
                        var initCommand =
                            connection
                                .CreateCommand())
                    {
                        initCommand.BindByName =
                            true;

                        initCommand.CommandText =
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

                        AddParameter(
                            initCommand,
                            "user_id",
                            OracleDbType.Int64,
                            userId);

                        AddParameter(
                            initCommand,
                            "resp_id",
                            OracleDbType.Int64,
                            respId);

                        AddParameter(
                            initCommand,
                            "resp_appl_id",
                            OracleDbType.Int64,
                            respApplId);

                        await initCommand
                            .ExecuteNonQueryAsync(
                                ct);
                    }

                    // ====================================================
                    // STEP 3 - READ FND_GLOBAL CONTEXT
                    // ====================================================

                    await using var verifyCommand =
                        connection
                            .CreateCommand();

                    verifyCommand.CommandText =
                        """
                        SELECT
                            fnd_global.user_id,
                            fnd_global.resp_id,
                            fnd_global.resp_appl_id,
                            fnd_global.login_id
                        FROM dual
                        """;

                    await using var reader =
                        await verifyCommand
                            .ExecuteReaderAsync(
                                ct);

                    if (
                        !await reader
                            .ReadAsync(
                                ct))
                    {
                        return Results.Problem(
                            detail:
                                "FND_GLOBAL.APPS_INITIALIZE completed but Oracle returned no context values.",

                            statusCode:
                                StatusCodes
                                    .Status503ServiceUnavailable,

                            title:
                                "Oracle Apps context check failed");
                    }

                    var actualUserId =
                        Convert.ToInt64(
                            reader.GetValue(
                                0));

                    var actualRespId =
                        Convert.ToInt64(
                            reader.GetValue(
                                1));

                    var actualRespApplId =
                        Convert.ToInt64(
                            reader.GetValue(
                                2));

                    long? actualLoginId =
                        reader.IsDBNull(
                            3)
                            ?
                            null
                            :
                            Convert.ToInt64(
                                reader.GetValue(
                                    3));

                    var matches =
                        actualUserId ==
                            userId
                        &&
                        actualRespId ==
                            respId
                        &&
                        actualRespApplId ==
                            respApplId;

                    return Results.Ok(
                        new
                        {
                            success =
                                matches,

                            oracleConnection =
                                "OK",

                            appsInitialize =
                                "OK",

                            configured =
                                new
                                {
                                    userId,
                                    respId,
                                    respApplId
                                },

                            actual =
                                new
                                {
                                    userId =
                                        actualUserId,

                                    respId =
                                        actualRespId,

                                    respApplId =
                                        actualRespApplId,

                                    loginId =
                                        actualLoginId
                                },

                            message =
                                matches
                                    ?
                                    "Oracle EBS Apps context initialized successfully."
                                    :
                                    "Oracle Apps context initialized, but actual values do not match configured IDs."
                        });
                }
                catch (
                    OracleException ex)
                {
                    var oracleMessage =
                        CleanOracleMessage(
                            ex);

                    return Results.Problem(
                        detail:
                            $"ORA-{ex.Number:D5}: {oracleMessage}",

                        statusCode:
                            StatusCodes
                                .Status503ServiceUnavailable,

                        title:
                            "Oracle Apps context check failed");
                }
                catch (
                    Exception ex)
                {
                    return Results.Problem(
                        detail:
                            ex.Message,

                        statusCode:
                            StatusCodes
                                .Status503ServiceUnavailable,

                        title:
                            "Oracle Apps context check failed");
                }
            })
            .AllowAnonymous();

        return app;
    }

    // ------------------------------------------------------------
    // Resolve logged-in portal vendor -> Oracle VENDOR_ID.
    // ------------------------------------------------------------
    private static async Task<decimal> ResolveOracleVendorId(
        CurrentUser current,
        AppDbContext db,
        CancellationToken ct)
    {
        var portalVendorId =
            await current
                .GetVendorIdAsync(
                    ct);

        if (
            portalVendorId is null)
        {
            throw new ApiException(
                403,
                "Vendor account mapping is missing.");
        }

        var oracleVendorIdRaw =
            await db.Vendors
                .AsNoTracking()
                .Where(
                    x =>
                        x.Id ==
                        portalVendorId.Value)
                .Select(
                    x =>
                        x.OracleVendorId)
                .FirstOrDefaultAsync(
                    ct);

        if (
            string.IsNullOrWhiteSpace(
                oracleVendorIdRaw))
        {
            throw new ApiException(
                409,
                "Oracle vendor mapping is missing.");
        }

        if (
            !decimal.TryParse(
                oracleVendorIdRaw,
                out var oracleVendorId))
        {
            throw new ApiException(
                409,
                "Oracle vendor mapping is invalid.");
        }

        return oracleVendorId;
    }

    // ============================================================
    // CONFIG HELPER
    // ============================================================

    private static long GetRequiredLong(
        IConfiguration config,
        string key)
    {
        if (
            !long.TryParse(
                config[key],
                out var value)
            ||
            value <= 0)
        {
            throw new ApiException(
                500,
                $"{key} must be configured with a valid Oracle EBS numeric ID.");
        }

        return value;
    }

    // ============================================================
    // ORACLE PARAMETER HELPER
    // ============================================================

    private static void AddParameter(
        OracleCommand command,
        string name,
        OracleDbType type,
        object value)
    {
        var parameter =
            command.Parameters.Add(
                name,
                type);

        parameter.Value =
            value
            ??
            DBNull.Value;
    }

    // ============================================================
    // ORACLE ERROR HELPER
    // ============================================================

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
                    StringSplitOptions
                        .RemoveEmptyEntries)
                .FirstOrDefault()
            ??
            ex.Message;
    }
}