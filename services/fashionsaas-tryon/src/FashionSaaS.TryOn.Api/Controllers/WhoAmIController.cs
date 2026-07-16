using System.Diagnostics.CodeAnalysis;
using FashionSaaS.TryOn.Api.Common;
using FashionSaaS.TryOn.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FashionSaaS.TryOn.Api.Controllers;

[ApiController]
[Route("api/whoami")]
[Authorize]
// CA1515: MVC's default ControllerFeatureProvider only discovers public top-level classes
// (verified: dotnet/aspnetcore#12796) — an internal controller here is never routed, so this
// type must stay public despite the "no public API surface" rule the analyzer assumes. — 2026-07-11
[SuppressMessage("Design", "CA1515:Consider making public types internal", Justification = "ASP.NET Core MVC controller discovery requires public top-level classes.")]
public class WhoAmIController(ICurrentTryOnContext context) : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        var response = ResponseData<object>.Success(new
        {
            context.TenantId,
            context.CustomerId,
            context.AiUsageLimit
        });
        return StatusCode(response.StatusCode, response);
    }
}
