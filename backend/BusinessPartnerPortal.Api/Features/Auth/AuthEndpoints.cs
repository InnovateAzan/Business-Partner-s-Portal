using BusinessPartnerPortal.Api.Auth;
using BusinessPartnerPortal.Api.Common;
using BusinessPartnerPortal.Api.Data;
using BusinessPartnerPortal.Api.Domain;
using BusinessPartnerPortal.Api.Security;
using BusinessPartnerPortal.Api.Services;

using System.Security.Cryptography;
using System.Text;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BusinessPartnerPortal.Api.Features.Auth;

public static class AuthEndpoints
{
    private const bool DeviceOtpEnabled = true;

    public sealed record LoginRequest(
        string Email,
        string Password
    );

    public sealed record SetPasswordRequest(
        string Token,
        string Password,
        string ConfirmPassword
    );

    public sealed record ForgotPasswordRequest(
        string Email
    );

    public sealed record ResetPasswordRequest(
        string Token,
        string Password,
        string ConfirmPassword
    );

    public sealed record VerifyLoginOtpRequest(
        Guid ChallengeId,
        string Otp
    );

    public static IEndpointRouteBuilder MapAuthEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group =
            app.MapGroup(
                    "/api/v1/auth"
                )
                .WithTags(
                    "Authentication"
                );

        // ========================================================
        // LOGIN
        // ========================================================

