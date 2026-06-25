namespace FashionSaaS.Domain.Events;

public record TenantActivatedEvent(Guid TenantId, string TenantEmail) : IDomainEvent;
