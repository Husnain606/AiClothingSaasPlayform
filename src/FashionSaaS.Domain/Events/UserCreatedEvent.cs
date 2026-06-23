namespace FashionSaaS.Domain.Events;

public record UserCreatedEvent(Guid UserId, string Email, string TemporaryPassword, Guid? TenantId) : IDomainEvent;
