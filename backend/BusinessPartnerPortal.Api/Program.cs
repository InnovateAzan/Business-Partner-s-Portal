using System.Text;
using System.Threading.RateLimiting;

using BusinessPartnerPortal.Api.Auth;
using BusinessPartnerPortal.Api.Common;
using BusinessPartnerPortal.Api.Data;

using BusinessPartnerPortal.Api.Features.Admin;
using BusinessPartnerPortal.Api.Features.Auth;
using BusinessPartnerPortal.Api.Features.Dashboard;
using BusinessPartnerPortal.Api.Features.Documents;
using BusinessPartnerPortal.Api.Features.Grns;
using BusinessPartnerPortal.Api.Features.Invoices;
using BusinessPartnerPortal.Api.Features.Integration;
using BusinessPartnerPortal.Api.Features.Notifications;
using BusinessPartnerPortal.Api.Features.Oracle;
using BusinessPartnerPortal.Api.Features.PurchaseOrders;
using BusinessPartnerPortal.Api.Features.Registration;
using BusinessPartnerPortal.Api.Features.VendorAccess;

using BusinessPartnerPortal.Api.Oracle;
using BusinessPartnerPortal.Api.Security;
using BusinessPartnerPortal.Api.Services;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

using Serilog;

// ============================================================
// LOAD ENVIRONMENT
// ============================================================

EnvLoader.Load();

var builder = WebApplication.CreateBuilder(args);

builder.Configuration
    .AddEnvironmentVariables();

// ============================================================
// LOGGING
// ============================================================

Log.Logger =
    new LoggerConfiguration()
        .Enrich
        .FromLogContext()
        .WriteTo
        .Console()
        .CreateLogger();

builder.Host.UseSerilog();

// ============================================================
// POSTGRESQL CONFIGURATION
// ============================================================

var dbHost =
    builder.Configuration["DB_HOST"]
    ?? "localhost";

var dbPort =
    builder.Configuration["DB_PORT"]
    ?? "5432";

var dbName =
    builder.Configuration["DB_NAME"]
    ?? "business_partner_portal";

var dbUser =
    builder.Configuration["DB_USERNAME"]
    ?? "postgres";

var dbPassword =
    builder.Configuration["DB_PASSWORD"]
    ?? throw new InvalidOperationException(
        "DB_PASSWORD is required."
    );

var connectionString =
    $"Host={dbHost};" +
    $"Port={dbPort};" +
    $"Database={dbName};" +
    $"Username={dbUser};" +
    $"Password={dbPassword};" +
    $"Pooling=true;" +
    $"Timeout=15;" +
    $"Command Timeout=30";

builder.Services
    .AddDbContext<AppDbContext>(
        options =>
            options.UseNpgsql(
                connectionString
            )
    );

// ============================================================
// COMMON SERVICES
// ============================================================

builder.Services
    .AddHttpContextAccessor();

builder.Services
    .AddScoped<CurrentUser>();

builder.Services
    .AddSingleton<JwtTokenService>();

// ============================================================
// JWT AUTHENTICATION
// ============================================================

var jwtSecret =
    builder.Configuration["JWT_SECRET"]
    ?? throw new InvalidOperationException(
        "JWT_SECRET is required."
    );

if (jwtSecret.Length < 48)
{
    throw new InvalidOperationException(
        "JWT_SECRET must be at least 48 characters."
    );
}

builder.Services
    .AddAuthentication(
        JwtBearerDefaults.AuthenticationScheme
    )
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters =
            new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,

                ValidIssuer =
                    builder.Configuration[
                        "JWT_ISSUER"
                    ],

                ValidAudience =
                    builder.Configuration[
                        "JWT_AUDIENCE"
                    ],

                IssuerSigningKey =
                    new SymmetricSecurityKey(
                        Encoding.UTF8
                            .GetBytes(
                                jwtSecret
                            )
                    ),

                ClockSkew =
                    TimeSpan.FromSeconds(
                        30
                    )
            };
    });

builder.Services
    .AddAuthorization();

// ============================================================
// CORS
// ============================================================
//
// Supports:
//   http://localhost:5173
//   http://127.0.0.1:5173
//   http://10.1.40.52:5173
//
// You can also define:
//
// FRONTEND_URLS=http://localhost:5173;http://10.1.40.52:5173
//
// or keep the existing:
// FRONTEND_URL=http://10.1.40.52:5173
//
// ============================================================

