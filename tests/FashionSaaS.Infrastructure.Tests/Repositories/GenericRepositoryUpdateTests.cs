using FashionSaaS.Application.Interfaces;
using FashionSaaS.Domain.Entities;
using FashionSaaS.Domain.Enums;
using FashionSaaS.Infrastructure.Persistence;
using FashionSaaS.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace FashionSaaS.Infrastructure.Tests.Repositories;

/// <summary>
/// Direct coverage for GenericRepository.UpdateAsync's tracked-duplicate branch: when a
/// caller passes a DETACHED entity instance whose Id matches an already-tracked entity
/// (e.g. fetched earlier via GetByIdAsync in the same DbContext/unit of work), UpdateAsync
/// must copy values onto the tracked instance via CurrentValues.SetValues rather than
/// attaching the detached instance directly - attaching a second instance with the same key
/// throws InvalidOperationException ("already being tracked").
/// </summary>
public class GenericRepositoryUpdateTests
{
    private readonly Guid _tenantId = Guid.NewGuid();

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
    public async Task UpdateAsync_DetachedInstanceWithSameIdAsTrackedEntity_UpdatesTrackedEntityValues()
    {
        await using ApplicationDbContext ctx = CreateContext();
        var discountId = Guid.NewGuid();
        var discount = new Discount
        {
            Id = discountId,
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

        // Fetch TRACKED (GenericRepository.GetByIdAsync uses DbSet.FindAsync, which tracks).
        Discount? tracked = await repo.GetByIdAsync(discountId);
        tracked.Should().NotBeNull();

        // A SEPARATE detached instance with the same Id but different scalar values -
        // simulating a caller that mutated a value fetched via an AsNoTracking query
        // elsewhere in the same unit of work.
        var detached = new Discount
        {
            Id = discountId,
            TenantId = _tenantId,
            Code = "SUMMER20",
            Type = DiscountType.Percentage,
            Value = 35,
            IsActive = false,
            StartsAt = discount.StartsAt,
            EndsAt = discount.EndsAt
        };

        Func<Task> act = async () =>
        {
            await repo.UpdateAsync(detached);
            await ctx.SaveChangesAsync();
        };

        await act.Should().NotThrowAsync();

        Discount persisted = await ctx.Discounts.AsNoTracking().SingleAsync(d => d.Id == discountId);
        persisted.Value.Should().Be(35);
        persisted.IsActive.Should().BeFalse();
    }
}
