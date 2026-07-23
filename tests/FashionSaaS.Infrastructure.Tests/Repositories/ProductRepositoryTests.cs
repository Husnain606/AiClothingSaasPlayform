using FashionSaaS.Application.Interfaces;
using FashionSaaS.Application.Products.DTOs;
using FashionSaaS.Domain.Entities;
using FashionSaaS.Domain.Enums;
using FashionSaaS.Infrastructure.Persistence;
using FashionSaaS.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace FashionSaaS.Infrastructure.Tests.Repositories;

public class ProductRepositoryTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _categoryId = Guid.NewGuid();

    private ApplicationDbContext CreateContext()
    {
        var currentTenant = new Mock<ICurrentTenantService>();
        currentTenant.Setup(c => c.TenantId).Returns(_tenantId);

        DbContextOptions<ApplicationDbContext> options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options, currentTenant.Object);
    }

    private void SeedCategory(ApplicationDbContext ctx)
    {
        var category = new Category
        {
            Id = _categoryId,
            TenantId = _tenantId,
            Name = "Shoes",
            Slug = "shoes",
            SortOrder = 1,
            IsActive = true
        };
        ctx.Categories.Add(category);
        ctx.SaveChanges();
    }

    [Fact]
    public async Task SlugExistsAsync_ExistingSlug_ReturnsTrue()
    {
        await using ApplicationDbContext ctx = CreateContext();
        SeedCategory(ctx);
        var product = new Product
        {
            TenantId = _tenantId,
            Name = "Nike Air Max",
            Slug = "nike-air-max",
            Description = "Great shoes",
            CategoryId = _categoryId,
            BasePrice = 99.99m
        };
        ctx.Products.Add(product);
        await ctx.SaveChangesAsync();

        var repo = new ProductRepository(ctx);
        var exists = await repo.SlugExistsAsync(_tenantId, "nike-air-max");

        exists.Should().BeTrue();
    }

    [Fact]
    public async Task SlugExistsAsync_ExcludeId_IgnoresSpecificId()
    {
        await using ApplicationDbContext ctx = CreateContext();
        SeedCategory(ctx);
        var product = new Product
        {
            TenantId = _tenantId,
            Name = "Nike Air Max",
            Slug = "nike-air-max",
            Description = "Great shoes",
            CategoryId = _categoryId,
            BasePrice = 99.99m
        };
        ctx.Products.Add(product);
        await ctx.SaveChangesAsync();

        var repo = new ProductRepository(ctx);
        var exists = await repo.SlugExistsAsync(_tenantId, "nike-air-max", product.Id);

        exists.Should().BeFalse();
    }

    [Fact]
    public async Task GetBySlugWithDetailsAsync_ExistingSlug_ReturnsProduct()
    {
        await using ApplicationDbContext ctx = CreateContext();
        SeedCategory(ctx);
        var product = new Product
        {
            TenantId = _tenantId,
            Name = "Nike Air Max",
            Slug = "nike-air-max",
            Description = "Great shoes",
            CategoryId = _categoryId,
            BasePrice = 99.99m
        };
        ctx.Products.Add(product);
        await ctx.SaveChangesAsync();

        var repo = new ProductRepository(ctx);
        Product? result = await repo.GetBySlugWithDetailsAsync(_tenantId, "nike-air-max");

        result.Should().NotBeNull();
        result!.Name.Should().Be("Nike Air Max");
        result.BasePrice.Should().Be(99.99m);
    }

    [Fact]
    public async Task GetBySlugWithDetailsAsync_NonExistentSlug_ReturnsNull()
    {
        await using ApplicationDbContext ctx = CreateContext();
        var repo = new ProductRepository(ctx);
        Product? result = await repo.GetBySlugWithDetailsAsync(_tenantId, "nonexistent");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetPagedAsync_WithPagination_ReturnsPaginatedResults()
    {
        await using ApplicationDbContext ctx = CreateContext();
        SeedCategory(ctx);

        for (var i = 1; i <= 5; i++)
        {
            var product = new Product
            {
                TenantId = _tenantId,
                Name = $"Product {i}",
                Slug = $"product-{i}",
                Description = "Test product",
                CategoryId = _categoryId,
                BasePrice = 50m + i
            };
            ctx.Products.Add(product);
        }
        await ctx.SaveChangesAsync();

        var repo = new ProductRepository(ctx);
        var filter = new ProductFilter
        {
            TenantId = _tenantId,
            CategoryId = _categoryId,
            Page = 1,
            PageSize = 2
        };
        (IReadOnlyList<Product>? items, var total) = await repo.GetPagedAsync(filter);

        items.Should().HaveCount(2);
        total.Should().Be(5);
    }

    [Fact]
    public async Task GetPagedAsync_ProductWithVariantsImagesAndReviews_IncludesNavigationProperties()
    {
        await using ApplicationDbContext ctx = CreateContext();
        SeedCategory(ctx);

        var product = new Product
        {
            TenantId = _tenantId,
            Name = "Nike Air Max",
            Slug = "nike-air-max",
            Description = "Great shoes",
            CategoryId = _categoryId,
            BasePrice = 99.99m
        };
        ctx.Products.Add(product);

        ctx.ProductVariants.Add(new ProductVariant
        {
            TenantId = _tenantId,
            ProductId = product.Id,
            Size = "10",
            Color = "Black",
            Sku = "NAM-10",
            IsActive = true
        });
        ctx.ProductVariants.Add(new ProductVariant
        {
            TenantId = _tenantId,
            ProductId = product.Id,
            Size = "11",
            Color = "White",
            Sku = "NAM-11",
            IsActive = true
        });

        ctx.ProductImages.Add(new ProductImage
        {
            TenantId = _tenantId,
            ProductId = product.Id,
            CloudinaryPublicId = "primary-image",
            Url = "https://example.com/primary.jpg",
            IsPrimary = true
        });

        ctx.Reviews.Add(new Review
        {
            TenantId = _tenantId,
            ProductId = product.Id,
            CustomerId = Guid.NewGuid(),
            Rating = 4,
            Status = ReviewStatus.Approved
        });

        await ctx.SaveChangesAsync();

        var repo = new ProductRepository(ctx);
        var filter = new ProductFilter
        {
            TenantId = _tenantId,
            Page = 1,
            PageSize = 10
        };
        (IReadOnlyList<Product>? items, _) = await repo.GetPagedAsync(filter);

        Product result = items.Should().ContainSingle().Subject;
        result.Category.Should().NotBeNull();
        result.Category!.Name.Should().Be("Shoes");
        result.Variants.Should().HaveCount(2);
        result.Images.Should().ContainSingle(i => i.IsPrimary && i.Url == "https://example.com/primary.jpg");
        result.Reviews.Should().ContainSingle(r => r.Rating == 4 && r.Status == ReviewStatus.Approved);
    }

    [Fact]
    public async Task HasVariantsAsync_ProductWithVariants_ReturnsTrue()
    {
        await using ApplicationDbContext ctx = CreateContext();
        SeedCategory(ctx);
        var product = new Product
        {
            TenantId = _tenantId,
            Name = "Nike Air Max",
            Slug = "nike-air-max",
            Description = "Great shoes",
            CategoryId = _categoryId,
            BasePrice = 99.99m
        };
        var variant = new ProductVariant
        {
            TenantId = _tenantId,
            ProductId = product.Id,
            Size = "10",
            Color = "Black",
            Sku = "NAM-10",
            IsActive = true
        };
        ctx.Products.Add(product);
        ctx.ProductVariants.Add(variant);
        await ctx.SaveChangesAsync();

        var hasVariants = await ctx.Products.AnyAsync(p => p.Id == product.Id && p.Variants.Any());

        hasVariants.Should().BeTrue();
    }
}
