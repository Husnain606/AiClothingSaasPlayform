namespace FashionSaaS.Domain.Events;

public record BankAccountChangedEvent(Guid BankAccountId, Guid? TenantId, string AdminEmail, string Action) : IDomainEvent;
