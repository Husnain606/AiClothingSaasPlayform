namespace FashionSaaS.Domain.Events;

public record ReviewSubmittedEvent(Guid ReviewId, Guid TenantId, Guid ProductId, int Rating) : IDomainEvent;
