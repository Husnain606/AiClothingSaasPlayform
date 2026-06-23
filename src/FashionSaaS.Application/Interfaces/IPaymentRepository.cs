using FashionSaaS.Domain.Entities;

namespace FashionSaaS.Application.Interfaces;

public interface IPaymentRepository : IGenericRepository<SubscriptionPayment>
{
    Task<IReadOnlyList<SubscriptionPayment>> GetPendingOverdueAsync(DateTime asOf);
    Task<IReadOnlyList<SubscriptionPayment>> GetDueSoonAsync(DateTime targetDate);
    Task<IReadOnlyList<SubscriptionPayment>> GetBySubscriptionAsync(Guid subscriptionId);
}
