using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using FashionSaaS.Application.Configuration;
using FashionSaaS.Application.Interfaces;
using FashionSaaS.Domain.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace FashionSaaS.Infrastructure.Services;

public class JwtService(IOptions<JwtSettings> jwtOptions) : IJwtService
{
    private readonly JwtSettings _jwt = jwtOptions.Value;

    public string GenerateAccessToken(User user, IList<string> roles, string? tenantSlug = null, bool mfaVerified = false)
    {
        var secret = _jwt.Secret is { Length: > 0 } s ? s
            : throw new InvalidOperationException("JwtSettings:Secret not set.");
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var isSuperAdmin = roles.Contains(nameof(Domain.Enums.RoleType.SuperAdmin));
        var expiryMinutes = isSuperAdmin ? 10 : 15;

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new("tenant_id", user.TenantId?.ToString() ?? string.Empty),
            new("mfa_verified", mfaVerified.ToString().ToLower())
        };

        if (!string.IsNullOrEmpty(tenantSlug))
        {
            claims.Add(new Claim("tenant_slug", tenantSlug));
        }

        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

        var token = new JwtSecurityToken(
            issuer: _jwt.Issuer,
            audience: _jwt.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(expiryMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string GenerateRefreshToken()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

    /// <summary>
    /// Validates the signature, issuer, and audience of an expired access token so the
    /// refresh flow can extract the userId. Lifetime validation is intentionally skipped
    /// because the caller presents an expired token by design; the HttpOnly refresh cookie
    /// is the actual credential.
    /// </summary>
    public ClaimsPrincipal? GetPrincipalFromExpiredToken(string token)
    {
        var secret = _jwt.Secret is { Length: > 0 } s ? s
            : throw new InvalidOperationException("JwtSettings:Secret not set.");
        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
            ValidateIssuer = true,
            ValidIssuer = _jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = _jwt.Audience,
            ValidateLifetime = false  // intentionally skipped — refresh cookie is the credential
        };

        var handler = new JwtSecurityTokenHandler();
        try
        {
            return handler.ValidateToken(token, validationParameters, out _);
        }
        catch
        {
            return null;
        }
    }

    /// <inheritdoc />
    public string GenerateMfaChallengeToken(Guid userId)
    {
        var secret = _jwt.Secret is { Length: > 0 } s ? s
            : throw new InvalidOperationException("JwtSettings:Secret not set.");
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new("purpose", "mfa_challenge")
        };

        var token = new JwtSecurityToken(
            issuer: _jwt.Issuer,
            audience: _jwt.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <inheritdoc />
    public Guid? ValidateMfaChallengeToken(string token)
    {
        var secret = _jwt.Secret is { Length: > 0 } s ? s
            : throw new InvalidOperationException("JwtSettings:Secret not set.");
        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
            ValidateIssuer = true,
            ValidIssuer = _jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = _jwt.Audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };

        var handler = new JwtSecurityTokenHandler();
        try
        {
            var principal = handler.ValidateToken(token, validationParameters, out _);

            // Must carry purpose=mfa_challenge to prevent access tokens being substituted
            var purpose = principal.FindFirstValue("purpose");
            if (purpose != "mfa_challenge")
                return null;

            var sub = principal.FindFirstValue(JwtRegisteredClaimNames.Sub)
                      ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(sub, out var userId) ? userId : null;
        }
        catch
        {
            return null;
        }
    }
}
