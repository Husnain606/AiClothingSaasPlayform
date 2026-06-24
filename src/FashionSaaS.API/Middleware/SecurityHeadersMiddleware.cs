namespace FashionSaaS.API.Middleware;

/// <summary>
/// Stub — Task 21 implements the full security-headers logic.
/// </summary>
public class SecurityHeadersMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        await next(context);
    }
}
