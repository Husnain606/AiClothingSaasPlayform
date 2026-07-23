using FashionSaaS.API.Constants;
using FashionSaaS.Application.Common;
using FashionSaaS.Application.Products;
using FashionSaaS.Application.Products.DTOs;
using FashionSaaS.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FashionSaaS.API.Controllers.Public;

/// <summary>
/// Public, unauthenticated storefront product browsing. Tenant scoping comes from the
/// {slug} route segment resolved by TenantResolutionMiddleware into ICurrentTenantService
/// before these actions run. Critical invariant: these endpoints must never expose a
/// Draft or Archived product. The list endpoint enforces this by building the
/// ProductFilter itself with Status hardcoded to Active — the caller-supplied
/// PublicProductFilter has no Status property, so there is nothing to bypass. The detail
/// endpoint reuses the existing ProductService.GetByIdAsync (no duplicated business
/// logic) and then gates the response on Status == Active, returning the same 404 shape
/// used for a genuinely missing product so a Draft product's existence is never leaked.
/// </summary>
[ApiController]
[AllowAnonymous]
[EnableRateLimiting("PublicPolicy")]
public class PublicProductsController(ProductService productService) : ControllerBase
{
    [HttpGet(ApiUrl.PublicCatalog.GetProducts)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAll([FromQuery] PublicProductFilter query, CancellationToken ct)
    {
        var filter = new ProductFilter
        {
            Search = query.Search,
            CategoryId = query.CategoryId,
            Page = query.Page,
            PageSize = query.PageSize,
            Status = ProductStatus.Active
        };

        ResponseData<PagedResult<ProductResponse>> response = await productService.GetAllAsync(filter, ct);
        return StatusCode(response.StatusCode, response);
    }

    [HttpGet(ApiUrl.PublicCatalog.GetProductById)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        ResponseData<ProductResponse> response = await productService.GetByIdAsync(id, ct);

        if (response.IsSuccess && response.Data?.Status != ProductStatus.Active)
        {
            // Draft/Archived products in the caller's own tenant must be invisible here —
            // reuse the exact 404 shape GetByIdAsync itself returns for a missing product.
            return StatusCode(404, ResponseData<string>.Failure("Product not found.", 404));
        }

        return StatusCode(response.StatusCode, response);
    }
}
