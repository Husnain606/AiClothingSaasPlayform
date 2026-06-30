using FashionSaaS.Domain.Entities;
using FashionSaaS.Application.SubscriptionPlans.DTOs;
using Mapster;

namespace FashionSaaS.Application.SubscriptionPlans.Mappings;

public class SubscriptionPlanMappings : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<SubscriptionPlan, SubscriptionPlanResponse>();
        config.NewConfig<CreateSubscriptionPlanRequest, SubscriptionPlan>()
            .Ignore(dest => dest.Id)
            .Ignore(dest => dest.CreatedAt)
            .Ignore(dest => dest.UpdatedAt)
            .Ignore(dest => dest.DomainEvents);
        config.NewConfig<UpdateSubscriptionPlanRequest, SubscriptionPlan>()
            .IgnoreNullValues(true);
    }
}
