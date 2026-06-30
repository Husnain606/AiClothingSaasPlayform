using FashionSaaS.Domain.Entities;
using FashionSaaS.Application.AuditLogs.DTOs;
using Mapster;

namespace FashionSaaS.Application.AuditLogs.Mappings;

public class AuditLogMappings : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<AuditLog, AuditLogResponse>();
    }
}
