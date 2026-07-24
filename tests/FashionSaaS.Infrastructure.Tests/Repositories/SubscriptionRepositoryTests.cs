using FashionSaaS.Application.Interfaces;
using FashionSaaS.Domain.Entities;
using FashionSaaS.Domain.Enums;
using FashionSaaS.Infrastructure.Persistence;
using FashionSaaS.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace FashionSaaS.Infrastructure.Tests.Repositories;

public class SubscriptionRepositoryTests
{
    private readonly Guid _tenantId = Guid.NewGuid();

    private static ApplicationDbContext CreateContext()
    {
        var currentTenant = new Mock<ICurrentTenantService>();
        currentTenant.Setup(c => c.TenantId).Returns((Guid?)null);

        DbContextOptions<ApplicationDbContext> options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options, currentTenant.Object);
    }

    [Fact]
    public async Task GetAllAsync_SubscriptionWithPlan_IncludesPlanNavigation()
    {
        await using ApplicationDbContext ctx = CreateContext();

        var plan = new SubscriptionPlan
        {
            PlanType = SubscriptionPlanType.Monthly,
            Name = "Pro Plan",
            Price = 49.99m,
            DurationDays = 30,
            IsActive = true
        };
        ctx.SubscriptionPlans.Add(plan);

        var subscription = new TenantSubscription
        {
            TenantId = _tenantId,
            PlanId = plan.Id,
            StartDate = DateTime.UtcNow,
            EndDate = DateTime.UtcNow.AddDays(30),
            Status = SubscriptionStatus.Active
        };
        ctx.TenantSubscriptions.Add(subscription);
        await ctx.SaveChangesAsync();

        var repo = new SubscriptionRepository(ctx);
        IReadOnlyList<TenantSubscription> results = await repo.GetAllAsync();

        TenantSubscription result = results.Should().ContainSingle().Subject;
        result.Plan.Should().NotBeNull();
        result.Plan.Name.Should().Be("Pro Plan");
        result.Plan.Price.Should().Be(49.99m);
    }
}
