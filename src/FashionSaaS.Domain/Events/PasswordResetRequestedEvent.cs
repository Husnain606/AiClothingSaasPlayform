namespace FashionSaaS.Domain.Events;

public record PasswordResetRequestedEvent(string Email, string ResetLink) : IDomainEvent;
