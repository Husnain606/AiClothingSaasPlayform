using FashionSaaS.Application.ProductVariants.DTOs;
using FashionSaaS.Domain.Entities;
using Mapster;

namespace FashionSaaS.Application.ProductVariants.Mappings;

public class ProductVariantMappings : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<ProductVariant, VariantResponse>()
            .Map(dest => dest.EffectivePrice, src => src.PriceOverride ?? (src.Product != null ? src.Product.BasePrice : 0m));
        config.NewConfig<AddVariantRequest, ProductVariant>()
            .Ignore(dest => dest.Id)
            .Ignore(dest => dest.CreatedAt)
            .Ignore(dest => dest.UpdatedAt)
            .Ignore(dest => dest.DomainEvents)
            .Ignore(dest => dest.TenantId);
        config.NewConfig<UpdateVariantRequest, ProductVariant>()
            .IgnoreNullValues(true);
    }
}
