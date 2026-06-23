namespace FashionSaaS.Domain.Events;

public record SubscriptionExpiredEvent(Guid TenantId, string TenantEmail) : IDomainEvent;
