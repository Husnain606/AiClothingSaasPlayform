using FashionSaaS.Application.Common;
using FashionSaaS.Application.Interfaces;
using FashionSaaS.Application.Products;
using FashionSaaS.Application.Products.DTOs;
using FashionSaaS.Domain.Entities;
using FashionSaaS.Domain.Enums;
using FashionSaaS.Domain.Events;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FashionSaaS.Application.Tests.Products;

public class ProductServiceTests
{
    private readonly Mock<IProductRepository> _products = new();
    private readonly Mock<ICategoryRepository> _categories = new();
    private readonly Mock<IProductVariantRepository> _variants = new();
    private readonly Mock<IProductImageRepository> _images = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IAuditLogService> _audit = new();
    private readonly Mock<ICurrentTenantService> _tenant = new();
    private readonly Guid _tenantId = Guid.NewGuid();

    public ProductServiceTests()
    {
        _tenant.SetupGet(t => t.TenantId).Returns(_tenantId);
    }

    private ProductService CreateService() => new(
        _products.Object, _categories.Object, _variants.Object, _images.Object,
        _uow.Object, _audit.Object, _tenant.Object, NullLogger<ProductService>.Instance);

    private Category Category(Guid id) => new() { Id = id, TenantId = _tenantId, Name = "Tops", Slug = "tops" };

    private Product Product(Guid id, Guid categoryId, ProductStatus status = ProductStatus.Draft) => new()
    {
        Id = id,
        TenantId = _tenantId,
        CategoryId = categoryId,
        Name = "Tee",
        Slug = "tee",
        BasePrice = 10m,
        Status = status
    };

