using System.Security.Claims;
using FashionSaaS.API.Constants;
using FashionSaaS.Application.Common;
using FashionSaaS.Application.Tenants;
using FashionSaaS.Application.Tenants.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FashionSaaS.API.Controllers.Admin;

[ApiController]
[Authorize(Roles = "SuperAdmin")]
[Authorize(Policy = "MfaVerified")]
[EnableRateLimiting("SuperAdminPolicy")]
internal class TenantsController(TenantService tenantService) : ControllerBase
{
    private Guid AdminId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string Ip => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    private string Ua => Request.Headers.UserAgent.ToString();

    [HttpGet(ApiUrl.AdminTenants.GetAll)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetAll([FromQuery] TenantFilterRequest filter)
    {
        ResponseData<PagedResult<TenantResponse>> response = await tenantService.GetAllAsync(filter);
        return StatusCode(response.StatusCode, response);
    }

    [HttpGet(ApiUrl.AdminTenants.GetById)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetById(Guid id)
    {
        ResponseData<TenantResponse> response = await tenantService.GetByIdAsync(id);
        return StatusCode(response.StatusCode, response);
    }

    [HttpPost(ApiUrl.AdminTenants.Create)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Create([FromBody] CreateTenantRequest request)
    {
        ResponseData<TenantResponse> response = await tenantService.CreateAsync(request, AdminId, Ip, Ua);
        return StatusCode(response.StatusCode, response);
    }

    [HttpPut(ApiUrl.AdminTenants.Update)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateTenantRequest request)
    {
        ResponseData<TenantResponse> response = await tenantService.UpdateAsync(id, request, AdminId, Ip, Ua);
        return StatusCode(response.StatusCode, response);
    }

    [HttpPut(ApiUrl.AdminTenants.Suspend)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Suspend(Guid id)
    {
        ResponseData<bool> response = await tenantService.SuspendAsync(id, AdminId, Ip, Ua);
        return StatusCode(response.StatusCode, response);
    }

    [HttpPut(ApiUrl.AdminTenants.Activate)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Activate(Guid id)
    {
        ResponseData<bool> response = await tenantService.ActivateAsync(id, AdminId, Ip, Ua);
        return StatusCode(response.StatusCode, response);
    }

    [HttpDelete(ApiUrl.AdminTenants.Delete)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Delete(Guid id)
    {
        ResponseData<bool> response = await tenantService.DeleteAsync(id, AdminId, Ip, Ua);
        return StatusCode(response.StatusCode, response);
    }
}
