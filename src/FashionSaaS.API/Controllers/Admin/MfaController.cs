using System.Security.Claims;
using FashionSaaS.API.Constants;
using FashionSaaS.Application.Common;
using FashionSaaS.Application.Mfa;
using FashionSaaS.Application.Mfa.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FashionSaaS.API.Controllers.Admin;

[ApiController]
[Authorize(Roles = "SuperAdmin")]
[EnableRateLimiting("SuperAdminPolicy")]
internal class MfaController(MfaService mfaService) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet(ApiUrl.AdminMfa.Setup)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Setup()
    {
        ResponseData<MfaSetupResponse> response = await mfaService.SetupAsync(UserId);
        return StatusCode(response.StatusCode, response);
    }

    [HttpPost(ApiUrl.AdminMfa.VerifySetup)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> VerifySetup([FromBody] VerifySetupRequest request)
    {
        ResponseData<IReadOnlyList<string>> response = await mfaService.VerifySetupAsync(UserId, request.Code);
        return StatusCode(response.StatusCode, response);
    }

    [HttpPost(ApiUrl.AdminMfa.RegenerateBackupCodes)]
    [Authorize(Policy = "MfaVerified")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> RegenerateBackupCodes()
    {
        ResponseData<IReadOnlyList<string>> response = await mfaService.RegenerateBackupCodesAsync(UserId);
        return StatusCode(response.StatusCode, response);
    }

    internal record VerifySetupRequest(string Code);
}
