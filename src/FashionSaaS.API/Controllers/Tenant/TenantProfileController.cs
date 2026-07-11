using System.Security.Claims;
using FashionSaaS.API.Constants;
using FashionSaaS.Application.Common;
using FashionSaaS.Application.Interfaces;
using FashionSaaS.Application.Tenants;
using FashionSaaS.Application.Tenants.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FashionSaaS.API.Controllers.Tenant;

[ApiController]
[Authorize(Roles = "AdminOwner")]
[EnableRateLimiting("AuthenticatedPolicy")]
public class TenantProfileController(TenantService tenantService, ICurrentTenantService currentTenant) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string Ip => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    private string Ua => Request.Headers.UserAgent.ToString();

    /// <summary>
    /// Returns the current tenant's own profile. Scoped to the authenticated tenant.
    /// </summary>
    [HttpGet(ApiUrl.TenantProfile.Get)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Get()
    {
        ResponseData<TenantResponse> response = await tenantService.GetByIdAsync(currentTenant.TenantId!.Value);
        return StatusCode(response.StatusCode, response);
    }

    /// <summary>
    /// Updates the current tenant's own profile. Scoped to the authenticated tenant.
    /// </summary>
    [HttpPut(ApiUrl.TenantProfile.Update)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Update([FromBody] UpdateTenantRequest request)
    {
        ResponseData<TenantResponse> response = await tenantService.UpdateAsync(currentTenant.TenantId!.Value, request, UserId, Ip, Ua);
        return StatusCode(response.StatusCode, response);
    }
}