        group.MapPost(
            "/login",
            async (
                LoginRequest request,
                AppDbContext db,
                JwtTokenService jwt,
                EmailOtpSender emailSender,
                IConfiguration config,
                HttpContext http,
                CancellationToken ct) =>
            {
                if (
                    string.IsNullOrWhiteSpace(
                        request.Email
                    )
                    ||
                    string.IsNullOrWhiteSpace(
                        request.Password
                    )
                )
                {
                    throw new ApiException(
                        400,
                        "Email and password are required."
                    );
                }

                var email =
                    request.Email
                        .Trim()
                        .ToLowerInvariant();

                var user =
                    await db.Users
                        .FirstOrDefaultAsync(
                            x =>
                                x.Email.ToLower() ==
                                email,
                            ct
                        );

                if (
                    user is null
                    ||
                    !user.IsActive
                )
                {
                    throw new ApiException(
                        401,
                        "Invalid email address or password."
                    );
                }

                // ====================================================
                // PASSWORD HASH CAN BE NULL FOR USERS THAT HAVE NOT
                // CREATED THEIR PASSWORD YET.
                // ====================================================

                var passwordHash =
                    user.PasswordHash;

                if (
                    string.IsNullOrWhiteSpace(
                        passwordHash
                    )
                )
                {
                    throw new ApiException(
                        401,
                        "Invalid email address or password."
                    );
                }

                // ====================================================
                // ACCOUNT LOCK
                // ====================================================

                if (
                    user.LockoutUntil is not null
                )
                {
                    if (
                        user.LockoutUntil >
                        DateTimeOffset.UtcNow
                    )
                    {
                        throw new ApiException(
                            423,
                            $"Account is temporarily locked until {user.LockoutUntil:dd-MMM-yyyy hh:mm tt}."
                        );
                    }

                    // Lock period has expired. Start a fresh attempt cycle.
                    user.LockoutUntil = null;
                    user.FailedLoginAttempts = 0;
                }

                // ====================================================
                // PASSWORD VERIFICATION
                // ====================================================

                var verification =
                    new PasswordHasher<User>()
                        .VerifyHashedPassword(
                            user,
                            passwordHash,
                            request.Password
                        );

                if (
                    verification ==
                    PasswordVerificationResult.Failed
                )
                {
                    user.FailedLoginAttempts++;

                    if (
                        user.FailedLoginAttempts >=
                        5
                    )
                    {
                        user.FailedLoginAttempts = 5;

                        user.LockoutUntil =
                            DateTimeOffset.UtcNow
                                .AddMinutes(
                                    15
                                );
                    }

                    user.UpdatedAt =
                        DateTimeOffset.UtcNow;

                    await db.SaveChangesAsync(
                        ct
                    );

                    if (user.LockoutUntil is not null)
                    {
                        throw new ApiException(
                            423,
                            "Account has been temporarily locked after 5 failed login attempts."
                        );
                    }

                    throw new ApiException(
                        401,
                        "Invalid email address or password."
                    );
                }

                // ====================================================
                // SUCCESSFUL LOGIN
                // ====================================================

                user.FailedLoginAttempts =
                    0;

                user.LockoutUntil =
                    null;

                user.LastLoginAt =
                    DateTimeOffset.UtcNow;

                user.UpdatedAt =
                    DateTimeOffset.UtcNow;

                await db.SaveChangesAsync(
                    ct
                );

                var otpEnabled =
                    !bool.TryParse(
                        config[
                            "LOGIN_OTP_ENABLED"
                        ],
                        out var enabled
                    )
                    ||
                    enabled;

                if (!otpEnabled)
                {
                    return await CreateLoginResponseAsync(
                        user,
                        db,
                        jwt,
                        ct
                    );
                }

                // ====================================================
                // OPTIONAL VENDOR DEVICE OTP
                // ====================================================

                if (
                    DeviceOtpEnabled
                    &&
                    string.Equals(
                        user.UserType,
                        "VENDOR",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    var deviceToken =
                        http.Request.Cookies[
                            "bpp_trusted_device"
                        ];

                    var deviceHash =
                        string.IsNullOrWhiteSpace(
                            deviceToken
                        )
                            ? null
                            : Hash(
                                deviceToken
                            );

                    TrustedDevice? trustedDevice =
                        deviceHash is null
                            ? null
                            : await db.TrustedDevices
                                .FirstOrDefaultAsync(
                                    x =>
                                        x.UserId ==
                                        user.Id
                                        &&
                                        x.TokenHash ==
                                        deviceHash
                                        &&
                                        x.RevokedAt ==
                                        null
                                        &&
                                        x.ExpiresAt >
                                        DateTimeOffset.UtcNow,
                                    ct
                                );

                    if (
                        trustedDevice is not null
                    )
                    {
                        trustedDevice.LastUsedAt =
                            DateTimeOffset.UtcNow;

                        await db.SaveChangesAsync(
                            ct
                        );
                    }
                    else
                    {
                        var now =
                            DateTimeOffset.UtcNow;

                        var previous =
                            await db.LoginOtpChallenges
                                .Where(
                                    x =>
                                        x.UserId ==
                                        user.Id
                                        &&
                                        x.UsedAt ==
                                        null
                                        &&
                                        x.ExpiresAt >
                                        now
                                )
                                .ToListAsync(
                                    ct
                                );

                        previous.ForEach(
                            x =>
                                x.UsedAt =
                                now
                        );

                        var otp =
                            RandomNumberGenerator
                                .GetInt32(
                                    100000,
                                    1000000
                                )
                                .ToString();

                        var challenge =
                            new LoginOtpChallenge
                            {
                                Id =
                                    Guid.NewGuid(),

                                UserId =
                                    user.Id,

                                OtpHash =
                                    Hash(
                                        otp
                                    ),

                                ExpiresAt =
                                    now.AddMinutes(
                                        10
                                    ),

                                CreatedAt =
                                    now
                            };

                        db.LoginOtpChallenges.Add(
                            challenge
                        );

                        await db.SaveChangesAsync(
                            ct
                        );

                        await emailSender
                            .SendLoginOtpAsync(
                                user.Email,
                                otp,
                                ct
                            );

                        return Results.Ok(
                            new
                            {
                                otpRequired =
                                    true,

                                challengeId =
                                    challenge.Id,

                                maskedEmail =
                                    MaskEmail(
                                        user.Email
                                    )
                            }
                        );
                    }
                }

                // ====================================================
                // ROLES
                // ====================================================

                var roles =
                    await (
                        from userRole
                            in db.UserRoles

                        join role
                            in db.Roles

                            on userRole.RoleId
                            equals role.Id

                        where
                            userRole.UserId ==
                            user.Id

                        select
                            role.Code
                    )
                    .ToListAsync(
                        ct
                    );

                // ====================================================
                // PERMISSIONS
                // ====================================================

                var permissions =
                    await (
                        from userRole
                            in db.UserRoles

                        join rolePermission
                            in db.RolePermissions

                            on userRole.RoleId
                            equals rolePermission.RoleId

                        join permission
                            in db.Permissions

                            on rolePermission.PermissionId
                            equals permission.Id

                        where
                            userRole.UserId ==
                            user.Id

                        select
                            permission.Code
                    )
                    .Distinct()
                    .ToListAsync(
                        ct
                    );

                // ====================================================
                // VENDOR MAPPING
                // ====================================================

                var vendorId =
                    await db.VendorUsers
                        .Where(
                            x =>
                                x.UserId ==
                                user.Id
                                &&
                                x.IsActive
                        )
                        .Select(
                            x =>
                                (Guid?)
                                x.VendorId
                        )
                        .FirstOrDefaultAsync(
                            ct
                        );

                var (
                    accessToken,
                    expiresAt
                ) =
                    jwt.Create(
                        user,
                        roles
                    );

                return Results.Ok(
                    new
                    {
                        accessToken,
                        expiresAt,

                        user =
                            new
                            {
                                id =
                                    user.Id,

                                fullName =
                                    user.FullName,

                                email =
                                    user.Email,

                                userType =
                                    user.UserType,

                                roles,

                                permissions,

                                vendorId
                            }
                    }
                );
            }
        )
        .AllowAnonymous();

        // ========================================================
        // PASSWORD SETUP INFORMATION
        // ========================================================

        group.MapGet(
            "/password-setup-info",
            async (
                string token,
                AppDbContext db,
                PasswordSetupTokenService tokenService,
                CancellationToken ct) =>
            {
                PasswordSetupPayload payload;

                try
                {
                    payload =
                        tokenService.Validate(
                            token
                        );
                }
                catch (
                    InvalidOperationException ex
                )
                {
                    throw new ApiException(
                        400,
                        ex.Message
                    );
                }

                var user =
                    await db.Users
                        .FirstOrDefaultAsync(
                            x =>
                                x.Id ==
                                payload.UserId,
                            ct
                        )
                    ??
                    throw new ApiException(
                        400,
                        "Password setup link is invalid."
                    );

                if (
                    !tokenService
                        .MatchesCurrentUserState(
                            payload,
                            user
                        )
                )
                {
                    throw new ApiException(
                        400,
                        "This password setup link has already been used or is no longer valid."
                    );
                }

                if (
                    !user.IsActive
                )
                {
                    throw new ApiException(
                        403,
                        "Portal access for this account is disabled."
                    );
                }

                return Results.Ok(
                    new
                    {
                        email =
                            user.Email,

                        fullName =
                            user.FullName,

                        expiresAt =
                            payload.ExpiresAt
                    }
                );
            }
        )
        .AllowAnonymous();

        // ========================================================
        // SET PASSWORD
        // ========================================================

        group.MapPost(
            "/set-password",
            async (
                SetPasswordRequest request,
                AppDbContext db,
                PasswordSetupTokenService tokenService,
                CancellationToken ct) =>
            {
                if (
                    string.IsNullOrWhiteSpace(
                        request.Token
                    )
                )
                {
                    throw new ApiException(
                        400,
                        "Password setup token is required."
                    );
                }

                ValidatePassword(
                    request.Password,
                    request.ConfirmPassword
                );

                PasswordSetupPayload payload;

                try
                {
                    payload =
                        tokenService.Validate(
                            request.Token
                        );
                }
                catch (
                    InvalidOperationException ex
                )
                {
                    throw new ApiException(
                        400,
                        ex.Message
                    );
                }

                var user =
                    await db.Users
                        .FirstOrDefaultAsync(
                            x =>
                                x.Id ==
                                payload.UserId,
                            ct
                        )
                    ??
                    throw new ApiException(
                        400,
                        "Password setup link is invalid."
                    );

                if (
                    !tokenService
                        .MatchesCurrentUserState(
                            payload,
                            user
                        )
                )
                {
                    throw new ApiException(
                        400,
                        "This password setup link has already been used or is no longer valid."
                    );
                }

                if (
                    !user.IsActive
                )
                {
                    throw new ApiException(
                        403,
                        "Portal access for this account is disabled."
                    );
                }

                user.PasswordHash =
                    new PasswordHasher<User>()
                        .HashPassword(
                            user,
                            request.Password
                        );

                user.FailedLoginAttempts =
                    0;

                user.LockoutUntil =
                    null;

                user.UpdatedAt =
                    DateTimeOffset.UtcNow;

                await db.SaveChangesAsync(
                    ct
                );

                return Results.Ok(
                    new
                    {
                        passwordSet =
                            true,

                        message =
                            "Password created successfully."
                    }
                );
            }
        )
        .AllowAnonymous();

        // ========================================================
        // FORGOT PASSWORD
        // ========================================================

        group.MapPost(
            "/forgot-password",
            async (
                ForgotPasswordRequest request,
                AppDbContext db,
                EmailOtpSender emailSender,
                IConfiguration config,
                CancellationToken ct) =>
            {
                var email =
                    request.Email?
                        .Trim()
                        .ToLowerInvariant();

                var user =
                    string.IsNullOrWhiteSpace(
                        email
                    )
                        ? null
                        : await db.Users
                            .FirstOrDefaultAsync(
                                x =>
                                    x.Email.ToLower() ==
                                    email
                                    &&
                                    x.IsActive,
                                ct
                            );

                if (
                    user is not null
                )
                {
                    var now =
                        DateTimeOffset.UtcNow;

                    var active =
                        await db.PasswordResetTokens
                            .Where(
                                x =>
                                    x.UserId ==
                                    user.Id
                                    &&
                                    x.UsedAt ==
                                    null
                                    &&
                                    x.ExpiresAt >
                                    now
                            )
                            .ToListAsync(
                                ct
                            );

                    active.ForEach(
                        x =>
                            x.UsedAt =
                            now
                    );

                    var token =
                        Convert.ToHexString(
                            RandomNumberGenerator
                                .GetBytes(
                                    32
                                )
                        );

                    db.PasswordResetTokens.Add(
                        new PasswordResetToken
                        {
                            Id =
                                Guid.NewGuid(),

                            UserId =
                                user.Id,

                            TokenHash =
                                Hash(
                                    token
                                ),

                            ExpiresAt =
                                now.AddMinutes(
                                    30
                                ),

                            CreatedAt =
                                now
                        }
                    );

                    await db.SaveChangesAsync(
                        ct
                    );

                    var frontendUrl =
                        config[
                            "FRONTEND_URL"
                        ]
                        ??
                        "http://localhost:5173";

                    await emailSender
                        .SendPasswordResetAsync(
                            user.Email,
                            $"{frontendUrl.TrimEnd('/')}/reset-password?token={Uri.EscapeDataString(token)}",
                            ct
                        );
                }

                return Results.Ok(
                    new
                    {
                        message =
                            "If an account exists for that email address, a password reset link has been sent."
                    }
                );
            }
        )
        .AllowAnonymous();

        // ========================================================
        // RESET PASSWORD
        // ========================================================

        group.MapPost(
            "/reset-password",
            async (
                ResetPasswordRequest request,
                AppDbContext db,
                CancellationToken ct) =>
            {
                ValidatePassword(
                    request.Password,
                    request.ConfirmPassword
                );

                var reset =
                    await db.PasswordResetTokens
                        .FirstOrDefaultAsync(
                            x =>
                                x.TokenHash ==
                                Hash(
                                    request.Token
                                )
                                &&
                                x.UsedAt ==
                                null
                                &&
                                x.ExpiresAt >
                                DateTimeOffset.UtcNow,
                            ct
                        )
                    ??
                    throw new ApiException(
                        400,
                        "Password reset link is invalid or expired."
                    );

                var user =
                    await db.Users
                        .FindAsync(
                            [
                                reset.UserId
                            ],
                            ct
                        )
                    ??
                    throw new ApiException(
                        400,
                        "Password reset link is invalid or expired."
                    );

                user.PasswordHash =
                    new PasswordHasher<User>()
                        .HashPassword(
                            user,
                            request.Password
                        );

                user.FailedLoginAttempts =
                    0;

                user.LockoutUntil =
                    null;

                user.UpdatedAt =
                    DateTimeOffset.UtcNow;

                reset.UsedAt =
                    DateTimeOffset.UtcNow;

                var devices =
                    await db.TrustedDevices
                        .Where(
                            x =>
                                x.UserId ==
                                user.Id
                                &&
                                x.RevokedAt ==
                                null
                        )
                        .ToListAsync(
                            ct
                        );

                devices.ForEach(
                    x =>
                        x.RevokedAt =
                        DateTimeOffset.UtcNow
                );

                await db.SaveChangesAsync(
                    ct
                );

                return Results.Ok(
                    new
                    {
                        passwordReset =
                            true
                    }
                );
            }
        )
        .AllowAnonymous();

        // ========================================================
        // VERIFY LOGIN OTP
        // ========================================================

        group.MapPost(
            "/verify-login-otp",
            async (
                VerifyLoginOtpRequest request,
                AppDbContext db,
                JwtTokenService jwt,
                HttpContext http,
                CancellationToken ct) =>
            {
                var challenge =
                    await db.LoginOtpChallenges
                        .FirstOrDefaultAsync(
                            x =>
                                x.Id ==
                                request.ChallengeId
                                &&
                                x.UsedAt ==
                                null
                                &&
                                x.ExpiresAt >
                                DateTimeOffset.UtcNow,
                            ct
                        )
                    ??
                    throw new ApiException(
                        400,
                        "Verification code is invalid or expired."
                    );

                if (
                    !CryptographicOperations.FixedTimeEquals(
                        Convert.FromHexString(
                            challenge.OtpHash
                        ),
                        Convert.FromHexString(
                            Hash(
                                (request.Otp ?? "").Trim()
                            )
                        )
                    )
                )
                {
                    challenge.Attempts++;

                    if (
                        challenge.Attempts >=
                        5
                    )
                    {
                        challenge.UsedAt =
                            DateTimeOffset.UtcNow;
                    }

                    await db.SaveChangesAsync(
                        ct
                    );

                    throw new ApiException(
                        400,
                        "Verification code is invalid or expired."
                    );
                }

                challenge.UsedAt =
                    DateTimeOffset.UtcNow;

                var token =
                    Convert.ToHexString(
                        RandomNumberGenerator
                            .GetBytes(
                                32
                            )
                    );

                db.TrustedDevices.Add(
                    new TrustedDevice
                    {
                        Id =
                            Guid.NewGuid(),

                        UserId =
                            challenge.UserId,

                        TokenHash =
                            Hash(
                                token
                            ),

                        CreatedAt =
                            DateTimeOffset.UtcNow,

                        LastUsedAt =
                            DateTimeOffset.UtcNow,

                        ExpiresAt =
                            DateTimeOffset.UtcNow
                                .AddDays(
                                    90
                                )
                    }
                );

                var user =
                    await db.Users
                        .FindAsync(
                            [
                                challenge.UserId
                            ],
                            ct
                        )
                    ??
                    throw new ApiException(
                        400,
                        "Verification code is invalid or expired."
                    );

                await db.SaveChangesAsync(
                    ct
                );

                http.Response.Cookies.Append(
                    "bpp_trusted_device",
                    token,
                    new CookieOptions
                    {
                        HttpOnly =
                            true,

                        Secure =
                            http.Request.IsHttps,

                        SameSite =
                            SameSiteMode.Lax,

                        Expires =
                            DateTimeOffset.UtcNow
                                .AddDays(
                                    90
                                ),

                        Path =
                            "/"
                    }
                );

                return await CreateLoginResponseAsync(
                    user,
                    db,
                    jwt,
                    ct
                );
            }
        )
        .AllowAnonymous();

        // ========================================================
        // RESEND LOGIN OTP
        // ========================================================

        group.MapPost(
            "/resend-login-otp",
            async (
                VerifyLoginOtpRequest request,
                AppDbContext db,
                EmailOtpSender emailSender,
                CancellationToken ct) =>
            {
                var current =
                    await db.LoginOtpChallenges
                        .FirstOrDefaultAsync(
                            x =>
                                x.Id ==
                                request.ChallengeId
                                &&
                                x.UsedAt ==
                                null
                                &&
                                x.ExpiresAt >
                                DateTimeOffset.UtcNow,
                            ct
                        )
                    ??
                    throw new ApiException(
                        400,
                        "Verification challenge is invalid or expired."
                    );

                if (
                    current.CreatedAt >
                    DateTimeOffset.UtcNow
                        .AddMinutes(
                            -1
                        )
                )
                {
                    throw new ApiException(
                        429,
                        "Please wait before requesting another code."
                    );
                }

                current.UsedAt =
                    DateTimeOffset.UtcNow;

                var otp =
                    RandomNumberGenerator
                        .GetInt32(
                            100000,
                            1000000
                        )
                        .ToString();

                var next =
                    new LoginOtpChallenge
                    {
                        Id =
                            Guid.NewGuid(),

                        UserId =
                            current.UserId,

                        OtpHash =
                            Hash(
                                otp
                            ),

                        CreatedAt =
                            DateTimeOffset.UtcNow,

                        ExpiresAt =
                            DateTimeOffset.UtcNow
                                .AddMinutes(
                                    10
                                )
                    };

                db.LoginOtpChallenges.Add(
                    next
                );

                var user =
                    await db.Users
                        .FindAsync(
                            [
                                current.UserId
                            ],
                            ct
                        )
                    ??
                    throw new ApiException(
                        400,
                        "Verification challenge is invalid or expired."
                    );

                await db.SaveChangesAsync(
                    ct
                );

                await emailSender
                    .SendLoginOtpAsync(
                        user.Email,
                        otp,
                        ct
                    );

                return Results.Ok(
                    new
                    {
                        challengeId =
                            next.Id
                    }
                );
            }
        )
        .AllowAnonymous();

        return app;
    }

