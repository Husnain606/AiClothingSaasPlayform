using FashionSaaS.Application.Discounts.DTOs;
using FashionSaaS.Application.Interfaces;
using FashionSaaS.Domain.Entities;
using FashionSaaS.Domain.Enums;
using FashionSaaS.Infrastructure.Persistence;
using FashionSaaS.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace FashionSaaS.Infrastructure.Tests.Repositories;

public class DiscountRepositoryTests
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
    public async Task GetByCodeAsync_ExistingCode_ReturnsDiscount()
    {
        await using var ctx = CreateContext();
        var discount = new Discount
        {
            TenantId = _tenantId,
            Code = "SUMMER20",
            Type = DiscountType.Percentage,
            Value = 20,
            IsActive = true,
            StartsAt = DateTime.UtcNow.AddDays(-1),
            EndsAt = DateTime.UtcNow.AddDays(30)
        };
        ctx.Discounts.Add(discount);
        await ctx.SaveChangesAsync();

        var repo = new DiscountRepository(ctx);
        var result = await repo.GetByCodeAsync(_tenantId, "SUMMER20");

        result.Should().NotBeNull();
        result!.Value.Should().Be(20);
        result.Type.Should().Be(DiscountType.Percentage);
    }

    [Fact]
    public async Task GetByCodeAsync_NonExistentCode_ReturnsNull()
    {
        await using var ctx = CreateContext();

        var repo = new DiscountRepository(ctx);
        var result = await repo.GetByCodeAsync(_tenantId, "NONEXISTENT");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByCodeAsync_DifferentTenant_ReturnsNull()
    {
        await using var ctx = CreateContext();
        var otherTenantId = Guid.NewGuid();
        var discount = new Discount
        {
            TenantId = otherTenantId,
            Code = "SUMMER20",
            Type = DiscountType.Percentage,
            Value = 20,
            IsActive = true,
            StartsAt = DateTime.UtcNow.AddDays(-1),
            EndsAt = DateTime.UtcNow.AddDays(30)
        };
        ctx.Discounts.Add(discount);
        await ctx.SaveChangesAsync();

        var repo = new DiscountRepository(ctx);
        var result = await repo.GetByCodeAsync(_tenantId, "SUMMER20");

        result.Should().BeNull();
    }

    [Fact]
    public async Task CodeExistsAsync_ExistingCode_ReturnsTrue()
    {
        await using var ctx = CreateContext();
        var discount = new Discount
        {
            TenantId = _tenantId,
            Code = "WINTER10",
            Type = DiscountType.FixedAmount,
            Value = 10,
            IsActive = true,
            StartsAt = DateTime.UtcNow.AddDays(-1),
            EndsAt = DateTime.UtcNow.AddDays(30)
        };
        ctx.Discounts.Add(discount);
        await ctx.SaveChangesAsync();

        var repo = new DiscountRepository(ctx);
        var exists = await repo.CodeExistsAsync(_tenantId, "WINTER10");

        exists.Should().BeTrue();
    }

    [Fact]
    public async Task CodeExistsAsync_ExcludeId_IgnoresSpecificDiscount()
    {
        await using var ctx = CreateContext();
        var discount = new Discount
        {
            TenantId = _tenantId,
            Code = "WINTER10",
            Type = DiscountType.FixedAmount,
            Value = 10,
            IsActive = true,
            StartsAt = DateTime.UtcNow.AddDays(-1),
            EndsAt = DateTime.UtcNow.AddDays(30)
        };
        ctx.Discounts.Add(discount);
        await ctx.SaveChangesAsync();

        var repo = new DiscountRepository(ctx);
        var exists = await repo.CodeExistsAsync(_tenantId, "WINTER10", discount.Id);

        exists.Should().BeFalse();
    }

    [Fact]
    public async Task GetPagedAsync_WithIsActiveFilter_ReturnsOnlyActiveDiscounts()
    {
        await using var ctx = CreateContext();
        var active = new Discount
        {
            TenantId = _tenantId,
            Code = "SUMMER20",
            Type = DiscountType.Percentage,
            Value = 20,
            IsActive = true,
            StartsAt = DateTime.UtcNow.AddDays(-1),
            EndsAt = DateTime.UtcNow.AddDays(30)
        };
        var inactive = new Discount
        {
            TenantId = _tenantId,
            Code = "WINTER10",
            Type = DiscountType.Percentage,
            Value = 10,
            IsActive = false,
            StartsAt = DateTime.UtcNow.AddDays(-1),
            EndsAt = DateTime.UtcNow.AddDays(30)
        };
        ctx.Discounts.AddRange(active, inactive);
        await ctx.SaveChangesAsync();

        var repo = new DiscountRepository(ctx);
        var filter = new DiscountFilter
        {
            TenantId = _tenantId,
            IsActive = true,
            Page = 1,
            PageSize = 20
        };
        var (items, total) = await repo.GetPagedAsync(filter);

        items.Should().HaveCount(1);
        total.Should().Be(1);
        items.First().Code.Should().Be("SUMMER20");
    }

    [Fact]
    public async Task GetPagedAsync_WithPagination_ReturnsPaginatedResults()
    {
        await using var ctx = CreateContext();
        for (int i = 1; i <= 5; i++)
        {
            var discount = new Discount
            {
                TenantId = _tenantId,
                Code = $"CODE{i:D2}",
                Type = DiscountType.Percentage,
                Value = i * 5,
                IsActive = true,
                StartsAt = DateTime.UtcNow.AddDays(-1),
                EndsAt = DateTime.UtcNow.AddDays(30)
            };
            ctx.Discounts.Add(discount);
        }
        await ctx.SaveChangesAsync();

        var repo = new DiscountRepository(ctx);
        var filter = new DiscountFilter
        {
            TenantId = _tenantId,
            Page = 1,
            PageSize = 2
        };
        var (items, total) = await repo.GetPagedAsync(filter);

        items.Should().HaveCount(2);
        total.Should().Be(5);
    }
}
