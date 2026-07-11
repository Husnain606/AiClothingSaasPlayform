using FashionSaaS.Application.Interfaces;

namespace FashionSaaS.Application.Auth;

/// <summary>
/// Checks whether a Super Admin's current IP is new (not seen in recent logins).
/// Detection only — alerting is handled by the domain event handler after the event is raised.
/// </summary>
public class SuperAdminIpGuardService(ILoginAttemptRepository loginAttemptRepository)
    : ISuperAdminIpGuardService
{
    /// <summary>
    /// Returns true if <paramref name="currentIp"/> is NOT in the user's recent known IPs.
    /// </summary>
    public async Task<bool> IsNewIpAsync(string email, string currentIp)
    {
        IReadOnlyList<string> knownIps = await loginAttemptRepository.GetRecentIpsByUserEmailAsync(email, 20);
        return !knownIps.Contains(currentIp, StringComparer.Ordinal);
    }
}
