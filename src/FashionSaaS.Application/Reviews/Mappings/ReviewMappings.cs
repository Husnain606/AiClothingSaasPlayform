using FashionSaaS.Application.Reviews.DTOs;
using FashionSaaS.Domain.Entities;
using Mapster;

namespace FashionSaaS.Application.Reviews.Mappings;

public class ReviewMappings : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<Review, ReviewResponse>();
    }
}
