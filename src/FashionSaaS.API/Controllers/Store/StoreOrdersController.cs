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

    /// <summary>Maximum accepted payment-proof size (10 MB).</summary>
    private const long MaxProofBytes = 10485760;

    [HttpPost(ApiUrl.StoreOrders.Create)]
    [RequestSizeLimit(MaxProofBytes)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Create([FromForm] CreateOrderRequest request, IFormFile? paymentProof,
        CancellationToken ct)
    {
        if (paymentProof is null || paymentProof.Length == 0)
            return StatusCode(400, ResponseData<string>.Failure("A payment proof file is required.", 400));

        if (paymentProof.Length > MaxProofBytes)
            return StatusCode(400, ResponseData<string>.Failure("Payment proof must be 10 MB or smaller.", 400));

        var firstName = request.ShippingAddress.FirstName;
        var lastName = request.ShippingAddress.LastName;

        // Buffer to memory so the service can read the magic-number header and then re-read from
        // the start; the 10 MB cap above bounds this. IFormFile streams are not reliably seekable.
        using var buffered = new MemoryStream();
        await using (Stream upload = paymentProof.OpenReadStream())
        {
            await upload.CopyToAsync(buffered, ct);
        }

        buffered.Position = 0;

        ResponseData<OrderDto> response = await orderService.CreateAsync(Email, firstName, lastName,
            request.ShippingAddress.Phone, request, UserId, Ip, Ua,
            buffered, paymentProof.FileName, paymentProof.ContentType, paymentProof.Length, ct);

        return StatusCode(response.StatusCode, response);
    }

    [HttpGet(ApiUrl.StoreOrders.GetMine)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetMine([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        ResponseData<PagedResult<OrderDto>> response = await orderService.GetForCustomerAsync(Email, page, pageSize);
        return StatusCode(response.StatusCode, response);
    }

    [HttpGet(ApiUrl.StoreOrders.GetById)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetById(Guid id)
    {
        ResponseData<OrderDto> response = await orderService.GetByIdForCustomerAsync(id, Email);
        return StatusCode(response.StatusCode, response);
    }

    [HttpPut(ApiUrl.StoreOrders.Cancel)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Cancel(Guid id, [FromBody] CancelOrderRequest body)
    {
        ResponseData<OrderDto> response = await orderService.CancelAsync(id, body.Reason, asCustomer: true, Email, UserId, Ip, Ua);
        return StatusCode(response.StatusCode, response);
    }
}
