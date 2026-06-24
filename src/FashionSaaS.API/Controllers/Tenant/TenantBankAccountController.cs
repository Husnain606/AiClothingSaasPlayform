using System.Security.Claims;
using FashionSaaS.API.Constants;
using FashionSaaS.Application.BankAccounts;
using FashionSaaS.Application.BankAccounts.DTOs;
using FashionSaaS.Application.Common;
using FashionSaaS.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FashionSaaS.API.Controllers.Tenant;

[ApiController]
[Authorize(Roles = "AdminOwner")]
[EnableRateLimiting("AuthenticatedPolicy")]
public class TenantBankAccountController(BankAccountService bankAccountService, ICurrentTenantService currentTenant) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string Ip => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    private string Ua => Request.Headers.UserAgent.ToString();

    /// <summary>
    /// Returns the current tenant's bank account with AccountNumber MASKED (****last4).
    /// Safe for display; use GetFull for the unmasked number.
    /// </summary>
    [HttpGet(ApiUrl.TenantBankAccount.Get)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Get()
    {
        var response = await bankAccountService.GetAsync(currentTenant.TenantId);
        return StatusCode(response.StatusCode, response);
    }

    /// <summary>
    /// Returns the current tenant's bank account with AccountNumber FULLY UNMASKED.
    /// AdminOwner only — single-fetch, scoped to the current tenant.
    /// SENSITIVE: never log the response body of this endpoint.
    /// </summary>
    [HttpGet(ApiUrl.TenantBankAccount.GetFull)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetFull()
    {
        var response = await bankAccountService.GetFullAsync(currentTenant.TenantId);
        return StatusCode(response.StatusCode, response);
    }

    [HttpPost(ApiUrl.TenantBankAccount.Create)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Create([FromBody] CreateBankAccountRequest request)
    {
        var response = await bankAccountService.CreateAsync(request, UserId, currentTenant.TenantId, Ip, Ua);
        return StatusCode(response.StatusCode, response);
    }

    [HttpPut(ApiUrl.TenantBankAccount.Update)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Update([FromBody] UpdateBankAccountRequest request)
    {
        var response = await bankAccountService.UpdateAsync(request, UserId, currentTenant.TenantId, Ip, Ua);
        return StatusCode(response.StatusCode, response);
    }
}
