namespace FashionSaaS.Domain.Events;

public record SuperAdminLoginFromNewIpEvent(Guid UserId, string Email, string NewIpAddress, DateTime OccurredAt) : IDomainEvent;
