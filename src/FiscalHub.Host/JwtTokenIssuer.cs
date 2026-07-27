using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using FiscalHub.Application.Auth;
using Microsoft.IdentityModel.Tokens;

namespace FiscalHub.Host;

/// <summary>Emite o JWT assinado (HS256) com os claims do usuário — inclusive o tenant, que escopa os dados.</summary>
internal sealed class JwtTokenIssuer(JwtOptions options, TimeProvider clock)
{
    public (string Token, DateTimeOffset ExpiresAt) Issue(AppUser user)
    {
        DateTimeOffset expiresAt = clock.GetUtcNow().AddMinutes(options.ExpiryMinutes);

        Claim[] claims =
        [
            new("sub", user.Id.ToString()),
            new("email", user.Email),
            new("name", user.Name),
            new("tenant", user.TenantId),
            new("role", user.Role),
        ];

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.Key));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(options.Issuer, options.Audience, claims, expires: expiresAt.UtcDateTime, signingCredentials: credentials);

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }
}
