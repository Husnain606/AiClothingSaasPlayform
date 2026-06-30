using FashionSaaS.Application.Interfaces;
using FashionSaaS.Domain.Entities;
using FashionSaaS.Infrastructure.Persistence;
using FashionSaaS.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace FashionSaaS.Infrastructure.Tests.Repositories;

public class CategoryRepositoryTests
{
    private Guid _tenantId = Guid.NewGuid();

    private ApplicationDbContext CreateContext()
    {
        var currentTenant = new Mock<ICurrentTenantService>();
        currentTenant.Setup(c => c.TenantId).Returns(_tenantId);

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options, currentTenant.Object);
    }

    [Fact]
    public async Task SlugExistsAsync_ExistingSlug_ReturnsTrue()
    {
        await using var ctx = CreateContext();
        var category = new Category
        {
            TenantId = _tenantId,
            Name = "Shoes",
            Slug = "shoes",
            SortOrder = 1,
            IsActive = true
        };
        ctx.Categories.Add(category);
        await ctx.SaveChangesAsync();

        var repo = new CategoryRepository(ctx);
        var exists = await repo.SlugExistsAsync(_tenantId, "shoes");

        exists.Should().BeTrue();
    }

    [Fact]
    public async Task SlugExistsAsync_ExcludeId_IgnoresSpecificId()
    {
        await using var ctx = CreateContext();
        var cat1 = new Category
        {
            TenantId = _tenantId,
            Name = "Shoes",
            Slug = "shoes",
            SortOrder = 1,
            IsActive = true
        };
        ctx.Categories.Add(cat1);
        await ctx.SaveChangesAsync();

        var repo = new CategoryRepository(ctx);
        var exists = await repo.SlugExistsAsync(_tenantId, "shoes", cat1.Id);

        exists.Should().BeFalse();
    }

    [Fact]
    public async Task SlugExistsAsync_DifferentTenant_ReturnsFalse()
    {
        await using var ctx = CreateContext();
        var otherTenantId = Guid.NewGuid();
        var category = new Category
        {
            TenantId = otherTenantId,
            Name = "Shoes",
            Slug = "shoes",
            SortOrder = 1,
            IsActive = true
        };
        ctx.Categories.Add(category);
        await ctx.SaveChangesAsync();

        var repo = new CategoryRepository(ctx);
        var exists = await repo.SlugExistsAsync(_tenantId, "shoes");

        exists.Should().BeFalse();
    }

    [Fact]
    public async Task GetTreeAsync_CategoriesWithParents_ReturnsSortedByParentThenOrder()
    {
        await using var ctx = CreateContext();
        var parentCat = new Category
        {
            TenantId = _tenantId,
            Name = "Apparel",
            Slug = "apparel",
            SortOrder = 1,
            IsActive = true
        };
        var child1 = new Category
        {
            TenantId = _tenantId,
            Name = "Shirts",
            Slug = "shirts",
            ParentCategoryId = parentCat.Id,
            SortOrder = 2,
            IsActive = true
        };
        var child2 = new Category
        {
            TenantId = _tenantId,
            Name = "Pants",
            Slug = "pants",
            ParentCategoryId = parentCat.Id,
            SortOrder = 1,
            IsActive = true
        };
        ctx.Categories.AddRange(parentCat, child1, child2);
        await ctx.SaveChangesAsync();

        var repo = new CategoryRepository(ctx);
        var result = await repo.GetTreeAsync(_tenantId);

        result.Should().HaveCount(3);
        result.Should().SatisfyRespectively(
            x => x.Id.Should().Be(parentCat.Id),
            x => x.Id.Should().Be(child2.Id), // SortOrder 1
            x => x.Id.Should().Be(child1.Id)  // SortOrder 2
        );
    }

    [Fact]
    public async Task HasChildrenAsync_CategoryWithChildren_ReturnsTrue()
    {
        await using var ctx = CreateContext();
        var parent = new Category
        {
            TenantId = _tenantId,
            Name = "Apparel",
            Slug = "apparel",
            SortOrder = 1,
            IsActive = true
        };
        var child = new Category
        {
            TenantId = _tenantId,
            Name = "Shirts",
            Slug = "shirts",
            ParentCategoryId = parent.Id,
            SortOrder = 1,
            IsActive = true
        };
        ctx.Categories.AddRange(parent, child);
        await ctx.SaveChangesAsync();

        var repo = new CategoryRepository(ctx);
        var hasChildren = await repo.HasChildrenAsync(_tenantId, parent.Id);

        hasChildren.Should().BeTrue();
    }

    [Fact]
    public async Task HasChildrenAsync_CategoryWithoutChildren_ReturnsFalse()
    {
        await using var ctx = CreateContext();
        var category = new Category
        {
            TenantId = _tenantId,
            Name = "Shoes",
            Slug = "shoes",
            SortOrder = 1,
            IsActive = true
        };
        ctx.Categories.Add(category);
        await ctx.SaveChangesAsync();

        var repo = new CategoryRepository(ctx);
        var hasChildren = await repo.HasChildrenAsync(_tenantId, category.Id);

        hasChildren.Should().BeFalse();
    }

    [Fact]
    public async Task HasProductsAsync_CategoryWithProducts_ReturnsTrue()
    {
        await using var ctx = CreateContext();
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
            TenantId = _tenantId,
            Name = "Nike Air Max",
            Slug = "nike-air-max",
            Description = "Great shoes",
            CategoryId = category.Id,
            BasePrice = 99.99m
        };
        ctx.Categories.Add(category);
        ctx.Products.Add(product);
        await ctx.SaveChangesAsync();

        var repo = new CategoryRepository(ctx);
        var hasProducts = await repo.HasProductsAsync(_tenantId, category.Id);

        hasProducts.Should().BeTrue();
    }
}