var configuredFrontendUrls =
    builder.Configuration["FRONTEND_URLS"]
    ?? builder.Configuration["FRONTEND_URL"]
    ?? string.Empty;

var allowedOrigins =
    configuredFrontendUrls
        .Split(
            new[] { ',', ';' },
            StringSplitOptions.RemoveEmptyEntries
        )
        .Select(x => x.Trim().TrimEnd('/'))
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .ToList();

// Always allow local development.
allowedOrigins.Add(
    "http://localhost:5173"
);

allowedOrigins.Add(
    "http://127.0.0.1:5173"
);

// Current LAN frontend.
allowedOrigins.Add(
    "http://10.1.40.52:5173"
);

// Remove duplicate origins.
allowedOrigins =
    allowedOrigins
        .Distinct(
            StringComparer.OrdinalIgnoreCase
        )
        .ToList();

builder.Services
    .AddCors(options =>
    {
        options.AddPolicy(
            "frontend",
            policy =>
            {
                policy
                    .WithOrigins(
                        allowedOrigins.ToArray()
                    )
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials();
            }
        );
    });

// ============================================================
// RATE LIMITING
// ============================================================

builder.Services
    .AddRateLimiter(options =>
    {
        options.RejectionStatusCode =
            StatusCodes
                .Status429TooManyRequests;

        options.AddPolicy(
            "api",
            context =>
                RateLimitPartition
                    .GetFixedWindowLimiter(
                        context.Connection
                            .RemoteIpAddress?
                            .ToString()
                        ?? "unknown",

                        _ =>
                            new FixedWindowRateLimiterOptions
                            {
                                PermitLimit =
                                    180,

                                Window =
                                    TimeSpan
                                        .FromMinutes(
                                            1
                                        ),

                                QueueLimit =
                                    0,

                                AutoReplenishment =
                                    true
                            }
                    )
        );
    });

// ============================================================
// SWAGGER
// ============================================================

builder.Services
    .AddEndpointsApiExplorer();

builder.Services
    .AddSwaggerGen();

var swaggerEnabled =
    !bool.TryParse(
        builder.Configuration[
            "SWAGGER_ENABLED"
        ],
        out var configuredSwagger
    )
    || configuredSwagger;

// ============================================================
// ORACLE CONFIGURATION
// ============================================================

var oracleOptions =
    new OracleOptions
    {
        Host =
            builder.Configuration[
                "ORACLE_HOST"
            ]
            ?? string.Empty,

        Port =
            int.TryParse(
                builder.Configuration[
                    "ORACLE_PORT"
                ],
                out var oraclePort
            )
                ? oraclePort
                : 1521,

        ServiceName =
            builder.Configuration[
                "ORACLE_SERVICE_NAME"
            ]
            ?? string.Empty,

        User =
            builder.Configuration[
                "ORACLE_USER"
            ]
            ?? string.Empty,

        Password =
            builder.Configuration[
                "ORACLE_PASSWORD"
            ]
            ?? string.Empty
    };

builder.Services
    .AddSingleton(
        oracleOptions
    );

builder.Services
    .AddScoped<OracleService>();

// ============================================================
// DATA PROTECTION
// ============================================================

builder.Services
    .AddDataProtection();

// ============================================================
// REGISTRATION / OTP / PASSWORD SETUP
// ============================================================

builder.Services
    .AddSingleton<
        RegistrationOtpStore
    >();

builder.Services
    .AddSingleton<
        PasswordSetupTokenService
    >();

builder.Services
    .AddScoped<
        EmailOtpSender
    >();

// ============================================================
// ORACLE INVOICE HTTP CLIENT
// ============================================================

builder.Services
    .AddHttpClient(
        "oracle-invoice"
    );

// ============================================================
// BACKGROUND WORKERS
// ============================================================

builder.Services
    .AddHostedService<
        OracleInvoiceOutboxWorker
    >();

builder.Services
    .AddHostedService<
        OracleReconciliationWorker
    >();

builder.Services
    .AddHostedService<
        DataRetentionWorker
    >();

// ============================================================
// API URLS
// ============================================================

builder.WebHost
    .UseUrls(
        builder.Configuration[
            "API_URLS"
        ]
        ??
        "http://localhost:5044;" +
        "https://localhost:7044"
    );

