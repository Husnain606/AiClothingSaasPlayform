using FashionSaaS.Domain.Entities;

namespace FashionSaaS.Application.Interfaces;

public interface IAuditLogRepository : IGenericRepository<AuditLog>
{
    Task<IReadOnlyList<AuditLog>> GetPagedAsync(string? action, string? entityName, Guid? userId, DateTime? from, DateTime? to, int page, int pageSize);
    Task<int> GetTotalCountAsync(string? action, string? entityName, Guid? userId, DateTime? from, DateTime? to);
}
