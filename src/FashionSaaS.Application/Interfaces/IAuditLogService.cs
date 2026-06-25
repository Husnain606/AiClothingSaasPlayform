namespace FashionSaaS.Application.Interfaces;

public interface IAuditLogService
{
    Task LogAsync(Guid? userId, Guid? tenantId, string action, string entityName, Guid entityId,
        object? oldValues, object? newValues, string ipAddress, string userAgent);
}
