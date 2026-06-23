using System.Security.Claims;
using FashionSaaS.Domain.Entities;

namespace FashionSaaS.Application.Interfaces;

public interface IJwtService
{
    string GenerateAccessToken(User user, IList<string> roles, string? tenantSlug = null, bool mfaVerified = false);
    string GenerateRefreshToken();
    ClaimsPrincipal? GetPrincipalFromExpiredToken(string token);
}
