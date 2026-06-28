using System.Text;
using FashionSaaS.Application.Interfaces;
using FashionSaaS.Application.ProductImages;
using FashionSaaS.Application.ProductImages.DTOs;
using FashionSaaS.Domain.Entities;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FashionSaaS.Application.Tests.ProductImages;

public class ProductImageServiceTests
{
    private readonly Mock<IProductImageRepository> _images = new();
    private readonly Mock<IProductRepository> _products = new();
    private readonly Mock<IImageStorageService> _storage = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IAuditLogService> _audit = new();
    private readonly Mock<ICurrentTenantService> _tenant = new();
    private readonly Guid _tenantId = Guid.NewGuid();

    public ProductImageServiceTests()
    {
        _tenant.SetupGet(t => t.TenantId).Returns(_tenantId);
    }

    private ProductImageService CreateService() => new(
        _images.Object, _products.Object, _storage.Object, _uow.Object, _audit.Object,
        _tenant.Object, NullLogger<ProductImageService>.Instance);

    private Product Product(Guid id) => new()
    {
        Id = id, TenantId = _tenantId, CategoryId = Guid.NewGuid(), Name = "Tee", Slug = "tee", BasePrice = 20m
    };

    private ProductImage Image(Guid productId, int sortOrder = 0, bool isPrimary = false) => new()
    {
        Id = Guid.NewGuid(), TenantId = _tenantId, ProductId = productId,
        CloudinaryPublicId = $"pid-{sortOrder}", Url = $"https://cdn/img-{sortOrder}.jpg",
        SortOrder = sortOrder, IsPrimary = isPrimary
    };

    private static Stream Content() => new MemoryStream(Encoding.UTF8.GetBytes("binary"));

