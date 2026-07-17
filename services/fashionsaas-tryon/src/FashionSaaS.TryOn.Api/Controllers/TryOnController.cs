using System.Diagnostics.CodeAnalysis;
using FashionSaaS.TryOn.Api.Common;
using FashionSaaS.TryOn.Application.TryOn;
using FashionSaaS.TryOn.Infrastructure.TryOn;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FashionSaaS.TryOn.Api.Controllers;

[ApiController]
[Route("api/tryon")]
[Authorize]
// CA1515: MVC's default ControllerFeatureProvider only discovers public top-level classes
// (verified: dotnet/aspnetcore#12796) — an internal controller here is never routed, so this
// type must stay public despite the "no public API surface" rule the analyzer assumes. — 2026-07-11
[SuppressMessage("Design", "CA1515:Consider making public types internal", Justification = "ASP.NET Core MVC controller discovery requires public top-level classes.")]
public class TryOnController(TryOnService tryOnService) : ControllerBase
{
    [HttpPost]
    [RequestSizeLimit(15_000_000)]
    public async Task<IActionResult> PostAsync([FromForm] TryOnRequestForm form, CancellationToken cancellationToken)
    {
        (var isSuccess, var statusCode, var message, TryOnResultResponse? data) = await tryOnService.RenderAsync(form, cancellationToken);

        ResponseData<TryOnResultResponse> response = isSuccess
            ? ResponseData<TryOnResultResponse>.Success(data!, message, statusCode)
            : ResponseData<TryOnResultResponse>.Failure(message, statusCode);

        return StatusCode(response.StatusCode, response);
    }
}
