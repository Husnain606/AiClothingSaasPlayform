using FashionSaaS.Application.Interfaces;

namespace FashionSaaS.Infrastructure.Services;

public class CurrentTenantService : ICurrentTenantService
{
    public Guid? TenantId { get; private set; }
    public string? TenantSlug { get; private set; }
    public bool IsResolved => TenantId.HasValue;

    public void SetTenant(Guid tenantId, string slug)
    {
        TenantId = tenantId;
        TenantSlug = slug;
    }
}
