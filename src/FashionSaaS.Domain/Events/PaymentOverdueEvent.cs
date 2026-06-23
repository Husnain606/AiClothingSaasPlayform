namespace FashionSaaS.Domain.Events;

public record PaymentOverdueEvent(Guid TenantId, string TenantEmail, decimal Amount, DateTime DueDate) : IDomainEvent;
