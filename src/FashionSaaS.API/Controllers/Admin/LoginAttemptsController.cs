using FashionSaaS.API.Constants;
using FashionSaaS.Application.Common;
using FashionSaaS.Application.LoginAttempts;
using FashionSaaS.Application.LoginAttempts.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FashionSaaS.API.Controllers.Admin;

[ApiController]
[Authorize(Roles = "SuperAdmin")]
[Authorize(Policy = "MfaVerified")]
[EnableRateLimiting("SuperAdminPolicy")]
public class LoginAttemptsController(LoginAttemptService loginAttemptService) : ControllerBase
{
    [HttpGet(ApiUrl.AdminLoginAttempts.GetAll)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetAll([FromQuery] LoginAttemptFilterRequest filter)
    {
        var response = await loginAttemptService.GetByEmailAsync(filter);
        return StatusCode(response.StatusCode, response);
    }
}
