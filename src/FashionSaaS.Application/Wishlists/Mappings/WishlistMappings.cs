using FashionSaaS.Domain.Entities;
using FashionSaaS.Application.Wishlists.DTOs;
using Mapster;

namespace FashionSaaS.Application.Wishlists.Mappings;

public class WishlistMappings : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<Wishlist, WishlistResponse>()
            .Map(dest => dest.Items, src => src.Items.AsQueryable().ProjectToType<WishlistItemResponse>());
        config.NewConfig<WishlistItem, WishlistItemResponse>();
    }
}
