namespace FashionSaaS.Domain.Events;

public record TenantCreatedEvent(Guid TenantId, string TenantName, string AdminEmail) : IDomainEvent;
