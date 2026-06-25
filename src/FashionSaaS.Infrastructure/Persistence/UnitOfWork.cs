using FashionSaaS.Application.Interfaces;
using FashionSaaS.Domain.Entities;
using MediatR;

namespace FashionSaaS.Infrastructure.Persistence;

public class UnitOfWork(ApplicationDbContext context, IPublisher publisher) : IUnitOfWork
{
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // Collect entities with pending domain events BEFORE saving
        var entitiesWithEvents = context.ChangeTracker.Entries<BaseEntity>()
            .Where(e => e.Entity.DomainEvents.Any())
            .Select(e => e.Entity)
            .ToList();

        var result = await context.SaveChangesAsync(cancellationToken);

        // Dispatch domain events AFTER successful save
        foreach (var entity in entitiesWithEvents)
        {
            var events = entity.DomainEvents.ToList();
            entity.ClearDomainEvents();

            foreach (var domainEvent in events)
            {
                // Build DomainEventNotification<TConcreteEventType> via reflection
                // so Domain stays MediatR-free
                var notificationType = typeof(DomainEventNotification<>)
                    .MakeGenericType(domainEvent.GetType());
                var notification = Activator.CreateInstance(notificationType, domainEvent)
                    as INotification;

                if (notification is not null)
                    await publisher.Publish(notification, cancellationToken);
            }
        }

        return result;
    }

    public void Dispose() => context.Dispose();
}