    // ============================================================
    // HASH
    // ============================================================

    private static string Hash(
        string value)
    {
        return Convert.ToHexString(
            SHA256.HashData(
                Encoding.UTF8
                    .GetBytes(
                        value
                    )
            )
        );
    }

    // ============================================================
    // MASK EMAIL
    // ============================================================

    private static string MaskEmail(
        string email)
    {
        var at =
            email.IndexOf(
                '@'
            );

        return
            at <=
            1
                ?
                "***" +
                email[at..]
                :
                email[0] +
                "***" +
                email[at..];
    }

    // ============================================================
    // PASSWORD VALIDATION
    // ============================================================

    private static void ValidatePassword(
        string password,
        string confirm)
    {
        if (
            string.IsNullOrWhiteSpace(
                password
            )
            ||
            password !=
            confirm
            ||
            password.Length <
            8
            ||
            !password.Any(
                char.IsUpper
            )
            ||
            !password.Any(
                char.IsLower
            )
            ||
            !password.Any(
                char.IsDigit
            )
        )
        {
            throw new ApiException(
                400,
                "Password must be at least 8 characters and include uppercase, lowercase and a number."
            );
        }
    }

    // ============================================================
    // CREATE LOGIN RESPONSE
    // ============================================================

    private static async Task<IResult>
        CreateLoginResponseAsync(
            User user,
            AppDbContext db,
            JwtTokenService jwt,
            CancellationToken ct)
    {
        var roles =
            await (
                from ur
                    in db.UserRoles

                join r
                    in db.Roles

                    on ur.RoleId
                    equals r.Id

                where
                    ur.UserId ==
                    user.Id

                select
                    r.Code
            )
            .ToListAsync(
                ct
            );

        var permissions =
            await (
                from ur
                    in db.UserRoles

                join rp
                    in db.RolePermissions

                    on ur.RoleId
                    equals rp.RoleId

                join p
                    in db.Permissions

                    on rp.PermissionId
                    equals p.Id

                where
                    ur.UserId ==
                    user.Id

                select
                    p.Code
            )
            .Distinct()
            .ToListAsync(
                ct
            );

        var vendorId =
            await db.VendorUsers
                .Where(
                    x =>
                        x.UserId ==
                        user.Id
                        &&
                        x.IsActive
                )
                .Select(
                    x =>
                        (Guid?)
                        x.VendorId
                )
                .FirstOrDefaultAsync(
                    ct
                );

        var (
            accessToken,
            expiresAt
        ) =
            jwt.Create(
                user,
                roles
            );

        return Results.Ok(
            new
            {
                accessToken,
                expiresAt,

                user =
                    new
                    {
                        id =
                            user.Id,

                        fullName =
                            user.FullName,

                        email =
                            user.Email,

                        userType =
                            user.UserType,

                        roles,

                        permissions,

                        vendorId
                    }
            }
        );
    }
}
