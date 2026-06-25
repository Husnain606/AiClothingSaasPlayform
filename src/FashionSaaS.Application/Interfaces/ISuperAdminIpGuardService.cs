namespace FashionSaaS.Application.Interfaces;

/// <summary>
/// Checks whether a Super Admin's current IP is new (not seen in recent logins).
/// </summary>
public interface ISuperAdminIpGuardService
{
    /// <summary>
    /// Returns true if <paramref name="currentIp"/> is NOT in the user's recent known IPs.
    /// </summary>
    Task<bool> IsNewIpAsync(string email, string currentIp);
}
