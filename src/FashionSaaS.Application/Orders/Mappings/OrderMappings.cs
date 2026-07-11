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
            // CA1308 suppressed: lowercase is the deliberate, documented API output shape for
            // OrderDto.Status (see Program.cs's JsonStringEnumConverter comment) — flipping to
            // ToUpperInvariant would change the JSON every storefront client already consumes.
#pragma warning disable CA1308
            .Map(d => d.Status, s => s.Status.ToString().ToLowerInvariant())
#pragma warning restore CA1308
            .Map(d => d.ShippingAddress, s => new ShippingAddressDto
            {
                FirstName = s.ShippingFirstName,
                LastName = s.ShippingLastName,
                Email = s.ShippingEmail,
                Phone = s.ShippingPhone,
                Street = s.ShippingStreet,
                City = s.ShippingCity,
                State = s.ShippingState,
                ZipCode = s.ShippingZipCode,
                Country = s.ShippingCountry
            });

        config.NewConfig<OrderItem, OrderItemDto>()
            .Map(d => d.Price, s => s.UnitPrice)
            .Map(d => d.Variant, s => BuildVariantDto(s));
    }

    private static OrderVariantDto? BuildVariantDto(OrderItem item)
    {
        if (item.ProductVariantId is null && string.IsNullOrEmpty(item.Size) && string.IsNullOrEmpty(item.Color))
        {
            return null;
        }

        return new OrderVariantDto
        {
            Size = string.IsNullOrEmpty(item.Size) ? null : item.Size,
            Color = string.IsNullOrEmpty(item.Color) ? null : item.Color
        };
    }
}