    private ProductVariant Variant(Guid productId, bool active = true) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = _tenantId,
        ProductId = productId,
        Sku = "SKU",
        IsActive = active
    };

    private ProductImage Image(Guid productId) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = _tenantId,
        ProductId = productId,
        Url = "https://img/x",
        IsPrimary = true
    };

    // ── Create ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_NewSlugValidCategory_ReturnsCreatedDraft()
    {
        var catId = Guid.NewGuid();
        _products.Setup(r => r.SlugExistsAsync(_tenantId, "tee", null, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _categories.Setup(r => r.GetByIdAsync(catId)).ReturnsAsync(Category(catId));

        ResponseData<ProductResponse> result = await CreateService().CreateAsync(
            new CreateProductRequest { Name = "Tee", Slug = "tee", CategoryId = catId, BasePrice = 10m },
            Guid.NewGuid(), "127.0.0.1", "ua");

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(201);
        result.Data!.Status.Should().Be(ProductStatus.Draft);
        _products.Verify(r => r.AddAsync(It.Is<Product>(p => p.Status == ProductStatus.Draft)), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_DuplicateSlug_Returns409()
    {
        var catId = Guid.NewGuid();
        _products.Setup(r => r.SlugExistsAsync(_tenantId, "tee", null, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        ResponseData<ProductResponse> result = await CreateService().CreateAsync(
            new CreateProductRequest { Name = "Tee", Slug = "tee", CategoryId = catId, BasePrice = 10m },
            Guid.NewGuid(), "127.0.0.1", "ua");

        result.StatusCode.Should().Be(409);
        _products.Verify(r => r.AddAsync(It.IsAny<Product>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_CategoryFromAnotherTenant_Returns404()
    {
        var catId = Guid.NewGuid();
        _products.Setup(r => r.SlugExistsAsync(_tenantId, "tee", null, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _categories.Setup(r => r.GetByIdAsync(catId))
            .ReturnsAsync(new Category { Id = catId, TenantId = Guid.NewGuid() });

        ResponseData<ProductResponse> result = await CreateService().CreateAsync(
            new CreateProductRequest { Name = "Tee", Slug = "tee", CategoryId = catId, BasePrice = 10m },
            Guid.NewGuid(), "127.0.0.1", "ua");

        result.StatusCode.Should().Be(404);
    }

    // ── Update ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_SlugConflictExcludingSelf_Returns409()
    {
        var id = Guid.NewGuid();
        var catId = Guid.NewGuid();
        _products.Setup(r => r.GetByIdAsync(id)).ReturnsAsync(Product(id, catId));
        _products.Setup(r => r.SlugExistsAsync(_tenantId, "taken", id, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        ResponseData<ProductResponse> result = await CreateService().UpdateAsync(id,
            new UpdateProductRequest { Name = "X", Slug = "taken", CategoryId = catId, BasePrice = 1m },
            Guid.NewGuid(), "127.0.0.1", "ua");

        result.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task UpdateAsync_SameCategory_ReturnsResponseWithReloadedCounts()
    {
        var id = Guid.NewGuid();
        var catId = Guid.NewGuid();
        Product stored = Product(id, catId);
        Product detailedProduct = Product(id, catId);
        detailedProduct.Variants.Add(Variant(id));
        detailedProduct.Images.Add(Image(id));

        _products.Setup(r => r.GetByIdAsync(id)).ReturnsAsync(stored);
        _products.Setup(r => r.SlugExistsAsync(_tenantId, "tee", id, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _categories.Setup(r => r.GetByIdAsync(catId)).ReturnsAsync(Category(catId));
        // Re-fetch after save returns entity with loaded collections
        _products.Setup(r => r.GetByIdWithDetailsAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(detailedProduct);

        ResponseData<ProductResponse> result = await CreateService().UpdateAsync(id,
            new UpdateProductRequest { Name = "Tee", Slug = "tee", CategoryId = catId, BasePrice = 10m },
            Guid.NewGuid(), "127.0.0.1", "ua");

        result.IsSuccess.Should().BeTrue();
        result.Data!.VariantCount.Should().Be(1);
        result.Data.PrimaryImageUrl.Should().NotBeNullOrEmpty();
        _products.Verify(r => r.GetByIdWithDetailsAsync(id, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── GetBySlug ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetBySlugAsync_ExistingSlug_ReturnsDetailedResponse()
    {
        var id = Guid.NewGuid();
        var catId = Guid.NewGuid();
        Product product = Product(id, catId);
        product.Variants.Add(Variant(id));
        product.Images.Add(Image(id));

        _products.Setup(r => r.GetBySlugWithDetailsAsync(_tenantId, "tee", It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);

        ResponseData<ProductResponse> result = await CreateService().GetBySlugAsync("tee");

        result.IsSuccess.Should().BeTrue();
        result.Data!.Slug.Should().Be("tee");
        result.Data.VariantCount.Should().Be(1);
        result.Data.PrimaryImageUrl.Should().NotBeNullOrEmpty();
        // Must NOT fall back to full-table-scan GetPagedAsync
        _products.Verify(r => r.GetPagedAsync(It.IsAny<ProductFilter>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetBySlugAsync_UnknownSlug_Returns404()
    {
        _products.Setup(r => r.GetBySlugWithDetailsAsync(_tenantId, "nope", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Product?)null);

        ResponseData<ProductResponse> result = await CreateService().GetBySlugAsync("nope");

        result.StatusCode.Should().Be(404);
    }

    // ── Publish gating ──────────────────────────────────────────────────────────

    [Fact]
    public async Task PublishAsync_NoActiveVariant_Returns400()
    {
        var id = Guid.NewGuid();
        var catId = Guid.NewGuid();
        _products.Setup(r => r.GetByIdAsync(id)).ReturnsAsync(Product(id, catId));
        _categories.Setup(r => r.GetByIdAsync(catId)).ReturnsAsync(Category(catId));
        _variants.Setup(r => r.GetByProductAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductVariant> { Variant(id, active: false) });

        ResponseData<ProductResponse> result = await CreateService().PublishAsync(id, Guid.NewGuid(), "127.0.0.1", "ua");

        result.StatusCode.Should().Be(400);
        result.Message.Should().Contain("variant");
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PublishAsync_NoImage_Returns400()
    {
        var id = Guid.NewGuid();
        var catId = Guid.NewGuid();
        _products.Setup(r => r.GetByIdAsync(id)).ReturnsAsync(Product(id, catId));
        _categories.Setup(r => r.GetByIdAsync(catId)).ReturnsAsync(Category(catId));
        _variants.Setup(r => r.GetByProductAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductVariant> { Variant(id) });
        _images.Setup(r => r.GetByProductAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductImage>());

        ResponseData<ProductResponse> result = await CreateService().PublishAsync(id, Guid.NewGuid(), "127.0.0.1", "ua");

        result.StatusCode.Should().Be(400);
        result.Message.Should().Contain("image");
    }

    [Fact]
    public async Task PublishAsync_AllGatesMet_SetsActiveAndRaisesEvent()
    {
        var id = Guid.NewGuid();
        var catId = Guid.NewGuid();
        Product product = Product(id, catId);
        _products.Setup(r => r.GetByIdAsync(id)).ReturnsAsync(product);
        _categories.Setup(r => r.GetByIdAsync(catId)).ReturnsAsync(Category(catId));
        _variants.Setup(r => r.GetByProductAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductVariant> { Variant(id) });
        _images.Setup(r => r.GetByProductAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductImage> { Image(id) });

        ResponseData<ProductResponse> result = await CreateService().PublishAsync(id, Guid.NewGuid(), "127.0.0.1", "ua");

        result.IsSuccess.Should().BeTrue();
        product.Status.Should().Be(ProductStatus.Active);
        product.DomainEvents.Should().ContainSingle(e => e is ProductPublishedEvent);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Archive ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ArchiveAsync_SetsArchivedAndRaisesEvent()
    {
        var id = Guid.NewGuid();
        var catId = Guid.NewGuid();
        Product product = Product(id, catId, ProductStatus.Active);
        _products.Setup(r => r.GetByIdAsync(id)).ReturnsAsync(product);
        _categories.Setup(r => r.GetByIdAsync(catId)).ReturnsAsync(Category(catId));

        ResponseData<ProductResponse> result = await CreateService().ArchiveAsync(id, Guid.NewGuid(), "127.0.0.1", "ua");

        result.IsSuccess.Should().BeTrue();
        product.Status.Should().Be(ProductStatus.Archived);
        product.DomainEvents.Should().ContainSingle(e => e is ProductArchivedEvent);
    }

    // ── Delete ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_DraftProduct_Succeeds()
    {
        var id = Guid.NewGuid();
        _products.Setup(r => r.GetByIdAsync(id)).ReturnsAsync(Product(id, Guid.NewGuid(), ProductStatus.Draft));

        ResponseData<bool> result = await CreateService().DeleteAsync(id, Guid.NewGuid(), "127.0.0.1", "ua");

        result.IsSuccess.Should().BeTrue();
        _products.Verify(r => r.DeleteAsync(It.IsAny<Product>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_ActiveProduct_Returns409()
    {
        var id = Guid.NewGuid();
        _products.Setup(r => r.GetByIdAsync(id)).ReturnsAsync(Product(id, Guid.NewGuid(), ProductStatus.Active));

        ResponseData<bool> result = await CreateService().DeleteAsync(id, Guid.NewGuid(), "127.0.0.1", "ua");

        result.StatusCode.Should().Be(409);
        _products.Verify(r => r.DeleteAsync(It.IsAny<Product>()), Times.Never);
    }

    // ── Paging ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAllAsync_ReturnsPagedResultWithTenantScope()
    {
        var catId = Guid.NewGuid();
        var list = new List<Product> { Product(Guid.NewGuid(), catId), Product(Guid.NewGuid(), catId) };
        _products.Setup(r => r.GetPagedAsync(It.IsAny<ProductFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((list, 5));

        ResponseData<PagedResult<ProductResponse>> result = await CreateService().GetAllAsync(new ProductFilter { Page = 1, PageSize = 2 });

        result.IsSuccess.Should().BeTrue();
        result.Data!.Items.Should().HaveCount(2);
        result.Data.TotalCount.Should().Be(5);
        result.Data.TotalPages.Should().Be(3);
        _products.Verify(r => r.GetPagedAsync(It.Is<ProductFilter>(f => f.TenantId == _tenantId),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