    // ── Upload ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UploadAsync_FirstImage_PersistsPublicIdUrl_AndBecomesPrimary()
    {
        var productId = Guid.NewGuid();
        _products.Setup(r => r.GetByIdAsync(productId)).ReturnsAsync(Product(productId));
        _images.Setup(r => r.GetByProductAsync(productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductImage>());
        _storage.Setup(s => s.UploadAsync(It.IsAny<Stream>(), "a.jpg", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(("public-123", "https://cdn/a.jpg"));

        ProductImage? saved = null;
        _images.Setup(r => r.AddAsync(It.IsAny<ProductImage>()))
            .Callback<ProductImage>(i => saved = i).Returns(Task.CompletedTask);

        var result = await CreateService().UploadAsync(
            new UploadImageRequest { ProductId = productId, AltText = "alt" }, Content(), "a.jpg",
            Guid.NewGuid(), "127.0.0.1", "ua");

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(201);
        saved!.CloudinaryPublicId.Should().Be("public-123");
        saved.Url.Should().Be("https://cdn/a.jpg");
        saved.IsPrimary.Should().BeTrue();
        result.Data!.IsPrimary.Should().BeTrue();
        result.Data.Url.Should().Be("https://cdn/a.jpg");
    }

    [Fact]
    public async Task UploadAsync_SubsequentImage_IsNotPrimary_AndGetsNextSortOrder()
    {
        var productId = Guid.NewGuid();
        _products.Setup(r => r.GetByIdAsync(productId)).ReturnsAsync(Product(productId));
        _images.Setup(r => r.GetByProductAsync(productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductImage> { Image(productId, 0, isPrimary: true) });
        _storage.Setup(s => s.UploadAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(("public-2", "https://cdn/b.jpg"));

        ProductImage? saved = null;
        _images.Setup(r => r.AddAsync(It.IsAny<ProductImage>()))
            .Callback<ProductImage>(i => saved = i).Returns(Task.CompletedTask);

        var result = await CreateService().UploadAsync(
            new UploadImageRequest { ProductId = productId }, Content(), "b.jpg",
            Guid.NewGuid(), "127.0.0.1", "ua");

        result.IsSuccess.Should().BeTrue();
        saved!.IsPrimary.Should().BeFalse();
        saved.SortOrder.Should().Be(1);
    }

    [Fact]
    public async Task UploadAsync_FolderIsTenantAndProductScoped()
    {
        var productId = Guid.NewGuid();
        _products.Setup(r => r.GetByIdAsync(productId)).ReturnsAsync(Product(productId));
        _images.Setup(r => r.GetByProductAsync(productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductImage>());
        string? folderUsed = null;
        _storage.Setup(s => s.UploadAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<Stream, string, string, CancellationToken>((_, _, folder, _) => folderUsed = folder)
            .ReturnsAsync(("p", "u"));

        await CreateService().UploadAsync(
            new UploadImageRequest { ProductId = productId }, Content(), "a.jpg",
            Guid.NewGuid(), "127.0.0.1", "ua");

        folderUsed.Should().Be($"tenants/{_tenantId}/products/{productId}");
    }

    [Fact]
    public async Task UploadAsync_ProductNotFound_Returns404_NoStorageCall()
    {
        var productId = Guid.NewGuid();
        _products.Setup(r => r.GetByIdAsync(productId))
            .ReturnsAsync(new Product { Id = productId, TenantId = Guid.NewGuid() });

        var result = await CreateService().UploadAsync(
            new UploadImageRequest { ProductId = productId }, Content(), "a.jpg",
            Guid.NewGuid(), "127.0.0.1", "ua");

        result.StatusCode.Should().Be(404);
        _storage.Verify(s => s.UploadAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _images.Verify(r => r.AddAsync(It.IsAny<ProductImage>()), Times.Never);
    }

    // ── SetPrimary ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SetPrimaryAsync_EnforcesSinglePrimary()
    {
        var productId = Guid.NewGuid();
        var img1 = Image(productId, 0, isPrimary: true);
        var img2 = Image(productId, 1, isPrimary: false);
        var img3 = Image(productId, 2, isPrimary: false);
        _images.Setup(r => r.GetByIdAsync(img2.Id)).ReturnsAsync(img2);
        _images.Setup(r => r.GetByProductAsync(productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductImage> { img1, img2, img3 });

        var result = await CreateService().SetPrimaryAsync(
            new SetPrimaryRequest { ImageId = img2.Id }, Guid.NewGuid(), "127.0.0.1", "ua");

        result.IsSuccess.Should().BeTrue();
        img1.IsPrimary.Should().BeFalse();
        img2.IsPrimary.Should().BeTrue();
        img3.IsPrimary.Should().BeFalse();
        new[] { img1, img2, img3 }.Count(i => i.IsPrimary).Should().Be(1);
    }

    [Fact]
    public async Task SetPrimaryAsync_ImageFromAnotherTenant_Returns404()
    {
        var img = new ProductImage { Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), ProductId = Guid.NewGuid() };
        _images.Setup(r => r.GetByIdAsync(img.Id)).ReturnsAsync(img);

        var result = await CreateService().SetPrimaryAsync(
            new SetPrimaryRequest { ImageId = img.Id }, Guid.NewGuid(), "127.0.0.1", "ua");

        result.StatusCode.Should().Be(404);
    }

    // ── Delete ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_StorageThrows_StillRemovesRow()
    {
        var productId = Guid.NewGuid();
        var img = Image(productId, 0, isPrimary: false);
        _images.Setup(r => r.GetByIdAsync(img.Id)).ReturnsAsync(img);
        _images.Setup(r => r.GetByProductAsync(productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductImage> { img });
        _storage.Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("cloudinary down"));

        var result = await CreateService().DeleteAsync(img.Id, Guid.NewGuid(), "127.0.0.1", "ua");

        result.IsSuccess.Should().BeTrue();
        _images.Verify(r => r.DeleteAsync(img), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_PrimaryDeleted_PromotesNextImageToPrimary()
    {
        var productId = Guid.NewGuid();
        var primary = Image(productId, 0, isPrimary: true);
        var next = Image(productId, 1, isPrimary: false);
        var third = Image(productId, 2, isPrimary: false);
        _images.Setup(r => r.GetByIdAsync(primary.Id)).ReturnsAsync(primary);
        // After deletion, listing returns the remaining images (service filters out the deleted id too).
        _images.Setup(r => r.GetByProductAsync(productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductImage> { next, third });

        var result = await CreateService().DeleteAsync(primary.Id, Guid.NewGuid(), "127.0.0.1", "ua");

        result.IsSuccess.Should().BeTrue();
        next.IsPrimary.Should().BeTrue();
        third.IsPrimary.Should().BeFalse();
        _images.Verify(r => r.UpdateAsync(next), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_NonPrimaryDeleted_DoesNotPromote()
    {
        var productId = Guid.NewGuid();
        var primary = Image(productId, 0, isPrimary: true);
        var target = Image(productId, 1, isPrimary: false);
        _images.Setup(r => r.GetByIdAsync(target.Id)).ReturnsAsync(target);

        var result = await CreateService().DeleteAsync(target.Id, Guid.NewGuid(), "127.0.0.1", "ua");

        result.IsSuccess.Should().BeTrue();
        primary.IsPrimary.Should().BeTrue();
        // No promotion path: GetByProductAsync should not be consulted for promotion.
        _images.Verify(r => r.GetByProductAsync(productId, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteAsync_ImageNotFound_Returns404()
    {
        _images.Setup(r => r.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((ProductImage?)null);

        var result = await CreateService().DeleteAsync(Guid.NewGuid(), Guid.NewGuid(), "127.0.0.1", "ua");

        result.StatusCode.Should().Be(404);
        _images.Verify(r => r.DeleteAsync(It.IsAny<ProductImage>()), Times.Never);
    }

    // ── Reorder ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReorderAsync_UpdatesSortOrderToMatchProvidedOrder()
    {
        var productId = Guid.NewGuid();
        var a = Image(productId, 0);
        var b = Image(productId, 1);
        var c = Image(productId, 2);
        _products.Setup(r => r.GetByIdAsync(productId)).ReturnsAsync(Product(productId));
        _images.Setup(r => r.GetByProductAsync(productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductImage> { a, b, c });

        // Desired order: c, a, b
        var result = await CreateService().ReorderAsync(productId,
            new ReorderImagesRequest { Ids = new[] { c.Id, a.Id, b.Id } },
            Guid.NewGuid(), "127.0.0.1", "ua");

        result.IsSuccess.Should().BeTrue();
        c.SortOrder.Should().Be(0);
        a.SortOrder.Should().Be(1);
        b.SortOrder.Should().Be(2);
    }

    [Fact]
    public async Task ReorderAsync_ProductNotFound_Returns404()
    {
        var productId = Guid.NewGuid();
        _products.Setup(r => r.GetByIdAsync(productId))
            .ReturnsAsync(new Product { Id = productId, TenantId = Guid.NewGuid() });

        var result = await CreateService().ReorderAsync(productId,
            new ReorderImagesRequest { Ids = new[] { Guid.NewGuid() } },
            Guid.NewGuid(), "127.0.0.1", "ua");

        result.StatusCode.Should().Be(404);
    }

    // ── GetByProduct ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetByProductAsync_ReturnsOrderedBySortOrder()
    {
        var productId = Guid.NewGuid();
        var a = Image(productId, 2);
        var b = Image(productId, 0);
        var c = Image(productId, 1);
        _products.Setup(r => r.GetByIdAsync(productId)).ReturnsAsync(Product(productId));
        _images.Setup(r => r.GetByProductAsync(productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductImage> { a, b, c });

        var result = await CreateService().GetByProductAsync(productId);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Select(i => i.Id).Should().ContainInOrder(b.Id, c.Id, a.Id);
    }
}
