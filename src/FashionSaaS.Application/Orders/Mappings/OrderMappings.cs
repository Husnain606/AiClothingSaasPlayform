using FashionSaaS.Application.Orders.DTOs;
using FashionSaaS.Domain.Entities;
using Mapster;

namespace FashionSaaS.Application.Orders.Mappings;

public class OrderMappings : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<Order, OrderDto>()
            .Map(d => d.OrderId, s => s.OrderNumber)
            .Map(d => d.Id, s => s.Id)
            .Map(d => d.Status, s => s.Status.ToString().ToLowerInvariant())
            .Map(d => d.ShippingAddress, s => new ShippingAddressDto
            {
                FirstName = s.ShippingFirstName, LastName = s.ShippingLastName,
                Email = s.ShippingEmail, Phone = s.ShippingPhone, Street = s.ShippingStreet,
                City = s.ShippingCity, State = s.ShippingState,
                ZipCode = s.ShippingZipCode, Country = s.ShippingCountry
            });

        config.NewConfig<OrderItem, OrderItemDto>()
            .Map(d => d.Price, s => s.UnitPrice)
            .Map(d => d.Variant, s => (s.ProductVariantId == null && s.Size == "" && s.Color == "")
                ? null
                : new OrderVariantDto { Size = s.Size == "" ? null : s.Size, Color = s.Color == "" ? null : s.Color });
    }
}
