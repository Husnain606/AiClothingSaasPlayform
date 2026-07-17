namespace FashionSaaS.TryOn.Application.Messaging;

public record TryOnCompletedEvent(
    Guid TryOnRequestId,
    Guid TenantId,
    Guid CustomerId,
    Guid ProductId,
    DateTime CreatedAt);
