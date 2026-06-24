using System.Security.Claims;
using System.Text.Json;
using FashionSaaS.Application.Common;
using FashionSaaS.Application.Interfaces;

namespace FashionSaaS.API.Middleware;

public class TenantResolutionMiddleware(RequestDelegate next)
{
    private static readonly JsonSerializerOptions JsonOptions =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public async Task InvokeAsync(HttpContext context,
        ITenantRepository tenantRepository, ICurrentTenantService currentTenantService)
    {
        var slug = context.GetRouteValue("slug")?.ToString();

        if (!string.IsNullOrEmpty(slug))
        {
            // Route slug takes priority
            var tenant = await tenantRepository.GetBySlugAsync(slug);
            if (tenant is null)
            {
                await WriteError(context, 404, $"Tenant '{slug}' not found.");
                return;
            }

            if (!tenant.IsActive)
            {
                await WriteError(context, 403, "This store is currently suspended.");
                return;
            }

            currentTenantService.SetTenant(tenant.Id, tenant.Slug);
        }
        else if (context.User?.Identity?.IsAuthenticated == true)
        {
            // M1: JWT tenant_id claim fallback for authenticated requests without a slug segment
            var tenantIdClaim = context.User.FindFirstValue("tenant_id");
            if (Guid.TryParse(tenantIdClaim, out var tenantId))
            {
                var tenantSlugClaim = context.User.FindFirstValue("tenant_slug");

                if (!string.IsNullOrEmpty(tenantSlugClaim))
                {
                    // slug available directly from JWT
                    currentTenantService.SetTenant(tenantId, tenantSlugClaim);
                }
                else
                {
                    // look up slug via repository
                    var tenant = await tenantRepository.GetByIdAsync(tenantId);
                    if (tenant is not null)
                        currentTenantService.SetTenant(tenant.Id, tenant.Slug);
                }
            }
            // No tenant claims — platform/SuperAdmin/auth route: skip silently
        }

        await next(context);
    }

    // I3: use ResponseData<string>.Failure so error shape matches ExceptionHandlingMiddleware
    private static async Task WriteError(HttpContext context, int statusCode, string message)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";
        var body = ResponseData<string>.Failure(message, statusCode);
        await context.Response.WriteAsync(JsonSerializer.Serialize(body, JsonOptions));
    }
}
