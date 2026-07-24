using System.Security.Claims;
using FashionSaaS.API.Constants;
using FashionSaaS.Application.Common;
using FashionSaaS.Application.Mfa;
using FashionSaaS.Application.Mfa.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FashionSaaS.API.Controllers.Tenant;

/// <summary>
/// MFA enrollment for tenant staff (AdminOwner, StoreManager). Mirrors
/// Admin/MfaController's flow against the same MfaService — tenant staff need
/// this to satisfy step-up TOTP re-verification (e.g. TenantBankAccountController.GetFull).
/// </summary>
[ApiController]
[Authorize(Roles = "AdminOwner,StoreManager")]
[EnableRateLimiting("AuthenticatedPolicy")]
public class TenantMfaController(MfaService mfaService) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet(ApiUrl.TenantMfa.Setup)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Setup()
    {
        ResponseData<MfaSetupResponse> response = await mfaService.SetupAsync(UserId);
        return StatusCode(response.StatusCode, response);
    }

    [HttpPost(ApiUrl.TenantMfa.VerifySetup)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> VerifySetup([FromBody] VerifySetupRequest request)
    {
        ResponseData<IReadOnlyList<string>> response = await mfaService.VerifySetupAsync(UserId, request.Code);
        return StatusCode(response.StatusCode, response);
    }

    [HttpPost(ApiUrl.TenantMfa.RegenerateBackupCodes)]
    [Authorize(Policy = "MfaVerified")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> RegenerateBackupCodes()
    {
        ResponseData<IReadOnlyList<string>> response = await mfaService.RegenerateBackupCodesAsync(UserId);
        return StatusCode(response.StatusCode, response);
    }

    public record VerifySetupRequest(string Code);
}
