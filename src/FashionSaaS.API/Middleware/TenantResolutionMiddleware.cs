namespace FashionSaaS.API.Middleware;

/// <summary>
/// Stub — Task 21 implements full tenant resolution from JWT claims / header.
/// </summary>
public class TenantResolutionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        await next(context);
    }
}
