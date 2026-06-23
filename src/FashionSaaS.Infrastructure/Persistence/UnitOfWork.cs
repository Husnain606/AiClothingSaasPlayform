using FashionSaaS.Application.Interfaces;
using FashionSaaS.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FashionSaaS.Infrastructure.Persistence;

public class UnitOfWork(ApplicationDbContext context) : IUnitOfWork
{
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => await context.SaveChangesAsync(cancellationToken);

    public async Task PublishDomainEventsAsync(IMediator mediator)
    {
        var entities = context.ChangeTracker.Entries<BaseEntity>()
            .Where(e => e.Entity.DomainEvents.Any())
            .Select(e => e.Entity)
            .ToList();

        var events = entities.SelectMany(e => e.DomainEvents).ToList();
        entities.ForEach(e => e.ClearDomainEvents());

        foreach (var domainEvent in events)
            await mediator.Publish(domainEvent);
    }

    public void Dispose() => context.Dispose();
}
