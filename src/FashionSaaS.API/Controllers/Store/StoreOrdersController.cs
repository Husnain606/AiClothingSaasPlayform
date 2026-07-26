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

    /// <summary>
    /// Bounds the ENTIRE multipart request body (file bytes + boundaries + all the
    /// ShippingAddress.*/Items[] form fields), not just the file — deliberately larger than
    /// <see cref="PaymentProofContentTypes.MaxFileSizeBytes"/> to leave headroom for the
    /// surrounding multipart fields. It cannot read configuration: attribute arguments must be
    /// compile-time constants. The TRUE business-rule file-size cap is config-driven
    /// (<see cref="FashionSaaS.Application.Configuration.PaymentProofStorageSettings.MaxFileSizeBytes"/>) and is enforced
    /// inside <c>OrderService.CreateAsync</c>, which returns the proper <see cref="ResponseData{T}"/>
    /// 400 envelope instead of a bare 413.
    /// </summary>
    private const long MultipartRequestSizeLimitBytes = 11_000_000;

    [HttpPost(ApiUrl.StoreOrders.Create)]
    [RequestSizeLimit(MultipartRequestSizeLimitBytes)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Create([FromForm] CreateOrderRequest request, IFormFile? paymentProof,
        CancellationToken ct)
    {
        // No FluentValidation validator exists for the payment proof: IFormFile paymentProof is a
        // sibling action-method parameter, not a member of CreateOrderRequest, and FluentValidation's
        // auto-validation pipeline only validates model-bound DTOs — it cannot reach a parameter
        // outside the bound body. Presence is checked here; the true content-type, magic-number and
        // config-driven size checks happen in OrderService.CreateAsync (see PaymentProofContentTypes
        // and PaymentProofStorageSettings), which is the boundary that can actually read configuration.
        if (paymentProof is null || paymentProof.Length == 0)
            return StatusCode(400, ResponseData<string>.Failure("A payment proof file is required.", 400));

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

    /// <summary>
    /// Streams the caller's own payment proof. A non-owner receives 404 rather than 403 so the
    /// existence of another customer's order is never disclosed.
    /// </summary>
    [HttpGet(ApiUrl.StoreOrders.GetPaymentProof)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetPaymentProof(Guid id, CancellationToken ct)
    {
        ResponseData<PaymentProofFileDto> response = await orderService.GetProofForCustomerAsync(id, Email, ct);
        if (!response.IsSuccess || response.Data is null)
            return StatusCode(response.StatusCode, ResponseData<string>.Failure(response.Message, response.StatusCode));

        return File(response.Data.Content, response.Data.ContentType, response.Data.FileName);
    }
}
