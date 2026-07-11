using FashionSaaS.Application.Interfaces;
using FashionSaaS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FashionSaaS.Infrastructure.Persistence.Repositories;

public class AuditLogRepository(ApplicationDbContext context)
    : GenericRepository<AuditLog>(context), IAuditLogRepository
{
    public async Task<IReadOnlyList<AuditLog>> GetPagedAsync(string? action, string? entityName,
        Guid? userId, DateTime? from, DateTime? to, int page, int pageSize)
    {
        IQueryable<AuditLog> query = DbSet.AsQueryable();
        if (!string.IsNullOrEmpty(action))
            query = query.Where(a => a.Action.Contains(action));
        if (!string.IsNullOrEmpty(entityName))
            query = query.Where(a => a.EntityName == entityName);
        if (userId.HasValue)
            query = query.Where(a => a.UserId == userId);
        if (from.HasValue)
            query = query.Where(a => a.CreatedAt >= from.Value);
        if (to.HasValue)
            query = query.Where(a => a.CreatedAt <= to.Value);
        return await query.OrderByDescending(a => a.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
    }

    public async Task<int> GetTotalCountAsync(string? action, string? entityName,
        Guid? userId, DateTime? from, DateTime? to)
    {
        IQueryable<AuditLog> query = DbSet.AsQueryable();
        if (!string.IsNullOrEmpty(action))
            query = query.Where(a => a.Action.Contains(action));
        if (!string.IsNullOrEmpty(entityName))
            query = query.Where(a => a.EntityName == entityName);
        if (userId.HasValue)
            query = query.Where(a => a.UserId == userId);
        if (from.HasValue)
            query = query.Where(a => a.CreatedAt >= from.Value);
        if (to.HasValue)
            query = query.Where(a => a.CreatedAt <= to.Value);
        return await query.CountAsync();
    }
}
