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
internal class PaymentsController(SubscriptionService subscriptionService) : ControllerBase
{
    private Guid AdminId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string Ip => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    private string Ua => Request.Headers.UserAgent.ToString();

    [HttpGet(ApiUrl.AdminPayments.GetAll)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetAll([FromQuery] Guid subscriptionId)
    {
        ResponseData<IReadOnlyList<PaymentResponse>> response = await subscriptionService.GetPaymentsBySubscriptionAsync(subscriptionId);
        return StatusCode(response.StatusCode, response);
    }

    [HttpGet(ApiUrl.AdminPayments.GetById)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetById(Guid id)
    {
        ResponseData<PaymentResponse> response = await subscriptionService.GetPaymentByIdAsync(id);
        return StatusCode(response.StatusCode, response);
    }

    [HttpPut(ApiUrl.AdminPayments.Confirm)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Confirm(Guid id)
    {
        ResponseData<PaymentResponse> response = await subscriptionService.ConfirmPaymentAsync(id, AdminId, Ip, Ua);
        return StatusCode(response.StatusCode, response);
    }
}
