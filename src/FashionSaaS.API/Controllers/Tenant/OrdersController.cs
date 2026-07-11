using System.Security.Claims;
using FashionSaaS.API.Constants;
using FashionSaaS.Application.Common;
using FashionSaaS.Application.Orders;
using FashionSaaS.Application.Orders.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FashionSaaS.API.Controllers.Tenant;

[ApiController]
[Authorize(Roles = "AdminOwner,OrderManager,StoreManager")]
[EnableRateLimiting("AuthenticatedPolicy")]
internal class OrdersController(OrderService orderService) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string Ip => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    private string Ua => Request.Headers.UserAgent.ToString();

    [HttpGet(ApiUrl.TenantOrders.GetAll)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetAll([FromQuery] OrderFilter filter)
    {
        ResponseData<PagedResult<OrderDto>> response = await orderService.GetAllAsync(filter);
        return StatusCode(response.StatusCode, response);
    }

    [HttpGet(ApiUrl.TenantOrders.GetById)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetById(Guid id)
    {
        ResponseData<OrderDto> response = await orderService.GetByIdAsync(id);
        return StatusCode(response.StatusCode, response);
    }

    [HttpPut(ApiUrl.TenantOrders.Confirm)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Confirm(Guid id)
    {
        ResponseData<OrderDto> response = await orderService.ConfirmAsync(id, UserId, Ip, Ua);
        return StatusCode(response.StatusCode, response);
    }

    [HttpPut(ApiUrl.TenantOrders.Ship)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Ship(Guid id, [FromBody] ShipOrderRequest body)
    {
        ResponseData<OrderDto> response = await orderService.ShipAsync(id, body.TrackingNumber, UserId, Ip, Ua);
        return StatusCode(response.StatusCode, response);
    }

    [HttpPut(ApiUrl.TenantOrders.Deliver)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Deliver(Guid id)
    {
        ResponseData<OrderDto> response = await orderService.DeliverAsync(id, UserId, Ip, Ua);
        return StatusCode(response.StatusCode, response);
    }

    [HttpPut(ApiUrl.TenantOrders.Cancel)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Cancel(Guid id, [FromBody] CancelOrderRequest body)
    {
        ResponseData<OrderDto> response = await orderService.CancelAsync(id, body.Reason, asCustomer: false, null, UserId, Ip, Ua);
        return StatusCode(response.StatusCode, response);
    }
}
