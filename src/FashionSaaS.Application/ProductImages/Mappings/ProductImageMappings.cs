using FashionSaaS.Application.ProductImages.DTOs;
using FashionSaaS.Domain.Entities;
using Mapster;

namespace FashionSaaS.Application.ProductImages.Mappings;

public class ProductImageMappings : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<ProductImage, ProductImageResponse>();
    }
}
