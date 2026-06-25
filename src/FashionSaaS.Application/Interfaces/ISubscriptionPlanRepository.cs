using FashionSaaS.Domain.Entities;

namespace FashionSaaS.Application.Interfaces;

public interface ISubscriptionPlanRepository : IGenericRepository<SubscriptionPlan>
{
    Task<IReadOnlyList<SubscriptionPlan>> GetActiveAsync();
}
