using FashionSaaS.Application.AuditLogs.DTOs;
using FashionSaaS.Application.Common;
using FashionSaaS.Application.Interfaces;
using FashionSaaS.Domain.Entities;

namespace FashionSaaS.Application.AuditLogs;

public class AuditLogQueryService(IAuditLogRepository auditLogRepository)
{
    public async Task<ResponseData<PagedResult<AuditLogResponse>>> GetPagedAsync(AuditLogFilterRequest filter)
    {
        IReadOnlyList<AuditLog> items = await auditLogRepository.GetPagedAsync(
            filter.Action, filter.EntityName, filter.UserId, filter.From, filter.To,
            filter.Page, filter.PageSize);
        var total = await auditLogRepository.GetTotalCountAsync(
            filter.Action, filter.EntityName, filter.UserId, filter.From, filter.To);

        var paged = new PagedResult<AuditLogResponse>
        {
            Items = items.Select(Map).ToList(),
            TotalCount = total,
            Page = filter.Page,
            PageSize = filter.PageSize
        };
        return ResponseData<PagedResult<AuditLogResponse>>.Success(paged);
    }

    public async Task<ResponseData<AuditLogResponse>> GetByIdAsync(Guid id)
    {
        AuditLog? log = await auditLogRepository.GetByIdAsync(id);
        if (log is null)
            return ResponseData<AuditLogResponse>.Failure("Audit log not found.", 404);
        return ResponseData<AuditLogResponse>.Success(Map(log));
    }

    private static AuditLogResponse Map(AuditLog a) => new()
    {
        Id = a.Id,
        UserId = a.UserId,
        TenantId = a.TenantId,
        Action = a.Action,
        EntityName = a.EntityName,
        EntityId = a.EntityId,
        OldValues = a.OldValues,
        NewValues = a.NewValues,
        IpAddress = a.IpAddress,
        CreatedAt = a.CreatedAt
    };
}
