using FashionSaaS.TryOn.Domain;
using FashionSaaS.TryOn.Infrastructure.Persistence;
using FashionSaaS.TryOn.Infrastructure.Quota;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace FashionSaaS.TryOn.Infrastructure.Tests.Quota;

public class UsageQuotaServiceTests
{
    private readonly Guid _tenantId = Guid.NewGuid();

    private static TryOnDbContext CreateContext()
    {
        DbContextOptions<TryOnDbContext> options = new DbContextOptionsBuilder<TryOnDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new TryOnDbContext(options);
    }

    private static TryOnRequest CreateTryOnRequest(Guid tenantId, TryOnStatus status, DateTime? createdAt = null) =>
        new()
        {
            TenantId = tenantId,
            CustomerId = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            Status = status,
            CreatedAt = createdAt ?? DateTime.UtcNow
        };

    [Fact]
    public async Task UsageQuotaService_GetUsedThisMonthAsync_SumsTryOnRequestsOnlyForTenant()
    {
        await using TryOnDbContext ctx = CreateContext();
        ctx.TryOnRequests.Add(CreateTryOnRequest(_tenantId, TryOnStatus.Completed));
        await ctx.SaveChangesAsync();
        UsageQuotaService service = new(ctx);

        var count = await service.GetUsedThisMonthAsync(_tenantId, CancellationToken.None);

        count.Should().Be(1);
    }

    [Fact]
    public async Task UsageQuotaService_GetUsedThisMonthAsync_ExcludesOtherTenants()
    {
        await using TryOnDbContext ctx = CreateContext();
        ctx.TryOnRequests.Add(CreateTryOnRequest(_tenantId, TryOnStatus.Completed));
        ctx.TryOnRequests.Add(CreateTryOnRequest(Guid.NewGuid(), TryOnStatus.Completed));
        await ctx.SaveChangesAsync();
        UsageQuotaService service = new(ctx);

        var count = await service.GetUsedThisMonthAsync(_tenantId, CancellationToken.None);

        count.Should().Be(1);
    }

    [Fact]
    public async Task UsageQuotaService_GetUsedThisMonthAsync_ExcludesFailedRows()
    {
        await using TryOnDbContext ctx = CreateContext();
        ctx.TryOnRequests.Add(CreateTryOnRequest(_tenantId, TryOnStatus.Completed));
        ctx.TryOnRequests.Add(CreateTryOnRequest(_tenantId, TryOnStatus.Failed));
        await ctx.SaveChangesAsync();
        UsageQuotaService service = new(ctx);

        var count = await service.GetUsedThisMonthAsync(_tenantId, CancellationToken.None);

        count.Should().Be(1);
    }

    [Fact]
    public async Task UsageQuotaService_GetUsedThisMonthAsync_ExcludesRowsBeforeStartOfMonth()
    {
        await using TryOnDbContext ctx = CreateContext();
        ctx.TryOnRequests.Add(CreateTryOnRequest(_tenantId, TryOnStatus.Completed));
        ctx.TryOnRequests.Add(CreateTryOnRequest(_tenantId, TryOnStatus.Completed, createdAt: DateTime.UtcNow.AddMonths(-1)));
        await ctx.SaveChangesAsync();
        UsageQuotaService service = new(ctx);

        var count = await service.GetUsedThisMonthAsync(_tenantId, CancellationToken.None);

        count.Should().Be(1);
    }

    [Fact]
    public async Task UsageQuotaService_GetUsedThisMonthAsync_SumsTryOnAndMeasurementForTenant()
    {
        await using TryOnDbContext ctx = CreateContext();
        ctx.TryOnRequests.Add(CreateTryOnRequest(_tenantId, TryOnStatus.Completed));
        ctx.MeasurementRequests.Add(new MeasurementRequest
        {
            TenantId = _tenantId,
            CustomerId = Guid.NewGuid(),
            Status = MeasurementStatus.Completed,
            CreatedAt = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();
        UsageQuotaService service = new(ctx);

        var count = await service.GetUsedThisMonthAsync(_tenantId, CancellationToken.None);

        count.Should().Be(2);
    }
}
