namespace FashionSaaS.Domain.Events;

public record PaymentReminderEvent(Guid TenantId, string TenantEmail, decimal Amount, DateTime DueDate) : IDomainEvent;
