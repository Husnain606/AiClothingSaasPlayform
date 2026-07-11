using FashionSaaS.Application.Discounts.DTOs;
using FashionSaaS.Domain.Entities;
using Mapster;

namespace FashionSaaS.Application.Discounts.Mappings;

public class DiscountMappings : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<Discount, DiscountResponse>();
        config.NewConfig<CreateDiscountRequest, Discount>()
            .Ignore(dest => dest.Id)
            .Ignore(dest => dest.CreatedAt)
            .Ignore(dest => dest.UpdatedAt)
            .Ignore(dest => dest.DomainEvents)
            .Ignore(dest => dest.TenantId)
            .Ignore(dest => dest.RedemptionCount);
        config.NewConfig<UpdateDiscountRequest, Discount>()
            .IgnoreNullValues(true);
    }
}
