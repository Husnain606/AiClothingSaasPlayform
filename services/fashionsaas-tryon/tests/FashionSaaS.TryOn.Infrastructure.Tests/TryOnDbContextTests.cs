using FashionSaaS.TryOn.Domain;
using FashionSaaS.TryOn.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace FashionSaaS.TryOn.Infrastructure.Tests;

public class TryOnDbContextTests
{
    private static TryOnDbContext CreateContext()
    {
        DbContextOptions<TryOnDbContext> options = new DbContextOptionsBuilder<TryOnDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new TryOnDbContext(options);
    }

    [Fact]
    public async Task SaveChangesAsync_PersistsTryOnRequest()
    {
        await using TryOnDbContext ctx = CreateContext();
        var request = new TryOnRequest
        {
            TenantId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            Status = TryOnStatus.Completed
        };

        ctx.TryOnRequests.Add(request);
        await ctx.SaveChangesAsync();

        TryOnRequest? saved = await ctx.TryOnRequests.FindAsync(request.Id);
        saved.Should().NotBeNull();
        saved!.Status.Should().Be(TryOnStatus.Completed);
    }

    [Fact]
    public async Task TryOnRequests_QueryByTenantAndStatus_ReturnsOnlyMatching()
    {
        await using TryOnDbContext ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        ctx.TryOnRequests.AddRange(
            new TryOnRequest { TenantId = tenantId, CustomerId = Guid.NewGuid(), ProductId = Guid.NewGuid(), Status = TryOnStatus.Completed },
            new TryOnRequest { TenantId = tenantId, CustomerId = Guid.NewGuid(), ProductId = Guid.NewGuid(), Status = TryOnStatus.Failed },
            new TryOnRequest { TenantId = Guid.NewGuid(), CustomerId = Guid.NewGuid(), ProductId = Guid.NewGuid(), Status = TryOnStatus.Completed });
        await ctx.SaveChangesAsync();

        var count = await ctx.TryOnRequests
            .Where(t => t.TenantId == tenantId && t.Status == TryOnStatus.Completed)
            .CountAsync();

        count.Should().Be(1);
    }
}
