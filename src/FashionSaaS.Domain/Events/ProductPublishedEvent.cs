namespace FashionSaaS.Domain.Events;

public record ProductPublishedEvent(Guid ProductId, Guid TenantId) : IDomainEvent;
