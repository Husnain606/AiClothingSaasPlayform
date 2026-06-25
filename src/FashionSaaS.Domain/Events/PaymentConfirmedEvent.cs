namespace FashionSaaS.Domain.Events;

public record PaymentConfirmedEvent(Guid TenantId, string TenantEmail, decimal Amount) : IDomainEvent;
