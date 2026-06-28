using FashionSaaS.Application.ProductVariants.DTOs;
using FashionSaaS.Application.ProductVariants.Validators;
using FluentAssertions;

namespace FashionSaaS.Application.Tests.ProductVariants;

public class ProductVariantValidatorTests
{
    private readonly AddVariantRequestValidator _add = new();
    private readonly UpdateVariantRequestValidator _update = new();

    private static AddVariantRequest ValidAdd() => new()
    {
        ProductId = Guid.NewGuid(), Size = "M", Color = "Red", Sku = "SKU-1", StockQuantity = 1
    };

    [Fact]
    public void Add_Valid_Passes() => _add.Validate(ValidAdd()).IsValid.Should().BeTrue();

    [Fact]
    public void Add_BlankSize_Fails()
    {
        var req = ValidAdd();
        req.Size = "";
        _add.Validate(req).Errors.Should().Contain(e => e.PropertyName == nameof(AddVariantRequest.Size));
    }

    [Fact]
    public void Add_BlankSku_Fails()
    {
        var req = ValidAdd();
        req.Sku = "";
        _add.Validate(req).Errors.Should().Contain(e => e.PropertyName == nameof(AddVariantRequest.Sku));
    }

    [Fact]
    public void Add_NegativeStock_Fails()
    {
        var req = ValidAdd();
        req.StockQuantity = -1;
        _add.Validate(req).Errors.Should().Contain(e => e.PropertyName == nameof(AddVariantRequest.StockQuantity));
    }

    [Fact]
    public void Add_NegativePriceOverride_Fails()
    {
        var req = ValidAdd();
        req.PriceOverride = -0.01m;
        _add.Validate(req).Errors.Should().Contain(e => e.PropertyName == nameof(AddVariantRequest.PriceOverride));
    }

    [Fact]
    public void Add_NullPriceOverride_Passes()
    {
        var req = ValidAdd();
        req.PriceOverride = null;
        _add.Validate(req).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Update_BlankColor_Fails()
    {
        var result = _update.Validate(new UpdateVariantRequest { Size = "M", Color = "", Sku = "SKU-1" });
        result.Errors.Should().Contain(e => e.PropertyName == nameof(UpdateVariantRequest.Color));
    }

    [Fact]
    public void Update_Valid_Passes()
    {
        _update.Validate(new UpdateVariantRequest { Size = "M", Color = "Red", Sku = "SKU-1" })
            .IsValid.Should().BeTrue();
    }

    [Fact]
    public void Update_StockQuantity_NotAccepted_OnUpdateRequest()
    {
        // UpdateVariantRequest must not carry StockQuantity — stock changes go through the ledger only.
        var props = typeof(UpdateVariantRequest).GetProperties().Select(p => p.Name);
        props.Should().NotContain(nameof(AddVariantRequest.StockQuantity),
            "stock must only change via InventoryService.AdjustStock (ledger-only)");
    }
}
