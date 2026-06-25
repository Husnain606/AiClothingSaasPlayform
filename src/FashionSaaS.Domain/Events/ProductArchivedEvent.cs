namespace FashionSaaS.Domain.Events;

public record ProductArchivedEvent(Guid ProductId, Guid TenantId) : IDomainEvent;
