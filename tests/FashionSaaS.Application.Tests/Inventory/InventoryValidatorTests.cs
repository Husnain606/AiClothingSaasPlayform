using FashionSaaS.Application.Inventory.DTOs;
using FashionSaaS.Application.Inventory.Validators;
using FashionSaaS.Domain.Enums;
using FluentAssertions;

namespace FashionSaaS.Application.Tests.Inventory;

public class InventoryValidatorTests
{
    private readonly AdjustStockRequestValidator _validator = new();

    private static AdjustStockRequest Valid() => new()
    {
        VariantId = Guid.NewGuid(),
        Delta = 5,
        Reason = StockAdjustmentReason.Restock
    };

    [Fact]
    public void Valid_Passes() => _validator.Validate(Valid()).IsValid.Should().BeTrue();

    [Fact]
    public void EmptyVariantId_Fails()
    {
        AdjustStockRequest req = Valid();
        req.VariantId = Guid.Empty;
        _validator.Validate(req).Errors.Should().Contain(e => e.PropertyName == nameof(AdjustStockRequest.VariantId));
    }

    [Fact]
    public void ZeroDelta_Fails()
    {
        AdjustStockRequest req = Valid();
        req.Delta = 0;
        _validator.Validate(req).Errors.Should().Contain(e => e.PropertyName == nameof(AdjustStockRequest.Delta));
    }

    [Fact]
    public void UndefinedReason_Fails()
    {
        AdjustStockRequest req = Valid();
        req.Reason = (StockAdjustmentReason)999;
        _validator.Validate(req).Errors.Should().Contain(e => e.PropertyName == nameof(AdjustStockRequest.Reason));
    }

    [Fact]
    public void NegativeDelta_IsAllowed()
    {
        AdjustStockRequest req = Valid();
        req.Delta = -3;
        _validator.Validate(req).IsValid.Should().BeTrue();
    }
}
