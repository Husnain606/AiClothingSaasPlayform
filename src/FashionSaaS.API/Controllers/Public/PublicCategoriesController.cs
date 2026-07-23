using FashionSaaS.API.Constants;
using FashionSaaS.Application.Categories;
using FashionSaaS.Application.Categories.DTOs;
using FashionSaaS.Application.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FashionSaaS.API.Controllers.Public;

/// <summary>
/// Public, unauthenticated storefront catalog browsing. Tenant scoping comes from the
/// {slug} route segment resolved by TenantResolutionMiddleware into ICurrentTenantService
/// before this action runs — the same CategoryService the admin catalog uses is reused
/// as-is, with no additional filtering needed (categories have no draft/published state).
/// </summary>
[ApiController]
[AllowAnonymous]
[EnableRateLimiting("PublicPolicy")]
public class PublicCategoriesController(CategoryService categoryService) : ControllerBase
{
    [HttpGet(ApiUrl.PublicCatalog.GetCategories)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        ResponseData<IReadOnlyList<CategoryResponse>> response = await categoryService.GetAllAsync(ct);
        return StatusCode(response.StatusCode, response);
    }
}
