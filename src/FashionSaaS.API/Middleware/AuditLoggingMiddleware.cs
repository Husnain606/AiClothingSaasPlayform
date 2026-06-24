namespace FashionSaaS.API.Middleware;

/// <summary>
/// Stub — Task 21 implements full audit-logging (writes AuditLog per request).
/// </summary>
public class AuditLoggingMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        await next(context);
    }
}