// ============================================================
// BUILD APPLICATION
// ============================================================

var app =
    builder.Build();

// ============================================================
// GLOBAL ERROR HANDLING
// ============================================================

app.UseMiddleware<
    GlobalExceptionHandler
>();

app.UseSerilogRequestLogging();

// ============================================================
// HTTPS / HSTS
// ============================================================

if (
    !app.Environment
        .IsDevelopment()
)
{
    app.UseHttpsRedirection();

    app.UseHsts();
}

// ============================================================
// SECURITY HEADERS
// ============================================================

app.Use(
    async (
        context,
        next
    ) =>
    {
        context.Response.Headers[
            "X-Content-Type-Options"
        ] =
            "nosniff";

        context.Response.Headers[
            "X-Frame-Options"
        ] =
            "DENY";

        context.Response.Headers[
            "Referrer-Policy"
        ] =
            "no-referrer";

        await next();
    }
);

// ============================================================
// SWAGGER
// ============================================================

if (swaggerEnabled)
{
    app.UseSwagger();

    app.UseSwaggerUI(
        options =>
        {
            options
                .SwaggerEndpoint(
                    "/swagger/v1/swagger.json",
                    "Business Partner Portal API v1"
                );

            options.RoutePrefix =
                "swagger";
        }
    );
}

// ============================================================
// REQUEST PIPELINE
// ============================================================

// IMPORTANT:
// CORS must run before authentication / authorization.
app.UseCors(
    "frontend"
);

app.UseRateLimiter();

app.UseAuthentication();

/*
 * TEMPORARILY DISABLED
 * ============================================================
 *
 * PostgresRlsMiddleware was opening the EF/Npgsql connection
 * before some existing endpoints.
 *
 * Those endpoints also call OpenAsync() on the same connection,
 * which resulted in:
 *
 * System.InvalidOperationException:
 * Connection already open
 *
 * Vendor isolation at the API/query level remains active.
 *
 * RLS should be re-enabled after the middleware is changed to
 * work safely with the application's existing connection
 * management.
 *
 * DO NOT DELETE PostgresRlsMiddleware.cs.
 */
// app.UseMiddleware<PostgresRlsMiddleware>();

app.UseAuthorization();

// ============================================================
// HEALTH ENDPOINT
// ============================================================

app.MapGet(
        "/health",
        () =>
            Results.Ok(
                new
                {
                    status =
                        "healthy",

                    time =
                        DateTimeOffset
                            .UtcNow
                }
            )
    )
    .AllowAnonymous();

// ============================================================
// AUTHENTICATION
// ============================================================

app.MapAuthEndpoints();

// ============================================================
// SELF REGISTRATION
// ============================================================

app.MapRegistrationEndpoints();

// ============================================================
// ORACLE
// ============================================================

app.MapOracleEndpoints();

// ============================================================
// DASHBOARD
// ============================================================

app.MapDashboardEndpoints();

// ============================================================
// DOCUMENTS
// ============================================================

app.MapDocumentEndpoints();

// ============================================================
// PURCHASE ORDERS
// ============================================================

app.MapPurchaseOrderEndpoints();

// ============================================================
// GRNs
// ============================================================

app.MapGrnEndpoints();

// ============================================================
// INVOICES
// ============================================================

app.MapInvoiceEndpoints();

// ============================================================
// NOTIFICATIONS
// ============================================================

app.MapNotificationEndpoints();

// ============================================================
// INTEGRATION
// ============================================================

app.MapIntegrationEndpoints();

// ============================================================
// VENDOR ACCESS
// ============================================================

app.MapVendorAccessEndpoints();

// ============================================================
// ADMIN VENDOR ONBOARDING
// ============================================================

app.MapAdminVendorAccessEndpoints();

// ============================================================
// ADMIN
// ============================================================

app.MapAdminEndpoints();

// ============================================================
// FALLBACK
// ============================================================

app.MapFallback(
        () =>
            Results.NotFound(
                new
                {
                    message =
                        "Endpoint not found."
                }
            )
    )
    .RequireRateLimiting(
        "api"
    );

// ============================================================
// BOOTSTRAP USERS / ROLES
// ============================================================

await Bootstrapper
    .EnsureUsersAsync(
        app.Services,
        app.Configuration,
        app.Logger
    );

// ============================================================
// START APPLICATION
// ============================================================

app.Run();