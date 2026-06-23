namespace FashionSaaS.Domain.Events;

public record TenantSuspendedEvent(Guid TenantId, string TenantEmail) : IDomainEvent;
