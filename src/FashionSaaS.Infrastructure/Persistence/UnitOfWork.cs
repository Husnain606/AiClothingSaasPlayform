using FashionSaaS.Application.Interfaces;
using FashionSaaS.Domain.Entities;
using FashionSaaS.Domain.Events;
using MediatR;

namespace FashionSaaS.Infrastructure.Persistence;

public sealed class UnitOfWork(ApplicationDbContext context, IPublisher publisher) : IUnitOfWork
{
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // Collect entities with pending domain events BEFORE saving
        var entitiesWithEvents = context.ChangeTracker.Entries<BaseEntity>()
            .Where(e => e.Entity.DomainEvents.Count > 0)
            .Select(e => e.Entity)
            .ToList();

        var result = await context.SaveChangesAsync(cancellationToken);

        // Dispatch domain events AFTER successful save
        foreach (BaseEntity? entity in entitiesWithEvents)
        {
            var events = entity.DomainEvents.ToList();
            entity.ClearDomainEvents();

            foreach (IDomainEvent? domainEvent in events)
            {
                // Build DomainEventNotification<TConcreteEventType> via reflection
                // so Domain stays MediatR-free
                Type notificationType = typeof(DomainEventNotification<>)
                    .MakeGenericType(domainEvent.GetType());
                var notification = Activator.CreateInstance(notificationType, domainEvent)
                    as INotification;

                if (notification is not null)
                    await publisher.Publish(notification, cancellationToken);
            }
        }

        return result;
    }

    public void Dispose()
    {
        context.Dispose();
        GC.SuppressFinalize(this);
    }
}
