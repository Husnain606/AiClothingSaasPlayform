using System.Security.Claims;
using FashionSaaS.TryOn.Application;
using Microsoft.AspNetCore.Http;

namespace FashionSaaS.TryOn.Infrastructure.Security;

public class CurrentTryOnContext(IHttpContextAccessor httpContextAccessor) : ICurrentTryOnContext
{
    // "sub" is the JWT registered claim name for subject; using the literal (rather than
    // System.IdentityModel.Tokens.Jwt's JwtRegisteredClaimNames.Sub constant) avoids this
    // project needing its own reference to that package — Infrastructure has no other need for it.
    private const string SubClaimType = "sub";

    private ClaimsPrincipal? User => httpContextAccessor.HttpContext?.User;

    public bool IsAuthenticated => User?.Identity?.IsAuthenticated == true;

    public Guid TenantId =>
        Guid.TryParse(User?.FindFirst("tenant_id")?.Value, out Guid id) ? id : Guid.Empty;

    public Guid CustomerId =>
        Guid.TryParse(
            User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? User?.FindFirst(SubClaimType)?.Value,
            out Guid id) ? id : Guid.Empty;

    public int AiUsageLimit =>
        int.TryParse(
            User?.FindFirst("ai_usage_limit")?.Value,
            System.Globalization.CultureInfo.InvariantCulture,
            out var limit) ? limit : 0;
}
