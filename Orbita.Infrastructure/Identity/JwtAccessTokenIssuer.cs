using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Orbita.Application.Identity;

namespace Orbita.Infrastructure.Identity;

/// <summary>
/// Reads Jwt:* from IConfiguration lazily, inside Issue rather than a constructor
/// field, for the same reason AddOrbitaInfrastructure resolves the connection string
/// lazily: it keeps this safe to construct at any time without caring whether it was
/// built before or after a test's configuration overrides land.
/// </summary>
public sealed class JwtAccessTokenIssuer(IConfiguration configuration) : IAccessTokenIssuer
{
    public IssuedAccessToken Issue(Guid userId, DateTimeOffset now)
    {
        var signingKey = configuration["Jwt:SigningKey"]
            ?? throw new InvalidOperationException("Missing 'Jwt:SigningKey' configuration value.");
        var issuer = configuration["Jwt:Issuer"] ?? "orbita";
        var audience = configuration["Jwt:Audience"] ?? "orbita-api";
        var lifetimeMinutes = int.Parse(configuration["Jwt:AccessTokenLifetimeMinutes"] ?? "15");

        var expiresAt = now + TimeSpan.FromMinutes(lifetimeMinutes);
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: [new Claim(JwtRegisteredClaimNames.Sub, userId.ToString())],
            notBefore: now.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        return new IssuedAccessToken(new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }
}
