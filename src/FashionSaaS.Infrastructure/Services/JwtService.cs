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

    public string GenerateAccessToken(User user, IEnumerable<string> roles, string? tenantSlug = null, bool mfaVerified = false, int aiUsageLimit = 0)
    {
        var secret = _jwt.Secret is { Length: > 0 } s ? s
            : throw new InvalidOperationException("JwtSettings:Secret not set.");
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        // Materialize once so we don't enumerate the IEnumerable twice (Contains + Select below).
        IReadOnlyList<string> roleList = roles as IReadOnlyList<string> ?? roles.ToList();

        var isSuperAdmin = roleList.Contains(nameof(Domain.Enums.RoleType.SuperAdmin), StringComparer.Ordinal);
        var expiryMinutes = isSuperAdmin ? 10 : 15;

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new("tenant_id", user.TenantId?.ToString() ?? string.Empty),
            // CA1308 suppressed: "true"/"false" lowercase is the exact literal value compared
            // by the "MfaVerified" authorization policy (RequireClaim("mfa_verified", "true"))
            // — flipping to uppercase would break that exact-match check.
#pragma warning disable CA1308
            new("mfa_verified", mfaVerified.ToString().ToLowerInvariant()),
#pragma warning restore CA1308
            new("ai_usage_limit", aiUsageLimit.ToString(System.Globalization.CultureInfo.InvariantCulture))
        };

        if (!string.IsNullOrEmpty(tenantSlug))
        {
            claims.Add(new Claim("tenant_slug", tenantSlug));
        }

        claims.AddRange(roleList.Select(r => new Claim(ClaimTypes.Role, r)));

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
            // CA5404 suppressed: this method exists specifically to read claims out of an
            // EXPIRED token during the refresh flow (see the class-level doc comment) — the
            // caller presents an expired access token by design; the HttpOnly refresh cookie
            // is the actual credential being validated, not the access token's lifetime.
#pragma warning disable CA5404
            ValidateLifetime = false
#pragma warning restore CA5404
        };

        var handler = new JwtSecurityTokenHandler();
        // CA1031 suppressed deliberately: any validation failure (signature, issuer, audience,
        // malformed token) must uniformly return null — the caller only cares "valid or not".
#pragma warning disable CA1031
        try
        {
            return handler.ValidateToken(token, validationParameters, out _);
        }
        catch
        {
            return null;
        }
#pragma warning restore CA1031
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
        // CA1031 suppressed deliberately: any validation failure must uniformly return null.
#pragma warning disable CA1031
        try
        {
            ClaimsPrincipal principal = handler.ValidateToken(token, validationParameters, out _);

            // Must carry purpose=mfa_challenge to prevent access tokens being substituted
            var purpose = principal.FindFirstValue("purpose");
            if (!string.Equals(purpose, "mfa_challenge", StringComparison.Ordinal))
                return null;

            var sub = principal.FindFirstValue(JwtRegisteredClaimNames.Sub)
                      ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(sub, out Guid userId) ? userId : null;
        }
        catch
        {
            return null;
        }
#pragma warning restore CA1031
    }
}
