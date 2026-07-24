using System.Security.Claims;
using FashionSaaS.API.Constants;
using FashionSaaS.Application.Common;
using FashionSaaS.Application.Wishlists;
using FashionSaaS.Application.Wishlists.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FashionSaaS.API.Controllers.Store;

[ApiController]
[Authorize(Roles = "Customer")]
[EnableRateLimiting("AuthenticatedPolicy")]
public class StoreWishlistController(WishlistService wishlistService) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string Email => User.FindFirstValue(ClaimTypes.Email)!;
    private string Ip => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    private string Ua => Request.Headers.UserAgent.ToString();

    [HttpGet(ApiUrl.AccountWishlist.GetMine)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetMine(CancellationToken ct)
    {
        // First/last name aren't collected on this minimal endpoint — GetOrCreateByEmailAsync
        // only uses them to seed a brand-new Customer row; an existing customer keeps their
        // real name untouched. Same pattern as StoreReviewsController.Submit.
        ResponseData<WishlistResponse> response = await wishlistService.GetMineAsync(Email, "Customer", string.Empty, null, ct);
        return StatusCode(response.StatusCode, response);
    }

    [HttpPost(ApiUrl.AccountWishlist.Add)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Add([FromBody] AddWishlistItemRequest request, CancellationToken ct)
    {
        ResponseData<WishlistItemResponse> response = await wishlistService.AddItemAsync(
            Email, "Customer", string.Empty, null, request, UserId, Ip, Ua, ct);
        return StatusCode(response.StatusCode, response);
    }

    [HttpDelete(ApiUrl.AccountWishlist.Remove)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Remove(Guid itemId, CancellationToken ct)
    {
        ResponseData<bool> response = await wishlistService.RemoveMyItemAsync(Email, itemId, UserId, Ip, Ua, ct);
        return StatusCode(response.StatusCode, response);
    }
}
