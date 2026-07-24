using FashionSaaS.API.Controllers.Public;
using FashionSaaS.Application.Common;
using FashionSaaS.Application.Interfaces;
using FashionSaaS.Application.ProductVariants;
using FashionSaaS.Application.ProductVariants.DTOs;
using FashionSaaS.Application.Products;
using FashionSaaS.Application.Products.DTOs;
using FashionSaaS.Domain.Entities;
using FashionSaaS.Domain.Enums;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FashionSaaS.API.Tests.Controllers.Public;

/// <summary>
/// Verifies the public storefront product endpoints never expose a Draft/Archived
/// product, and that the list endpoint's filter cannot be steered onto a status other
/// than Active by any caller-supplied query value (PublicProductFilter has no Status
/// property to bind onto). ProductService itself is exercised through its real Moq
/// setup (same style as ProductServiceTests) rather than re-implemented.
/// </summary>
public class PublicProductsControllerTests
{
    private readonly Mock<IProductRepository> _products = new();
    private readonly Mock<ICategoryRepository> _categories = new();
    private readonly Mock<IProductVariantRepository> _variants = new();
    private readonly Mock<IProductImageRepository> _images = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IAuditLogService> _audit = new();
    private readonly Mock<ICurrentTenantService> _tenant = new();
    private readonly Guid _tenantId = Guid.NewGuid();

    public PublicProductsControllerTests()
    {
        _tenant.SetupGet(t => t.TenantId).Returns(_tenantId);
    }

    private ProductService CreateService() => new(
        _products.Object, _categories.Object, _variants.Object, _images.Object,
        _uow.Object, _audit.Object, _tenant.Object, NullLogger<ProductService>.Instance);

    private ProductVariantService CreateVariantService() => new(
        _variants.Object, _products.Object, _uow.Object, _audit.Object, _tenant.Object,
        NullLogger<ProductVariantService>.Instance);

    private PublicProductsController CreateController() => new(CreateService(), CreateVariantService());

    private Product Product(ProductStatus status) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = _tenantId,
        CategoryId = Guid.NewGuid(),
        Name = "Tee",
        Slug = "tee",
        BasePrice = 10m,
        Status = status
    };

    [Fact]
    public async Task GetAll_AlwaysQueriesActiveOnly_RegardlessOfRepositoryContent()
    {
        PublicProductsController controller = CreateController();
        ProductFilter? capturedFilter = null;
        _products
            .Setup(r => r.GetPagedAsync(It.IsAny<ProductFilter>(), It.IsAny<CancellationToken>()))
            .Callback<ProductFilter, CancellationToken>((f, _) => capturedFilter = f)
            .ReturnsAsync((new List<Product>(), 0));

        await controller.GetAll(new PublicProductFilter { Search = "shirt", Page = 2, PageSize = 5 }, CancellationToken.None);

        capturedFilter.Should().NotBeNull();
        capturedFilter!.Status.Should().Be(ProductStatus.Active);
        capturedFilter.TenantId.Should().Be(_tenantId);
        capturedFilter.Search.Should().Be("shirt");
    }

    [Fact]
    public async Task GetById_DraftProductInSameTenant_Returns404NotTheProduct()
    {
        Product draft = Product(ProductStatus.Draft);
        _products.Setup(r => r.GetByIdWithDetailsAsync(draft.Id, It.IsAny<CancellationToken>())).ReturnsAsync(draft);
        PublicProductsController controller = CreateController();

        var result = await controller.GetById(draft.Id, CancellationToken.None) as ObjectResult;

        result.Should().NotBeNull();
        result!.StatusCode.Should().Be(404);
        var body = result.Value as ResponseData<string>;
        body.Should().NotBeNull();
        body!.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task GetById_ArchivedProductInSameTenant_Returns404()
    {
        Product archived = Product(ProductStatus.Archived);
        _products.Setup(r => r.GetByIdWithDetailsAsync(archived.Id, It.IsAny<CancellationToken>())).ReturnsAsync(archived);
        PublicProductsController controller = CreateController();

        var result = await controller.GetById(archived.Id, CancellationToken.None) as ObjectResult;

        result!.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task GetById_ActiveProductInSameTenant_Returns200WithData()
    {
        Product active = Product(ProductStatus.Active);
        _products.Setup(r => r.GetByIdWithDetailsAsync(active.Id, It.IsAny<CancellationToken>())).ReturnsAsync(active);
        PublicProductsController controller = CreateController();

        var result = await controller.GetById(active.Id, CancellationToken.None) as ObjectResult;

        result.Should().NotBeNull();
        result!.StatusCode.Should().Be(200);
        var body = result.Value as ResponseData<ProductResponse>;
        body!.IsSuccess.Should().BeTrue();
        body.Data!.Id.Should().Be(active.Id);
    }

    [Fact]
    public async Task GetVariants_DraftProduct_Returns404()
    {
        Product draft = Product(ProductStatus.Draft);
        _products.Setup(r => r.GetByIdWithDetailsAsync(draft.Id, It.IsAny<CancellationToken>())).ReturnsAsync(draft);
        _products.Setup(r => r.GetByIdAsync(draft.Id)).ReturnsAsync(draft);
        PublicProductsController controller = CreateController();

        var result = await controller.GetVariants(draft.Id, CancellationToken.None) as ObjectResult;

        result.Should().NotBeNull();
        result!.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task GetVariants_ActiveProduct_ReturnsOnlyActiveVariants()
    {
        Product active = Product(ProductStatus.Active);
        _products.Setup(r => r.GetByIdWithDetailsAsync(active.Id, It.IsAny<CancellationToken>())).ReturnsAsync(active);
        _products.Setup(r => r.GetByIdAsync(active.Id)).ReturnsAsync(active);
        var activeVariant = new ProductVariant { Id = Guid.NewGuid(), TenantId = _tenantId, ProductId = active.Id, Size = "M", Color = "Red", Sku = "SKU-1", IsActive = true };
        var inactiveVariant = new ProductVariant { Id = Guid.NewGuid(), TenantId = _tenantId, ProductId = active.Id, Size = "L", Color = "Blue", Sku = "SKU-2", IsActive = false };
        _variants.Setup(r => r.GetByProductAsync(active.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductVariant> { activeVariant, inactiveVariant });
        PublicProductsController controller = CreateController();

        var result = await controller.GetVariants(active.Id, CancellationToken.None) as ObjectResult;

        result.Should().NotBeNull();
        result!.StatusCode.Should().Be(200);
        var body = result.Value as ResponseData<IReadOnlyList<VariantResponse>>;
        body!.IsSuccess.Should().BeTrue();
        body.Data.Should().ContainSingle(v => v.Id == activeVariant.Id);
        body.Data.Should().NotContain(v => v.Id == inactiveVariant.Id);
    }
}
