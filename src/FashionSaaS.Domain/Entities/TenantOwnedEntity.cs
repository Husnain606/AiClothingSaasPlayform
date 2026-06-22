namespace FashionSaaS.Domain.Entities;

public abstract class TenantOwnedEntity : BaseEntity
{
    public Guid TenantId { get; set; }
}
