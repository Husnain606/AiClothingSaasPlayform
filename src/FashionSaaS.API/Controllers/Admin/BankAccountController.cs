using System.Security.Claims;
using FashionSaaS.API.Constants;
using FashionSaaS.Application.BankAccounts;
using FashionSaaS.Application.BankAccounts.DTOs;
using FashionSaaS.Application.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FashionSaaS.API.Controllers.Admin;

[ApiController]
[Authorize(Roles = "SuperAdmin")]
[Authorize(Policy = "MfaVerified")]
[EnableRateLimiting("SuperAdminPolicy")]
public class BankAccountController(BankAccountService bankAccountService) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string Ip => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    private string Ua => Request.Headers.UserAgent.ToString();

    /// <summary>
    /// Returns the platform bank account with AccountNumber MASKED (****last4).
    /// Safe for list/summary use.
    /// </summary>
    [HttpGet(ApiUrl.AdminBankAccount.Get)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Get()
    {
        ResponseData<BankAccountResponse> response = await bankAccountService.GetAsync(null);
        return StatusCode(response.StatusCode, response);
    }

    /// <summary>
    /// Returns the platform bank account with AccountNumber FULLY UNMASKED.
    /// Requires a fresh TOTP code (step-up re-verification) — SuperAdmin + MFA-gated.
    /// </summary>
    [HttpPost(ApiUrl.AdminBankAccount.GetFull)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetFull([FromBody] VerifyTotpRequest request)
    {
        ResponseData<BankAccountFullResponse> response = await bankAccountService.GetFullAsync(null, UserId, request.TotpCode);
        return StatusCode(response.StatusCode, response);
    }

    [HttpPost(ApiUrl.AdminBankAccount.Create)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Create([FromBody] CreateBankAccountRequest request)
    {
        ResponseData<BankAccountResponse> response = await bankAccountService.CreateAsync(request, UserId, null, Ip, Ua);
        return StatusCode(response.StatusCode, response);
    }

    [HttpPut(ApiUrl.AdminBankAccount.Update)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Update([FromBody] UpdateBankAccountRequest request)
    {
        ResponseData<BankAccountResponse> response = await bankAccountService.UpdateAsync(request, UserId, null, Ip, Ua);
        return StatusCode(response.StatusCode, response);
    }
}
