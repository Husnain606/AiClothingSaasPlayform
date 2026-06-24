using FashionSaaS.Application.Interfaces;

namespace FashionSaaS.API.Middleware;

public class TenantResolutionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context,
        ITenantRepository tenantRepository, ICurrentTenantService currentTenantService)
    {
        var slug = context.GetRouteValue("slug")?.ToString();

        if (!string.IsNullOrEmpty(slug))
        {
            var tenant = await tenantRepository.GetBySlugAsync(slug);
            if (tenant is null)
            {
                context.Response.StatusCode = 404;
                await context.Response.WriteAsJsonAsync(new { message = $"Tenant '{slug}' not found." });
                return;
            }

            if (!tenant.IsActive)
            {
                context.Response.StatusCode = 403;
                await context.Response.WriteAsJsonAsync(new { message = "This store is currently suspended." });
                return;
            }

            currentTenantService.SetTenant(tenant.Id, tenant.Slug);
        }

        await next(context);
    }
}
