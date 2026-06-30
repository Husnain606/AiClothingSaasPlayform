using FashionSaaS.Application.Interfaces;
using FashionSaaS.Domain.Entities;
using FashionSaaS.Infrastructure.Persistence;
using FashionSaaS.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace FashionSaaS.Infrastructure.Tests.Repositories;

public class ProductVariantRepositoryTests
{
    private Guid _tenantId = Guid.NewGuid();
    private Guid _productId = Guid.NewGuid();

    private ApplicationDbContext CreateContext()
    {
        var currentTenant = new Mock<ICurrentTenantService>();
        currentTenant.Setup(c => c.TenantId).Returns(_tenantId);

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options, currentTenant.Object);
    }

    private void SeedProduct(ApplicationDbContext ctx)
    {
        var category = new Category
        {
            TenantId = _tenantId,
            Name = "Shoes",
            Slug = "shoes",
            SortOrder = 1,
            IsActive = true
        };
        var product = new Product
        {
            Id = _productId,
            TenantId = _tenantId,
            Name = "Nike Air Max",
            Slug = "nike-air-max",
            Description = "Great shoes",
            CategoryId = category.Id,
            BasePrice = 99.99m
        };
        ctx.Categories.Add(category);
        ctx.Products.Add(product);
        ctx.SaveChanges();
    }

    [Fact]
    public async Task SkuExistsAsync_ExistingSku_ReturnsTrue()
    {
        await using var ctx = CreateContext();
        SeedProduct(ctx);
        var variant = new ProductVariant
        {
            TenantId = _tenantId,
            ProductId = _productId,
            Size = "10",
            Color = "Black",
            Sku = "NAM-10",
            IsActive = true
        };
        ctx.ProductVariants.Add(variant);
        await ctx.SaveChangesAsync();

        var repo = new ProductVariantRepository(ctx);
        var exists = await repo.SkuExistsAsync(_tenantId, "NAM-10");

        exists.Should().BeTrue();
    }

    [Fact]
    public async Task SkuExistsAsync_ExcludeId_IgnoresSpecificId()
    {
        await using var ctx = CreateContext();
        SeedProduct(ctx);
        var variant = new ProductVariant
        {
            TenantId = _tenantId,
            ProductId = _productId,
            Size = "10",
            Color = "Black",
            Sku = "NAM-10",
            IsActive = true
        };
        ctx.ProductVariants.Add(variant);
        await ctx.SaveChangesAsync();

        var repo = new ProductVariantRepository(ctx);
        var exists = await repo.SkuExistsAsync(_tenantId, "NAM-10", variant.Id);

        exists.Should().BeFalse();
    }

    [Fact]
    public async Task GetByProductAsync_ProductWithVariants_ReturnsAllVariants()
    {
        await using var ctx = CreateContext();
        SeedProduct(ctx);
        var var1 = new ProductVariant
        {
            TenantId = _tenantId,
            ProductId = _productId,
            Size = "10",
            Color = "Black",
            Sku = "NAM-10",
            IsActive = true
        };
        var var2 = new ProductVariant
        {
            TenantId = _tenantId,
            ProductId = _productId,
            Size = "11",
            Color = "White",
            Sku = "NAM-11",
            IsActive = true
        };
        ctx.ProductVariants.AddRange(var1, var2);
        await ctx.SaveChangesAsync();

        var repo = new ProductVariantRepository(ctx);
        var result = await repo.GetByProductAsync(_productId);

        result.Should().HaveCount(2);
    }
}
