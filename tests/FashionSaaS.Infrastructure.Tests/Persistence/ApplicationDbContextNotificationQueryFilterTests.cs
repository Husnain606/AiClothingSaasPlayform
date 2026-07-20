using FashionSaaS.Application.Interfaces;
using FashionSaaS.Domain.Entities;
using FashionSaaS.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace FashionSaaS.Infrastructure.Tests.Persistence;

public class ApplicationDbContextNotificationQueryFilterTests
{
    private static ApplicationDbContext CreateContext(Guid? tenantId, string databaseName)
    {
        var currentTenant = new Mock<ICurrentTenantService>();
        currentTenant.Setup(c => c.TenantId).Returns(tenantId);

        DbContextOptions<ApplicationDbContext> options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;
        return new ApplicationDbContext(options, currentTenant.Object);
    }

    [Fact]
    public async Task ApplicationDbContext_Notification_QueryFilter_ScopesToTenantOrBroadcast()
    {
        var tenant1Id = Guid.NewGuid();
        var tenant2Id = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString();

        await using (ApplicationDbContext seedCtx = CreateContext(tenant1Id, databaseName))
        {
            seedCtx.Notifications.AddRange(
                new Notification { TenantId = tenant1Id, Title = "Tenant 1 notification", Message = "m", EntityName = "Order", EntityId = Guid.NewGuid() },
                new Notification { TenantId = tenant2Id, Title = "Tenant 2 notification", Message = "m", EntityName = "Order", EntityId = Guid.NewGuid() },
                new Notification { TenantId = null, Title = "Platform notification", Message = "m", EntityName = "Order", EntityId = Guid.NewGuid() });
            await seedCtx.SaveChangesAsync();
        }

        await using ApplicationDbContext tenant1Ctx = CreateContext(tenant1Id, databaseName);
        List<Notification> tenant1Results = await tenant1Ctx.Notifications.AsNoTracking().ToListAsync();

        tenant1Results.Should().ContainSingle().Which.TenantId.Should().Be(tenant1Id);

        await using ApplicationDbContext superAdminCtx = CreateContext(null, databaseName);
        List<Notification> superAdminResults = await superAdminCtx.Notifications.AsNoTracking().ToListAsync();

        superAdminResults.Should().ContainSingle().Which.TenantId.Should().BeNull();
    }
}
