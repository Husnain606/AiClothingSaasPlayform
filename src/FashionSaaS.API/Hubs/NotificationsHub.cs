using System.Security.Claims;
using FashionSaaS.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace FashionSaaS.API.Hubs;

/// <summary>
/// Real-time notifications hub. Group membership (`tenant:{tenantId}`, `user:{userId}`) is
/// derived exclusively from the caller's validated JWT claims on connect — never from
/// client-supplied input. SuperAdmin connections (tenant-less, `tenant_id` claim empty) join
/// only the user-group. The `tenant:{tenantId}` group carries STAFF-ONLY broadcasts (order
/// placed/status-changed, payment confirmed, low stock, review submitted are all admin
/// alerts) — a Customer-role connection is excluded from it even though customer JWTs also
/// carry a `tenant_id` claim (tenant is resolved from the JWT, not a slug, on `api/store/*`
/// routes). Every authenticated connection (staff or customer) still joins its own
/// `user:{userId}` group, keyed by the JWT's `NameIdentifier` (the `User` entity id).
/// </summary>
[Authorize]
public class NotificationsHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var tenantId = Context.User?.FindFirst("tenant_id")?.Value;
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        List<string> roles = Context.User?.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList() ?? [];

        // Tenant broadcasts (order/payment/stock/review admin alerts) are staff-only — a
        // Customer-role connection must never join the tenant group, or every customer would
        // receive every other customer's order/payment events. Fail closed: a connection with
        // no roles at all does not join either (matches the tenancy "no context -> zero rows"
        // invariant), not just one with the Customer role.
        var isStaff = roles.Count > 0 && !roles.Contains(nameof(RoleType.Customer));

        if (isStaff && !string.IsNullOrEmpty(tenantId))
            await Groups.AddToGroupAsync(Context.ConnectionId, $"tenant:{tenantId}");
        if (!string.IsNullOrEmpty(userId))
            await Groups.AddToGroupAsync(Context.ConnectionId, $"user:{userId}");

        await base.OnConnectedAsync();
    }
}
