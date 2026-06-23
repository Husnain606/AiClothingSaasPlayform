using FashionSaaS.Application.Interfaces;
using FashionSaaS.Domain.Entities;
using FashionSaaS.Infrastructure.Persistence;
using FashionSaaS.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace FashionSaaS.Infrastructure.Tests.Repositories;

public class TenantRepositoryTests
{
    private ApplicationDbContext CreateContext()
    {
        var currentTenant = new Mock<ICurrentTenantService>();
        currentTenant.Setup(c => c.TenantId).Returns((Guid?)null);

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options, currentTenant.Object);
    }

    [Fact]
    public async Task GetBySlugAsync_ExistingSlug_ReturnsTenant()
    {
        await using var ctx = CreateContext();
        var tenant = new Tenant { Name = "Nike", Slug = "nike", Email = "admin@nike.com" };
        ctx.Tenants.Add(tenant);
        await ctx.SaveChangesAsync();

        var repo = new TenantRepository(ctx);
        var result = await repo.GetBySlugAsync("nike");

        result.Should().NotBeNull();
        result!.Name.Should().Be("Nike");
    }

    [Fact]
    public async Task SlugExistsAsync_NonExistentSlug_ReturnsFalse()
    {
        await using var ctx = CreateContext();
        var repo = new TenantRepository(ctx);
        var exists = await repo.SlugExistsAsync("nonexistent");
        exists.Should().BeFalse();
    }

    [Fact]
    public async Task AddAsync_SavesChangesViaUnitOfWork_PersistsTenant()
    {
        await using var ctx = CreateContext();
        var repo = new TenantRepository(ctx);
        var tenant = new Tenant { Name = "Adidas", Slug = "adidas", Email = "admin@adidas.com" };

        await repo.AddAsync(tenant);
        await ctx.SaveChangesAsync();

        var saved = await ctx.Tenants.FindAsync(tenant.Id);
        saved.Should().NotBeNull();
        saved!.Slug.Should().Be("adidas");
    }
}
