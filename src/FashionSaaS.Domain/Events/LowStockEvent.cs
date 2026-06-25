namespace FashionSaaS.Domain.Events;

public record LowStockEvent(Guid ProductVariantId, Guid TenantId, int Remaining) : IDomainEvent;
