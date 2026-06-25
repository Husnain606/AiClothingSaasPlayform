using FashionSaaS.Domain.Enums;

namespace FashionSaaS.Domain.Events;

public record ReviewModeratedEvent(Guid ReviewId, Guid TenantId, ReviewStatus Status) : IDomainEvent;
