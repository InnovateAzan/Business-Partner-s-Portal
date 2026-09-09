using System.Security.Cryptography;
using System.Text;

using BusinessPartnerPortal.Api.Domain;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;

namespace BusinessPartnerPortal.Api.Services;

public sealed record PasswordSetupPayload(
    Guid UserId,
    string Email,
    string PasswordState,
    DateTimeOffset ExpiresAt
);

public sealed class PasswordSetupTokenService
{
    private readonly IDataProtector _protector;
    private readonly IConfiguration _configuration;

    public PasswordSetupTokenService(
        IDataProtectionProvider provider,
        IConfiguration configuration)
    {
        _configuration =
            configuration;

        _protector =
            provider.CreateProtector(
                "BusinessPartnerPortal.PasswordSetup.v2");
    }

    public string Create(
        User user)
    {
        var expiryMinutes =
            int.TryParse(
                _configuration[
                    "PASSWORD_SETUP_MINUTES"],
                out var configuredMinutes)
                ? configuredMinutes
                : 60;

        var expiresAt =
            DateTimeOffset.UtcNow
                .AddMinutes(
                    expiryMinutes);

        var emailEncoded =
            Convert.ToBase64String(
                Encoding.UTF8.GetBytes(
                    user.Email
                        .Trim()
                        .ToLowerInvariant()));

        var passwordState =
            Fingerprint(
                user.PasswordHash);

        var raw =
            string.Join(
                "|",
                user.Id.ToString("N"),
                emailEncoded,
                passwordState,
                expiresAt.ToUnixTimeSeconds());

        var protectedValue =
            _protector.Protect(raw);

        return WebEncoders.Base64UrlEncode(
            Encoding.UTF8.GetBytes(
                protectedValue));
    }

    public PasswordSetupPayload Validate(
        string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException(
                "Password setup token is missing.");
        }

        try
        {
            var protectedValue =
                Encoding.UTF8.GetString(
                    WebEncoders
                        .Base64UrlDecode(
                            token));

            var raw =
                _protector.Unprotect(
                    protectedValue);

            var parts =
                raw.Split('|');

            if (parts.Length != 4)
            {
                throw new InvalidOperationException(
                    "Password setup link is invalid.");
            }

            var userId =
                Guid.ParseExact(
                    parts[0],
                    "N");

            var email =
                Encoding.UTF8.GetString(
                    Convert.FromBase64String(
                        parts[1]));

            var passwordState =
                parts[2];

            var expiresAt =
                DateTimeOffset
                    .FromUnixTimeSeconds(
                        long.Parse(
                            parts[3]));

            if (
                expiresAt <=
                DateTimeOffset.UtcNow)
            {
                throw new InvalidOperationException(
                    "Password setup link has expired.");
            }

            return new PasswordSetupPayload(
                userId,
                email,
                passwordState,
                expiresAt);
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch
        {
            throw new InvalidOperationException(
                "Password setup link is invalid.");
        }
    }

    public bool MatchesCurrentUserState(
        PasswordSetupPayload payload,
        User user)
    {
        if (
            payload.UserId !=
            user.Id)
        {
            return false;
        }

        if (
            !string.Equals(
                payload.Email,
                user.Email,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var currentState =
            Fingerprint(
                user.PasswordHash);

        return string.Equals(
            payload.PasswordState,
            currentState,
            StringComparison.Ordinal);
    }

    private static string Fingerprint(
        string? passwordHash)
    {
        var source =
            string.IsNullOrWhiteSpace(
                passwordHash)
                ? "<NO_PASSWORD>"
                : passwordHash;

        var bytes =
            SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    source));

        return Convert.ToHexString(
            bytes);
    }
}