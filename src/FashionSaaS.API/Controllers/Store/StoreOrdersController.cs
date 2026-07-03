using System.Security.Claims;
using FashionSaaS.API.Constants;
using FashionSaaS.Application.Common;
using FashionSaaS.Application.Orders;
using FashionSaaS.Application.Orders.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FashionSaaS.API.Controllers.Store;

[ApiController]
[Authorize(Roles = "Customer")]
[EnableRateLimiting("AuthenticatedPolicy")]
public class StoreOrdersController(OrderService orderService) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string Email => User.FindFirstValue(ClaimTypes.Email)!;
    private string Ip => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    private string Ua => Request.Headers.UserAgent.ToString();

    [HttpPost(ApiUrl.StoreOrders.Create)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Create([FromBody] CreateOrderRequest request)
    {
        var firstName = request.ShippingAddress.FirstName;
        var lastName = request.ShippingAddress.LastName;
        var response = await orderService.CreateAsync(Email, firstName, lastName,
            request.ShippingAddress.Phone, request, UserId, Ip, Ua);
        return StatusCode(response.StatusCode, response);
    }

    [HttpGet(ApiUrl.StoreOrders.GetMine)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetMine([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var response = await orderService.GetForCustomerAsync(Email, page, pageSize);
        return StatusCode(response.StatusCode, response);
    }

    [HttpGet(ApiUrl.StoreOrders.GetById)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetById(Guid id)
    {
        var response = await orderService.GetByIdForCustomerAsync(id, Email);
        return StatusCode(response.StatusCode, response);
    }

    [HttpPut(ApiUrl.StoreOrders.Cancel)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Cancel(Guid id, [FromBody] CancelOrderRequest body)
    {
        var response = await orderService.CancelAsync(id, body.Reason, asCustomer: true, Email, UserId, Ip, Ua);
        return StatusCode(response.StatusCode, response);
    }
}
