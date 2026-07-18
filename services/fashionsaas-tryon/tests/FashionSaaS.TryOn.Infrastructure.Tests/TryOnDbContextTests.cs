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

    [Fact]
    public async Task SaveChangesAsync_PersistsMeasurementRequest()
    {
        await using TryOnDbContext ctx = CreateContext();
        var request = new MeasurementRequest
        {
            TenantId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            Status = MeasurementStatus.Completed,
            ChestCm = 96.5m,
            RecommendedSize = SizeCode.M
        };

        ctx.MeasurementRequests.Add(request);
        await ctx.SaveChangesAsync();

        MeasurementRequest? saved = await ctx.MeasurementRequests.FindAsync(request.Id);
        saved.Should().NotBeNull();
        saved!.Status.Should().Be(MeasurementStatus.Completed);
        saved.ChestCm.Should().Be(96.5m);
        saved.RecommendedSize.Should().Be(SizeCode.M);
    }

    [Fact]
    public async Task MeasurementRequests_QueryByTenantAndStatus_ReturnsOnlyMatching()
    {
        await using TryOnDbContext ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        ctx.MeasurementRequests.AddRange(
            new MeasurementRequest { TenantId = tenantId, CustomerId = Guid.NewGuid(), Status = MeasurementStatus.Completed },
            new MeasurementRequest { TenantId = tenantId, CustomerId = Guid.NewGuid(), Status = MeasurementStatus.Failed },
            new MeasurementRequest { TenantId = Guid.NewGuid(), CustomerId = Guid.NewGuid(), Status = MeasurementStatus.Completed });
        await ctx.SaveChangesAsync();

        var count = await ctx.MeasurementRequests
            .Where(m => m.TenantId == tenantId && m.Status == MeasurementStatus.Completed)
            .CountAsync();

        count.Should().Be(1);
    }

    [Fact]
    public async Task SaveChangesAsync_PersistsChatRequest()
    {
        await using TryOnDbContext ctx = CreateContext();
        var request = new ChatRequest
        {
            TenantId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            Status = ChatRequestStatus.Completed,
            MessageLength = 42,
            ReplyLength = 120,
            HadProductContext = true
        };

        ctx.ChatRequests.Add(request);
        await ctx.SaveChangesAsync();

        ChatRequest? saved = await ctx.ChatRequests.FindAsync(request.Id);
        saved.Should().NotBeNull();
        saved!.Status.Should().Be(ChatRequestStatus.Completed);
        saved.MessageLength.Should().Be(42);
        saved.ReplyLength.Should().Be(120);
        saved.HadProductContext.Should().BeTrue();
    }

    [Fact]
    public async Task ChatRequests_QueryByTenantAndStatus_ReturnsOnlyMatching()
    {
        await using TryOnDbContext ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        ctx.ChatRequests.AddRange(
            new ChatRequest { TenantId = tenantId, CustomerId = Guid.NewGuid(), Status = ChatRequestStatus.Completed },
            new ChatRequest { TenantId = tenantId, CustomerId = Guid.NewGuid(), Status = ChatRequestStatus.Failed },
            new ChatRequest { TenantId = Guid.NewGuid(), CustomerId = Guid.NewGuid(), Status = ChatRequestStatus.Completed });
        await ctx.SaveChangesAsync();

        var count = await ctx.ChatRequests
            .Where(c => c.TenantId == tenantId && c.Status == ChatRequestStatus.Completed)
            .CountAsync();

        count.Should().Be(1);
    }
}
