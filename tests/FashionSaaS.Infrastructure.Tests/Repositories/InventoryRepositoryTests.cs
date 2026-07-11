using FashionSaaS.Application.Interfaces;
using FashionSaaS.Domain.Entities;
using FashionSaaS.Domain.Enums;
using FashionSaaS.Infrastructure.Persistence;
using FashionSaaS.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace FashionSaaS.Infrastructure.Tests.Repositories;

public class InventoryRepositoryTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _variantId = Guid.NewGuid();

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
    public async Task GetByVariantAsync_VariantWithStockAdjustments_ReturnsAllAdjustments()
    {
        await using ApplicationDbContext ctx = CreateContext();
        var adjustment1 = new StockAdjustment
        {
            TenantId = _tenantId,
            ProductVariantId = _variantId,
            Delta = 100,
            Reason = StockAdjustmentReason.Restock,
            ResultingQuantity = 100,
            AdjustedByUserId = Guid.NewGuid()
        };
        var adjustment2 = new StockAdjustment
        {
            TenantId = _tenantId,
            ProductVariantId = _variantId,
            Delta = -10,
            Reason = StockAdjustmentReason.Sale,
            ResultingQuantity = 90,
            AdjustedByUserId = Guid.NewGuid()
        };
        ctx.StockAdjustments.AddRange(adjustment1, adjustment2);
        await ctx.SaveChangesAsync();

        var repo = new StockAdjustmentRepository(ctx);
        IReadOnlyList<StockAdjustment> result = await repo.GetByVariantAsync(_variantId);

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetByVariantAsync_VariantWithNoAdjustments_ReturnsEmptyList()
    {
        await using ApplicationDbContext ctx = CreateContext();

        var repo = new StockAdjustmentRepository(ctx);
        IReadOnlyList<StockAdjustment> result = await repo.GetByVariantAsync(_variantId);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetByVariantAsync_FiltersToSpecificVariant_ExcludesOtherVariants()
    {
        await using ApplicationDbContext ctx = CreateContext();
        var otherVariantId = Guid.NewGuid();
        var adjustment1 = new StockAdjustment
        {
            TenantId = _tenantId,
            ProductVariantId = _variantId,
            Delta = 50,
            Reason = StockAdjustmentReason.Restock,
            ResultingQuantity = 50,
            AdjustedByUserId = Guid.NewGuid()
        };
        var adjustment2 = new StockAdjustment
        {
            TenantId = _tenantId,
            ProductVariantId = otherVariantId,
            Delta = 30,
            Reason = StockAdjustmentReason.Correction,
            ResultingQuantity = 30,
            AdjustedByUserId = Guid.NewGuid()
        };
        ctx.StockAdjustments.AddRange(adjustment1, adjustment2);
        await ctx.SaveChangesAsync();

        var repo = new StockAdjustmentRepository(ctx);
        IReadOnlyList<StockAdjustment> result = await repo.GetByVariantAsync(_variantId);

        result.Should().HaveCount(1);
        result[0].Delta.Should().Be(50);
    }

    [Fact]
    public async Task GetByVariantAsync_MultipleAdjustments_ReturnsOrderedByDateDescending()
    {
        await using ApplicationDbContext ctx = CreateContext();
        var adjustment1 = new StockAdjustment
        {
            TenantId = _tenantId,
            ProductVariantId = _variantId,
            Delta = 100,
            Reason = StockAdjustmentReason.Restock,
            ResultingQuantity = 100,
            AdjustedByUserId = Guid.NewGuid()
        };
        ctx.StockAdjustments.Add(adjustment1);
        await ctx.SaveChangesAsync();

        var adjustment2 = new StockAdjustment
        {
            TenantId = _tenantId,
            ProductVariantId = _variantId,
            Delta = -5,
            Reason = StockAdjustmentReason.Sale,
            ResultingQuantity = 95,
            AdjustedByUserId = Guid.NewGuid()
        };
        ctx.StockAdjustments.Add(adjustment2);
        await ctx.SaveChangesAsync();

        var repo = new StockAdjustmentRepository(ctx);
        IReadOnlyList<StockAdjustment> result = await repo.GetByVariantAsync(_variantId);

        result.Should().HaveCount(2);
        result.Should().BeInDescendingOrder(x => x.CreatedAt);
    }
}
