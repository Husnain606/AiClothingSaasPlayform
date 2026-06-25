using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using FashionSaaS.API.Constants;
using FashionSaaS.Application.Auth;
using FashionSaaS.Application.Auth.DTOs;
using FashionSaaS.Application.Common;
using FashionSaaS.Application.Configuration;
using FashionSaaS.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace FashionSaaS.API.Controllers.Auth;

[ApiController]
public class AuthController(AuthService authService, IPasswordResetTokenRepository resetTokenRepo,
    IPasswordHistoryRepository historyRepo, IOptions<JwtSettings> jwtOptions) : ControllerBase
{
    private string Ip => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    private string Ua => Request.Headers.UserAgent.ToString();

    [AllowAnonymous]
    [HttpPost(ApiUrl.Auth.Login)]
    [EnableRateLimiting("PublicPolicy")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var response = await authService.LoginAsync(request, Ip, Ua);
        if (response.IsSuccess && response.Data?.RefreshToken is not null)
            SetRefreshTokenCookie(response.Data.RefreshToken);
        if (response.Data is not null)
            response.Data.RefreshToken = null;
        return StatusCode(response.StatusCode, response);
    }

    [AllowAnonymous]
    [HttpPost(ApiUrl.Auth.LoginMfa)]
    [EnableRateLimiting("PublicPolicy")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> LoginMfa([FromBody] LoginMfaRequest request,
        [FromServices] ITotpService totpService)
    {
        var response = await authService.LoginMfaAsync(request, totpService, Ip, Ua);
        if (response.IsSuccess && response.Data?.RefreshToken is not null)
            SetRefreshTokenCookie(response.Data.RefreshToken);
        if (response.Data is not null)
            response.Data.RefreshToken = null;
        return StatusCode(response.StatusCode, response);
    }

    [AllowAnonymous]
    [HttpPost(ApiUrl.Auth.Refresh)]
    [EnableRateLimiting("PublicPolicy")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Refresh()
    {
        var rawToken = Request.Cookies["refreshToken"];
        var bearerToken = Request.Headers.Authorization.ToString();

        if (string.IsNullOrEmpty(rawToken))
            return StatusCode(401, ResponseData<string>.Failure("Invalid session.", 401));

        // Extract userId from the access token by validating its SIGNATURE but ignoring lifetime.
        // This allows refresh to work when the access token is expired (the primary use case).
        // The security gate remains the HttpOnly+Secure+SameSite=Strict refresh cookie.
        var accessToken = bearerToken.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? bearerToken["Bearer ".Length..].Trim()
            : null;

        if (string.IsNullOrEmpty(accessToken))
            return StatusCode(401, ResponseData<string>.Failure("Invalid session.", 401));

        var jwt = jwtOptions.Value;
        if (string.IsNullOrEmpty(jwt.Secret))
            return StatusCode(401, ResponseData<string>.Failure("Invalid session.", 401));

        ClaimsPrincipal principal;
        try
        {
            var handler = new JwtSecurityTokenHandler();
            principal = handler.ValidateToken(accessToken, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Secret)),
                ValidateIssuer = true,
                ValidIssuer = jwt.Issuer,
                ValidateAudience = true,
                ValidAudience = jwt.Audience,
                ValidateLifetime = false  // intentionally ignore expiry — cookie is the credential
            }, out _);
        }
        catch
        {
            return StatusCode(401, ResponseData<string>.Failure("Invalid session.", 401));
        }

        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userId, out var uid))
            return StatusCode(401, ResponseData<string>.Failure("Invalid session.", 401));

        var response = await authService.RefreshTokenByUserIdAsync(uid, rawToken, Ip, Ua);
        if (response.IsSuccess && response.Data?.RefreshToken is not null)
            SetRefreshTokenCookie(response.Data.RefreshToken);
        if (response.Data is not null)
            response.Data.RefreshToken = null;
        return StatusCode(response.StatusCode, response);
    }

    [Authorize]
    [HttpPost(ApiUrl.Auth.Logout)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Logout()
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        DeleteRefreshTokenCookie();
        var response = await authService.LogoutAsync(userId);
        return StatusCode(response.StatusCode, response);
    }

    [AllowAnonymous]
    [HttpPost(ApiUrl.Auth.ForgotPassword)]
    [EnableRateLimiting("PublicPolicy")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request)
    {
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var response = await authService.ForgotPasswordAsync(request.Email, baseUrl, resetTokenRepo);
        return StatusCode(response.StatusCode, response);
    }

    [AllowAnonymous]
    [HttpPost(ApiUrl.Auth.ResetPassword)]
    [EnableRateLimiting("PublicPolicy")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
    {
        var response = await authService.ResetPasswordAsync(request, resetTokenRepo, historyRepo);
        return StatusCode(response.StatusCode, response);
    }

    [Authorize]
    [HttpPut(ApiUrl.Auth.ChangePassword)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        DeleteRefreshTokenCookie();
        var response = await authService.ChangePasswordAsync(userId, request, historyRepo);
        return StatusCode(response.StatusCode, response);
    }

    private void SetRefreshTokenCookie(string rawToken)
    {
        Response.Cookies.Append("refreshToken", rawToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Expires = DateTimeOffset.UtcNow.AddDays(7)
        });
    }

    private void DeleteRefreshTokenCookie()
        => Response.Cookies.Delete("refreshToken");
}
