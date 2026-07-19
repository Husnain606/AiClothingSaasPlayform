namespace FashionSaaS.Domain.Events;

public record OrderPlacedEvent(Guid OrderId, Guid TenantId, string OrderNumber, decimal Total) : IDomainEvent;
