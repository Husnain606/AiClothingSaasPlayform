namespace FashionSaaS.API.Middleware;

internal class SecurityHeadersMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        // M3: X-XSS-Protection removed — deprecated, flagged by security scanners
        // M2: Strict-Transport-Security removed — Program.cs already calls app.UseHsts() to avoid duplicate header
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["X-Frame-Options"] = "DENY";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        context.Response.Headers["Content-Security-Policy"] = "default-src 'self'";
        await next(context);
    }
}
