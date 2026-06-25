using FashionSaaS.API.Constants;
using FashionSaaS.Application.Common;
using FashionSaaS.Application.Interfaces;
using FashionSaaS.Application.Subscriptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FashionSaaS.API.Controllers.Tenant;

[ApiController]
[Authorize(Roles = "AdminOwner")]
[EnableRateLimiting("AuthenticatedPolicy")]
public class TenantSubscriptionController(SubscriptionService subscriptionService, ICurrentTenantService currentTenant) : ControllerBase
{
    /// <summary>
    /// Returns the active subscription for the current tenant.
    /// </summary>
    [HttpGet(ApiUrl.TenantSubscription.Get)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Get()
    {
        var response = await subscriptionService.GetByTenantAsync(currentTenant.TenantId!.Value);
        return StatusCode(response.StatusCode, response);
    }

    /// <summary>
    /// Returns payments for the current tenant's active subscription.
    /// The subscription lookup is scoped to currentTenant — no cross-tenant access possible.
    /// </summary>
    [HttpGet(ApiUrl.TenantSubscription.GetPayments)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetPayments()
    {
        var sub = await subscriptionService.GetByTenantAsync(currentTenant.TenantId!.Value);
        if (!sub.IsSuccess) return StatusCode(sub.StatusCode, sub);

        var payments = await subscriptionService.GetPaymentsBySubscriptionAsync(sub.Data!.Id);
        return StatusCode(payments.StatusCode, payments);
    }
}
