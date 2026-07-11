using System.Security.Claims;
using FashionSaaS.API.Constants;
using FashionSaaS.Application.Common;
using FashionSaaS.Application.Wishlists;
using FashionSaaS.Application.Wishlists.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FashionSaaS.API.Controllers.Tenant;

[ApiController]
[Authorize(Roles = "AdminOwner,StoreManager")]
[EnableRateLimiting("AuthenticatedPolicy")]
internal class WishlistsController(WishlistService wishlistService) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string Ip => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    private string Ua => Request.Headers.UserAgent.ToString();

    [HttpGet(ApiUrl.TenantWishlists.GetByCustomer)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetByCustomer(Guid customerId)
    {
        ResponseData<WishlistResponse> response = await wishlistService.GetByCustomerAsync(customerId);
        return StatusCode(response.StatusCode, response);
    }

    [HttpDelete(ApiUrl.TenantWishlists.RemoveItem)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> RemoveItem(Guid itemId)
    {
        ResponseData<bool> response = await wishlistService.RemoveItemAsync(itemId, UserId, Ip, Ua);
        return StatusCode(response.StatusCode, response);
    }
}
