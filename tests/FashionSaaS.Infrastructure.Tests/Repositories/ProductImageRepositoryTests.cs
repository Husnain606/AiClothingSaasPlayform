using FashionSaaS.Application.Interfaces;
using FashionSaaS.Domain.Entities;
using FashionSaaS.Infrastructure.Persistence;
using FashionSaaS.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace FashionSaaS.Infrastructure.Tests.Repositories;

public class ProductImageRepositoryTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _productId = Guid.NewGuid();

    private ApplicationDbContext CreateContext()
    {
        var currentTenant = new Mock<ICurrentTenantService>();
        currentTenant.Setup(c => c.TenantId).Returns(_tenantId);

        DbContextOptions<ApplicationDbContext> options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options, currentTenant.Object);
    }

    [Fact]
    public async Task GetByProductAsync_ProductWithImages_ReturnsAllImagesOrderedBySortOrder()
    {
        await using ApplicationDbContext ctx = CreateContext();
        var image1 = new ProductImage
        {
            TenantId = _tenantId,
            ProductId = _productId,
            CloudinaryPublicId = "img1",
            Url = "https://cdn.example.com/img1.jpg",
            AltText = "Front view",
            SortOrder = 2
        };
        var image2 = new ProductImage
        {
            TenantId = _tenantId,
            ProductId = _productId,
            CloudinaryPublicId = "img2",
            Url = "https://cdn.example.com/img2.jpg",
            AltText = "Side view",
            SortOrder = 1
        };
        ctx.ProductImages.AddRange(image1, image2);
        await ctx.SaveChangesAsync();

        var repo = new ProductImageRepository(ctx);
        IReadOnlyList<ProductImage> result = await repo.GetByProductAsync(_productId);

        result.Should().HaveCount(2);
        result.Should().BeInAscendingOrder(x => x.SortOrder);
    }

    [Fact]
    public async Task GetByProductAsync_NoImages_ReturnsEmptyList()
    {
        await using ApplicationDbContext ctx = CreateContext();

        var repo = new ProductImageRepository(ctx);
        IReadOnlyList<ProductImage> result = await repo.GetByProductAsync(_productId);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetPrimaryAsync_ProductWithPrimaryImage_ReturnsPrimaryImage()
    {
        await using ApplicationDbContext ctx = CreateContext();
        var primary = new ProductImage
        {
            TenantId = _tenantId,
            ProductId = _productId,
            CloudinaryPublicId = "primary-img",
            Url = "https://cdn.example.com/primary.jpg",
            SortOrder = 1,
            IsPrimary = true
        };
        var secondary = new ProductImage
        {
            TenantId = _tenantId,
            ProductId = _productId,
            CloudinaryPublicId = "secondary-img",
            Url = "https://cdn.example.com/secondary.jpg",
            SortOrder = 2,
            IsPrimary = false
        };
        ctx.ProductImages.AddRange(primary, secondary);
        await ctx.SaveChangesAsync();

        var repo = new ProductImageRepository(ctx);
        ProductImage? result = await repo.GetPrimaryAsync(_productId);

        result.Should().NotBeNull();
        result!.IsPrimary.Should().BeTrue();
        result.CloudinaryPublicId.Should().Be("primary-img");
    }

    [Fact]
    public async Task GetPrimaryAsync_NoPrimaryImage_ReturnsNull()
    {
        await using ApplicationDbContext ctx = CreateContext();
        var image = new ProductImage
        {
            TenantId = _tenantId,
            ProductId = _productId,
            CloudinaryPublicId = "img",
            Url = "https://cdn.example.com/img.jpg",
            SortOrder = 1,
            IsPrimary = false
        };
        ctx.ProductImages.Add(image);
        await ctx.SaveChangesAsync();

        var repo = new ProductImageRepository(ctx);
        ProductImage? result = await repo.GetPrimaryAsync(_productId);

        result.Should().BeNull();
    }
}
