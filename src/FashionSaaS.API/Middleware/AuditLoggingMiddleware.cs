using System.Security.Claims;
using FashionSaaS.Application.Interfaces;

namespace FashionSaaS.API.Middleware;

public class AuditLoggingMiddleware(RequestDelegate next)
{
    private static readonly HashSet<string> WriteMethods =
        new(StringComparer.OrdinalIgnoreCase) { "POST", "PUT", "DELETE", "PATCH" };

    public async Task InvokeAsync(HttpContext context, IAuditLogService auditLogService)
    {
        await next(context);

        if (!WriteMethods.Contains(context.Request.Method)) return;
        if (context.Response.StatusCode is < 200 or >= 400) return;

        var userId = context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        var tenantId = context.User?.FindFirstValue("tenant_id");
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var ua = context.Request.Headers.UserAgent.ToString();
        var path = context.Request.Path.Value ?? string.Empty;
        var method = context.Request.Method;

        await auditLogService.LogAsync(
            userId is not null ? Guid.Parse(userId) : null,
            tenantId is not null && Guid.TryParse(tenantId, out var tid) ? tid : null,
            $"{method} {path}",
            "HttpRequest",
            Guid.NewGuid(),
            null,
            new { Path = path, StatusCode = context.Response.StatusCode },
            ip, ua);
    }
}
