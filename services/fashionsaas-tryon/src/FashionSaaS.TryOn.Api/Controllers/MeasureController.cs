using System.Diagnostics.CodeAnalysis;
using FashionSaaS.TryOn.Api.Common;
using FashionSaaS.TryOn.Application.Measurement;
using FashionSaaS.TryOn.Infrastructure.Measurement;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FashionSaaS.TryOn.Api.Controllers;

[ApiController]
[Route("api/measure")]
[Authorize]
// CA1515: MVC's default ControllerFeatureProvider only discovers public top-level classes
// (verified: dotnet/aspnetcore#12796) — an internal controller here is never routed, so this
// type must stay public despite the "no public API surface" rule the analyzer assumes.
[SuppressMessage("Design", "CA1515:Consider making public types internal", Justification = "ASP.NET Core MVC controller discovery requires public top-level classes.")]
public class MeasureController(MeasurementService measurementService) : ControllerBase
{
    [HttpPost]
    [RequestSizeLimit(15_000_000)]
    public async Task<IActionResult> PostAsync([FromForm] MeasurementRequestForm form, CancellationToken cancellationToken)
    {
        (var isSuccess, var statusCode, var message, MeasurementResultResponse? data) = await measurementService.EstimateAsync(form, cancellationToken);

        ResponseData<MeasurementResultResponse> response = isSuccess
            ? ResponseData<MeasurementResultResponse>.Success(data!, message, statusCode)
            : ResponseData<MeasurementResultResponse>.Failure(message, statusCode);

        return StatusCode(response.StatusCode, response);
    }
}
