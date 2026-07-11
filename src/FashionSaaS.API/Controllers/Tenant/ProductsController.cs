using System.Security.Claims;
using FashionSaaS.API.Constants;
using FashionSaaS.Application.Common;
using FashionSaaS.Application.Products;
using FashionSaaS.Application.Products.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FashionSaaS.API.Controllers.Tenant;

[ApiController]
[Authorize(Roles = "AdminOwner,StoreManager,ContentManager")]
[EnableRateLimiting("AuthenticatedPolicy")]
internal class ProductsController(ProductService productService) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string Ip => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    private string Ua => Request.Headers.UserAgent.ToString();

    [HttpGet(ApiUrl.TenantProducts.GetAll)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetAll([FromQuery] ProductFilter filter)
    {
        ResponseData<PagedResult<ProductResponse>> response = await productService.GetAllAsync(filter);
        return StatusCode(response.StatusCode, response);
    }

    [HttpGet(ApiUrl.TenantProducts.GetById)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetById(Guid id)
    {
        ResponseData<ProductResponse> response = await productService.GetByIdAsync(id);
        return StatusCode(response.StatusCode, response);
    }

    [HttpGet(ApiUrl.TenantProducts.GetBySlug)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetBySlug(string slug)
    {
        ResponseData<ProductResponse> response = await productService.GetBySlugAsync(slug);
        return StatusCode(response.StatusCode, response);
    }

    [HttpPost(ApiUrl.TenantProducts.Create)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Create([FromBody] CreateProductRequest request)
    {
        ResponseData<ProductResponse> response = await productService.CreateAsync(request, UserId, Ip, Ua);
        return StatusCode(response.StatusCode, response);
    }

    [HttpPut(ApiUrl.TenantProducts.Update)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateProductRequest request)
    {
        ResponseData<ProductResponse> response = await productService.UpdateAsync(id, request, UserId, Ip, Ua);
        return StatusCode(response.StatusCode, response);
    }

    [HttpPost(ApiUrl.TenantProducts.Publish)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Publish(Guid id)
    {
        ResponseData<ProductResponse> response = await productService.PublishAsync(id, UserId, Ip, Ua);
        return StatusCode(response.StatusCode, response);
    }

    [HttpPost(ApiUrl.TenantProducts.Archive)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Archive(Guid id)
    {
        ResponseData<ProductResponse> response = await productService.ArchiveAsync(id, UserId, Ip, Ua);
        return StatusCode(response.StatusCode, response);
    }

    [HttpDelete(ApiUrl.TenantProducts.Delete)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Delete(Guid id)
    {
        ResponseData<bool> response = await productService.DeleteAsync(id, UserId, Ip, Ua);
        return StatusCode(response.StatusCode, response);
    }
}
