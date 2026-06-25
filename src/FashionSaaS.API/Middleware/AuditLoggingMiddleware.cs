using System.Security.Claims;
using FashionSaaS.Application.Interfaces;

namespace FashionSaaS.API.Middleware;

public class AuditLoggingMiddleware(RequestDelegate next, ILogger<AuditLoggingMiddleware> logger)
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

        // I1: guard against missing or non-GUID NameIdentifier claim
        Guid? uid = Guid.TryParse(userId, out var parsedUid) ? parsedUid : null;

        try
        {
            // I2: isolate audit failures — a DB/audit error must never surface on an otherwise-successful response
            await auditLogService.LogAsync(
                uid,
                tenantId is not null && Guid.TryParse(tenantId, out var tid) ? tid : null,
                $"{method} {path}",
                "HttpRequest",
                Guid.NewGuid(),
                null,
                new { Path = path, StatusCode = context.Response.StatusCode },
                ip, ua);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Audit logging failed for {Method} {Path}", method, path);
        }
    }
}
