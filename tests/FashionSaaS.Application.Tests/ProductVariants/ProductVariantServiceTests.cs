using FashionSaaS.Application.Interfaces;
using FashionSaaS.Application.ProductVariants;
using FashionSaaS.Application.ProductVariants.DTOs;
using FashionSaaS.Domain.Entities;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FashionSaaS.Application.Tests.ProductVariants;

public class ProductVariantServiceTests
{
    private readonly Mock<IProductVariantRepository> _variants = new();
    private readonly Mock<IProductRepository> _products = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IAuditLogService> _audit = new();
    private readonly Mock<ICurrentTenantService> _tenant = new();
    private readonly Guid _tenantId = Guid.NewGuid();

    public ProductVariantServiceTests()
    {
        _tenant.SetupGet(t => t.TenantId).Returns(_tenantId);
    }

    private ProductVariantService CreateService() => new(
        _variants.Object, _products.Object, _uow.Object, _audit.Object,
        _tenant.Object, NullLogger<ProductVariantService>.Instance);

    private Product Product(Guid id, decimal basePrice = 20m) => new()
    {
        Id = id, TenantId = _tenantId, CategoryId = Guid.NewGuid(), Name = "Tee", Slug = "tee", BasePrice = basePrice
    };

    private ProductVariant Variant(Guid productId, string size = "M", string color = "Red") => new()
    {
        Id = Guid.NewGuid(), TenantId = _tenantId, ProductId = productId, Size = size, Color = color, Sku = "SKU-1"
    };

    // ── Add ───────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AddAsync_NewSkuAndCombo_Succeeds_WithEffectivePrice()
    {
        var productId = Guid.NewGuid();
        _products.Setup(r => r.GetByIdAsync(productId)).ReturnsAsync(Product(productId, 20m));
        _variants.Setup(r => r.SkuExistsAsync(_tenantId, "SKU-NEW", null, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _variants.Setup(r => r.GetByProductAsync(productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductVariant>());

        var result = await CreateService().AddAsync(
            new AddVariantRequest { ProductId = productId, Size = "M", Color = "Red", Sku = "SKU-NEW", StockQuantity = 3 },
            Guid.NewGuid(), "127.0.0.1", "ua");

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(201);
        // No override → effective price falls back to product BasePrice.
        result.Data!.EffectivePrice.Should().Be(20m);
        _variants.Verify(r => r.AddAsync(It.IsAny<ProductVariant>()), Times.Once);
    }

    [Fact]
    public async Task AddAsync_PriceOverride_UsesOverrideAsEffectivePrice()
    {
        var productId = Guid.NewGuid();
        _products.Setup(r => r.GetByIdAsync(productId)).ReturnsAsync(Product(productId, 20m));
        _variants.Setup(r => r.SkuExistsAsync(_tenantId, "SKU-NEW", null, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _variants.Setup(r => r.GetByProductAsync(productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductVariant>());

        var result = await CreateService().AddAsync(
            new AddVariantRequest { ProductId = productId, Size = "M", Color = "Red", Sku = "SKU-NEW", PriceOverride = 9.99m },
            Guid.NewGuid(), "127.0.0.1", "ua");

        result.IsSuccess.Should().BeTrue();
        result.Data!.EffectivePrice.Should().Be(9.99m);
    }

    [Fact]
    public async Task AddAsync_ProductFromAnotherTenant_Returns404()
    {
        var productId = Guid.NewGuid();
        _products.Setup(r => r.GetByIdAsync(productId))
            .ReturnsAsync(new Product { Id = productId, TenantId = Guid.NewGuid() });

        var result = await CreateService().AddAsync(
            new AddVariantRequest { ProductId = productId, Size = "M", Color = "Red", Sku = "SKU" },
            Guid.NewGuid(), "127.0.0.1", "ua");

        result.StatusCode.Should().Be(404);
        _variants.Verify(r => r.AddAsync(It.IsAny<ProductVariant>()), Times.Never);
    }

    [Fact]
    public async Task AddAsync_DuplicateSku_Returns409()
    {
        var productId = Guid.NewGuid();
        _products.Setup(r => r.GetByIdAsync(productId)).ReturnsAsync(Product(productId));
        _variants.Setup(r => r.SkuExistsAsync(_tenantId, "SKU-DUP", null, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await CreateService().AddAsync(
            new AddVariantRequest { ProductId = productId, Size = "M", Color = "Red", Sku = "SKU-DUP" },
            Guid.NewGuid(), "127.0.0.1", "ua");

        result.StatusCode.Should().Be(409);
        _variants.Verify(r => r.AddAsync(It.IsAny<ProductVariant>()), Times.Never);
    }

    [Fact]
    public async Task AddAsync_DuplicateSizeColor_Returns409()
    {
        var productId = Guid.NewGuid();
        _products.Setup(r => r.GetByIdAsync(productId)).ReturnsAsync(Product(productId));
        _variants.Setup(r => r.SkuExistsAsync(_tenantId, "SKU-X", null, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _variants.Setup(r => r.GetByProductAsync(productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductVariant> { Variant(productId, "m", "red") }); // case-insensitive match

        var result = await CreateService().AddAsync(
            new AddVariantRequest { ProductId = productId, Size = "M", Color = "Red", Sku = "SKU-X" },
            Guid.NewGuid(), "127.0.0.1", "ua");

        result.StatusCode.Should().Be(409);
        _variants.Verify(r => r.AddAsync(It.IsAny<ProductVariant>()), Times.Never);
    }

    // ── Deactivate / Delete ───────────────────────────────────────────────────────

    [Fact]
    public async Task DeactivateAsync_SetsInactive()
    {
        var productId = Guid.NewGuid();
        var variant = Variant(productId);
        _variants.Setup(r => r.GetByIdAsync(variant.Id)).ReturnsAsync(variant);

        var result = await CreateService().DeactivateAsync(variant.Id, Guid.NewGuid(), "127.0.0.1", "ua");

        result.IsSuccess.Should().BeTrue();
        variant.IsActive.Should().BeFalse();
        _variants.Verify(r => r.UpdateAsync(It.IsAny<ProductVariant>()), Times.Once);
        _variants.Verify(r => r.DeleteAsync(It.IsAny<ProductVariant>()), Times.Never);
    }

    [Fact]
    public async Task DeleteAsync_RemovesVariant()
    {
        var productId = Guid.NewGuid();
        var variant = Variant(productId);
        _variants.Setup(r => r.GetByIdAsync(variant.Id)).ReturnsAsync(variant);

        var result = await CreateService().DeleteAsync(variant.Id, Guid.NewGuid(), "127.0.0.1", "ua");

        result.IsSuccess.Should().BeTrue();
        _variants.Verify(r => r.DeleteAsync(variant), Times.Once);
    }

    // ── GetByProduct ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetByProductAsync_ComputesEffectivePricePerVariant()
    {
        var productId = Guid.NewGuid();
        var v1 = Variant(productId, "S", "Red");
        var v2 = Variant(productId, "M", "Blue");
        v2.PriceOverride = 5m;
        _products.Setup(r => r.GetByIdAsync(productId)).ReturnsAsync(Product(productId, 20m));
        _variants.Setup(r => r.GetByProductAsync(productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductVariant> { v1, v2 });

        var result = await CreateService().GetByProductAsync(productId);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Single(r => r.Id == v1.Id).EffectivePrice.Should().Be(20m);
        result.Data!.Single(r => r.Id == v2.Id).EffectivePrice.Should().Be(5m);
    }
}
