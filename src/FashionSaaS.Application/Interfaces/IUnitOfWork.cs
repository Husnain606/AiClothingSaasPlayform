using MediatR;

namespace FashionSaaS.Application.Interfaces;

public interface IUnitOfWork : IDisposable
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    Task PublishDomainEventsAsync(IMediator mediator);
}
