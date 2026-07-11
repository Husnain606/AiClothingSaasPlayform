using FashionSaaS.Application.Subscriptions.DTOs;
using FashionSaaS.Domain.Entities;
using Mapster;

namespace FashionSaaS.Application.Subscriptions.Mappings;

public class SubscriptionMappings : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<TenantSubscription, SubscriptionResponse>()
            .Map(dest => dest.PlanName, src => src.Plan.Name)
            .Map(dest => dest.Price, src => src.Plan.Price);
        config.NewConfig<SubscriptionPayment, PaymentResponse>();
        config.NewConfig<AssignSubscriptionRequest, TenantSubscription>()
            .Ignore(dest => dest.Id)
            .Ignore(dest => dest.CreatedAt)
            .Ignore(dest => dest.UpdatedAt)
            .Ignore(dest => dest.DomainEvents)
            .Ignore(dest => dest.Plan);
        config.NewConfig<ChangePlanRequest, TenantSubscription>()
            .Map(dest => dest.PlanId, src => src.NewPlanId);
    }
}
