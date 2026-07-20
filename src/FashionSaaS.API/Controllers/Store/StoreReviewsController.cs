using System.Security.Claims;
using FashionSaaS.API.Constants;
using FashionSaaS.Application.Common;
using FashionSaaS.Application.Reviews;
using FashionSaaS.Application.Reviews.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FashionSaaS.API.Controllers.Store;

[ApiController]
[Authorize(Roles = "Customer")]
[EnableRateLimiting("AuthenticatedPolicy")]
public class StoreReviewsController(ReviewService reviewService) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string Email => User.FindFirstValue(ClaimTypes.Email)!;
    private string Ip => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    private string Ua => Request.Headers.UserAgent.ToString();

    [HttpPost(ApiUrl.StoreReviews.Submit)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Submit([FromBody] SubmitReviewRequest request)
    {
        // First/last name aren't collected on this minimal endpoint — GetOrCreateByEmailAsync
        // only uses them to seed a brand-new Customer row; an existing customer (the common
        // case — reviews follow a purchase) keeps their real name untouched.
        ResponseData<ReviewResponse> response = await reviewService.SubmitAsync(
            Email, "Customer", string.Empty, null, request, UserId, Ip, Ua);
        return StatusCode(response.StatusCode, response);
    }
}
