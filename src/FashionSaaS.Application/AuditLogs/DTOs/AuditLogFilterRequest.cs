namespace FashionSaaS.Application.AuditLogs.DTOs;

public class AuditLogFilterRequest
{
    public string? Action { get; set; }
    public string? EntityName { get; set; }
    public Guid? UserId { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}
