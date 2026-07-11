using System.Security.Claims;
using FashionSaaS.API.Constants;
using FashionSaaS.Application.Common;
using FashionSaaS.Application.Discounts;
using FashionSaaS.Application.Discounts.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FashionSaaS.API.Controllers.Tenant;

[ApiController]
[Authorize(Roles = "AdminOwner,StoreManager")]
[EnableRateLimiting("AuthenticatedPolicy")]
internal class DiscountsController(DiscountService discountService) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string Ip => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    private string Ua => Request.Headers.UserAgent.ToString();

    [HttpGet(ApiUrl.TenantDiscounts.GetAll)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetAll([FromQuery] DiscountFilter filter)
    {
        ResponseData<PagedResult<DiscountResponse>> response = await discountService.GetAllAsync(filter);
        return StatusCode(response.StatusCode, response);
    }

    [HttpGet(ApiUrl.TenantDiscounts.GetById)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetById(Guid id)
    {
        ResponseData<DiscountResponse> response = await discountService.GetByIdAsync(id);
        return StatusCode(response.StatusCode, response);
    }

    [HttpGet(ApiUrl.TenantDiscounts.GetByCode)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetByCode(string code)
    {
        ResponseData<DiscountResponse> response = await discountService.GetByCodeAsync(code);
        return StatusCode(response.StatusCode, response);
    }

    [HttpPost(ApiUrl.TenantDiscounts.Create)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Create([FromBody] CreateDiscountRequest request)
    {
        ResponseData<DiscountResponse> response = await discountService.CreateAsync(request, UserId, Ip, Ua);
        return StatusCode(response.StatusCode, response);
    }

    [HttpPut(ApiUrl.TenantDiscounts.Update)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateDiscountRequest request)
    {
        ResponseData<DiscountResponse> response = await discountService.UpdateAsync(id, request, UserId, Ip, Ua);
        return StatusCode(response.StatusCode, response);
    }

    [HttpPost(ApiUrl.TenantDiscounts.Deactivate)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Deactivate(Guid id)
    {
        ResponseData<bool> response = await discountService.DeactivateAsync(id, UserId, Ip, Ua);
        return StatusCode(response.StatusCode, response);
    }

    [HttpDelete(ApiUrl.TenantDiscounts.Delete)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Delete(Guid id)
    {
        ResponseData<bool> response = await discountService.DeleteAsync(id, UserId, Ip, Ua);
        return StatusCode(response.StatusCode, response);
    }
}
