using FashionSaaS.Application.Inventory.DTOs;
using FashionSaaS.Domain.Entities;
using Mapster;

namespace FashionSaaS.Application.Inventory.Mappings;

public class InventoryMappings : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<StockAdjustment, StockAdjustmentResponse>();
        config.NewConfig<AdjustStockRequest, StockAdjustment>()
            .Ignore(dest => dest.Id)
            .Ignore(dest => dest.CreatedAt)
            .Ignore(dest => dest.UpdatedAt)
            .Ignore(dest => dest.DomainEvents)
            .Ignore(dest => dest.TenantId)
            .Ignore(dest => dest.ResultingQuantity)
            .Ignore(dest => dest.AdjustedByUserId)
            .Map(dest => dest.ProductVariantId, src => src.VariantId);
    }
}
