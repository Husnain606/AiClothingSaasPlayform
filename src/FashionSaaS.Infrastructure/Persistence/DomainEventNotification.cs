using FashionSaaS.Domain.Events;
using MediatR;

namespace FashionSaaS.Infrastructure.Persistence;

/// <summary>
/// MediatR notification wrapper for domain events.
/// Keeps Domain layer free of MediatR; Infrastructure bridges the two.
/// </summary>
public record DomainEventNotification<TDomainEvent>(TDomainEvent DomainEvent)
    : INotification
    where TDomainEvent : IDomainEvent;
