namespace FashionSaaS.Domain.Events;

public record SubscriptionAssignedEvent(Guid TenantId, string TenantEmail, string PlanName, DateTime EndDate) : IDomainEvent;
