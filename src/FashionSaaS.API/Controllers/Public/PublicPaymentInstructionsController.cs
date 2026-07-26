using FashionSaaS.API.Constants;
using FashionSaaS.Application.Common;
using FashionSaaS.Application.Interfaces;
using FashionSaaS.Application.Tenants;
using FashionSaaS.Application.Tenants.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FashionSaaS.API.Controllers.Public;

/// <summary>
/// Public, unauthenticated payment instructions for a storefront. Tenant scoping comes from the
/// {slug} route segment resolved by TenantResolutionMiddleware.
/// <para>
/// Deliberately returns only the tenant-authored free-text instructions. The tenant's
/// BankAccount record is AES-256-GCM encrypted and gated behind AdminOwner/SuperAdmin, and is
/// never exposed here — the tenant decides exactly what payment detail customers see.
/// </para>
/// </summary>
[ApiController]
[AllowAnonymous]
[EnableRateLimiting("PublicPolicy")]
public class PublicPaymentInstructionsController(
    TenantService tenantService,
    ICurrentTenantService currentTenant) : ControllerBase
{
    [HttpGet(ApiUrl.PublicCatalog.GetPaymentInstructions)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get()
    {
        ResponseData<TenantResponse> response = await tenantService.GetByIdAsync(currentTenant.TenantId!.Value);
        if (!response.IsSuccess || response.Data is null)
            return StatusCode(404, ResponseData<string>.Failure("Store not found.", 404));

        // Unset instructions are a normal state, not an error — the storefront shows a fallback.
        return StatusCode(200, ResponseData<string>.Success(response.Data.PaymentInstructions ?? string.Empty));
    }
}
