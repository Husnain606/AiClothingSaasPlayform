using System.Security.Claims;
using FashionSaaS.API.Constants;
using FashionSaaS.Application.Common;
using FashionSaaS.Application.Subscriptions;
using FashionSaaS.Application.Subscriptions.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FashionSaaS.API.Controllers.Admin;

[ApiController]
[Authorize(Roles = "SuperAdmin")]
[Authorize(Policy = "MfaVerified")]
[EnableRateLimiting("SuperAdminPolicy")]
public class SubscriptionsController(SubscriptionService subscriptionService) : ControllerBase
{
    private Guid AdminId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string Ip => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    private string Ua => Request.Headers.UserAgent.ToString();

    [HttpGet(ApiUrl.AdminSubscriptions.GetAll)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetAll()
    {
        var response = await subscriptionService.GetAllAsync();
        return StatusCode(response.StatusCode, response);
    }

    [HttpPost(ApiUrl.AdminSubscriptions.Assign)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Assign([FromBody] AssignSubscriptionRequest request)
    {
        var response = await subscriptionService.AssignAsync(request, AdminId, Ip, Ua);
        return StatusCode(response.StatusCode, response);
    }

    [HttpPut(ApiUrl.AdminSubscriptions.ChangePlan)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ChangePlan(Guid id, [FromBody] ChangePlanRequest request)
    {
        var response = await subscriptionService.ChangePlanAsync(id, request.NewPlanId, AdminId, Ip, Ua);
        return StatusCode(response.StatusCode, response);
    }

    [HttpPut(ApiUrl.AdminSubscriptions.Suspend)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Suspend(Guid id)
    {
        var response = await subscriptionService.SuspendAsync(id, AdminId, Ip, Ua);
        return StatusCode(response.StatusCode, response);
    }

    [HttpPut(ApiUrl.AdminSubscriptions.Reactivate)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Reactivate(Guid id)
    {
        var response = await subscriptionService.ReactivateAsync(id, AdminId, Ip, Ua);
        return StatusCode(response.StatusCode, response);
    }
}
