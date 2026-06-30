using FashionSaaS.Domain.Entities;
using FashionSaaS.Application.Products.DTOs;
using Mapster;

namespace FashionSaaS.Application.Products.Mappings;

public class ProductMappings : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<Product, ProductResponse>()
            .Map(dest => dest.CategoryName, src => src.Category != null ? src.Category.Name : null)
            .Map(dest => dest.VariantCount, src => src.Variants.Count)
            .Map(dest => dest.ApprovedReviewCount, src => src.Reviews.Count(r => r.Status.ToString() == "Approved"));
        config.NewConfig<Product, ProductSummaryResponse>();
        config.NewConfig<CreateProductRequest, Product>()
            .Ignore(dest => dest.Id)
            .Ignore(dest => dest.CreatedAt)
            .Ignore(dest => dest.UpdatedAt)
            .Ignore(dest => dest.DomainEvents)
            .Ignore(dest => dest.TenantId);
        config.NewConfig<UpdateProductRequest, Product>()
            .IgnoreNullValues(true);
    }
}
