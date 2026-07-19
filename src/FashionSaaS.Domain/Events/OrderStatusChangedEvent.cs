using FashionSaaS.Domain.Enums;

namespace FashionSaaS.Domain.Events;

public record OrderStatusChangedEvent(
    Guid OrderId, Guid TenantId, Guid CustomerId, string OrderNumber,
    OrderStatus PreviousStatus, OrderStatus NewStatus) : IDomainEvent;
