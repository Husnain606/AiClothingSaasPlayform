using System.Security.Claims;
using FashionSaaS.Domain.Entities;

namespace FashionSaaS.Application.Interfaces;

public interface IJwtService
{
    string GenerateAccessToken(User user, IList<string> roles, string? tenantSlug = null, bool mfaVerified = false);
    string GenerateRefreshToken();
    ClaimsPrincipal? GetPrincipalFromExpiredToken(string token);

    /// <summary>
    /// Issues a short-lived (5-minute) HS256 token that proves the password step
    /// completed successfully. The token carries a <c>purpose=mfa_challenge</c> claim
    /// so it cannot be substituted for an access token.
    /// </summary>
    string GenerateMfaChallengeToken(Guid userId);

    /// <summary>
    /// Validates the MFA-challenge token: checks signature, issuer, audience, lifetime,
    /// and the <c>purpose=mfa_challenge</c> claim. Returns the userId from <c>sub</c>,
    /// or <c>null</c> if the token is invalid, expired, or has the wrong purpose.
    /// </summary>
    Guid? ValidateMfaChallengeToken(string token);
}
