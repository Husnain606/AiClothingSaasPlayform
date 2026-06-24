using System.Security.Claims;
using FashionSaaS.API.Constants;
using FashionSaaS.Application.Common;
using FashionSaaS.Application.Interfaces;
using FashionSaaS.Application.Users;
using FashionSaaS.Application.Users.DTOs;
using FashionSaaS.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FashionSaaS.API.Controllers.Tenant;

[ApiController]
[Authorize(Roles = "AdminOwner")]
[EnableRateLimiting("AuthenticatedPolicy")]
public class TenantUsersController(UserService userService, ICurrentTenantService currentTenant) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string Ip => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    private string Ua => Request.Headers.UserAgent.ToString();

    [HttpGet(ApiUrl.TenantUsers.GetAll)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetAll([FromQuery] UserFilterRequest filter)
    {
        var response = await userService.GetByTenantAsync(currentTenant.TenantId!.Value, filter);
        return StatusCode(response.StatusCode, response);
    }

    /// <summary>
    /// Returns a single user — only if they belong to the current tenant (cross-tenant access prevented).
    /// </summary>
    [HttpGet(ApiUrl.TenantUsers.GetById)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetById(Guid id)
    {
        var response = await userService.GetByIdAsync(id);
        if (!response.IsSuccess) return StatusCode(response.StatusCode, response);

        // Enforce tenant scope: a tenant admin may only read users in their own tenant.
        if (response.Data?.TenantId != currentTenant.TenantId)
            return StatusCode(403, ResponseData<string>.Failure("Forbidden: user does not belong to your tenant.", 403));

        return StatusCode(response.StatusCode, response);
    }

    [HttpPost(ApiUrl.TenantUsers.Create)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Create([FromBody] CreateUserRequest request)
    {
        // Force the user into the current tenant — caller cannot target another tenant.
        request.TenantId = currentTenant.TenantId;
        var response = await userService.CreateAsync(request, UserId, Ip, Ua);
        return StatusCode(response.StatusCode, response);
    }

    [HttpPut(ApiUrl.TenantUsers.Update)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateUserRequest request)
    {
        // Verify the target user belongs to this tenant before allowing update.
        var check = await userService.GetByIdAsync(id);
        if (!check.IsSuccess) return StatusCode(check.StatusCode, check);
        if (check.Data?.TenantId != currentTenant.TenantId)
            return StatusCode(403, ResponseData<string>.Failure("Forbidden: user does not belong to your tenant.", 403));

        var response = await userService.UpdateAsync(id, request, UserId, Ip, Ua);
        return StatusCode(response.StatusCode, response);
    }

    [HttpPut(ApiUrl.TenantUsers.AssignRole)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> AssignRole(Guid id, [FromBody] RoleType role)
    {
        // Verify the target user belongs to this tenant before assigning a role.
        var check = await userService.GetByIdAsync(id);
        if (!check.IsSuccess) return StatusCode(check.StatusCode, check);
        if (check.Data?.TenantId != currentTenant.TenantId)
            return StatusCode(403, ResponseData<string>.Failure("Forbidden: user does not belong to your tenant.", 403));

        var response = await userService.AssignRoleAsync(id, role, UserId, Ip, Ua);
        return StatusCode(response.StatusCode, response);
    }

    [HttpDelete(ApiUrl.TenantUsers.Delete)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Delete(Guid id)
    {
        // Verify the target user belongs to this tenant before deletion.
        var check = await userService.GetByIdAsync(id);
        if (!check.IsSuccess) return StatusCode(check.StatusCode, check);
        if (check.Data?.TenantId != currentTenant.TenantId)
            return StatusCode(403, ResponseData<string>.Failure("Forbidden: user does not belong to your tenant.", 403));

        var response = await userService.DeleteAsync(id, UserId, Ip, Ua);
        return StatusCode(response.StatusCode, response);
    }
}
