using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using BusinessPartnerPortal.Api.Domain;
using Microsoft.IdentityModel.Tokens;

namespace BusinessPartnerPortal.Api.Auth;

public sealed class JwtTokenService(IConfiguration config)
{
    public (string Token, DateTimeOffset ExpiresAt) Create(User user, IEnumerable<string> roles)
    {
        var secret = config["JWT_SECRET"] ?? throw new InvalidOperationException("JWT_SECRET is not configured.");
        var minutes = int.TryParse(config["JWT_ACCESS_TOKEN_MINUTES"], out var value) ? value : 10;
        var expires = DateTimeOffset.UtcNow.AddMinutes(minutes);
        var claims = new List<Claim>{ new(ClaimTypes.NameIdentifier,user.Id.ToString()), new(ClaimTypes.Name,user.FullName), new(ClaimTypes.Email,user.Email), new("user_type",user.UserType) };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        var credentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(config["JWT_ISSUER"], config["JWT_AUDIENCE"], claims, expires: expires.UtcDateTime, signingCredentials: credentials);
        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}
