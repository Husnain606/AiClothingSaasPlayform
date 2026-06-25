using System.Text.Json;
using FashionSaaS.Application.Interfaces;
using FashionSaaS.Domain.Entities;
using FashionSaaS.Infrastructure.Persistence;

namespace FashionSaaS.Infrastructure.Services;

public class AuditLogService(ApplicationDbContext context) : IAuditLogService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    public async Task LogAsync(Guid? userId, Guid? tenantId, string action, string entityName,
        Guid entityId, object? oldValues, object? newValues, string ipAddress, string userAgent)
    {
        var log = new AuditLog
        {
            UserId = userId,
            TenantId = tenantId,
            Action = action,
            EntityName = entityName,
            EntityId = entityId,
            OldValues = oldValues is not null ? JsonSerializer.Serialize(MaskSensitive(oldValues), JsonOptions) : null,
            NewValues = newValues is not null ? JsonSerializer.Serialize(MaskSensitive(newValues), JsonOptions) : null,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        context.AuditLogs.Add(log);
        await context.SaveChangesAsync();
    }

    private static object MaskSensitive(object obj)
    {
        var json = JsonSerializer.Serialize(obj, JsonOptions);
        var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(json) ?? new();

        var sensitiveKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Password", "PasswordHash", "Token", "TokenHash",
            "AccountNumber", "Iban", "TotpSecret", "Secret"
        };

        foreach (var key in dict.Keys.ToList())
        {
            if (sensitiveKeys.Contains(key))
                dict[key] = "***MASKED***";
        }

        return dict;
    }
}
