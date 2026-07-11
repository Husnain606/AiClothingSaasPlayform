using FashionSaaS.Application.Interfaces;
using FashionSaaS.Application.Orders.DTOs;
using FashionSaaS.Domain.Entities;
using FashionSaaS.Domain.Enums;
using FashionSaaS.Infrastructure.Persistence;
using FashionSaaS.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace FashionSaaS.Infrastructure.Tests.Repositories;

public class OrderRepositoryTests
{
    private readonly Guid _tenantId = Guid.NewGuid();

    private ApplicationDbContext CreateContext()
    {
        var currentTenant = new Mock<ICurrentTenantService>();
        currentTenant.Setup(c => c.TenantId).Returns(_tenantId);
        DbContextOptions<ApplicationDbContext> options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new ApplicationDbContext(options, currentTenant.Object);
    }

    private static Order MakeOrder(Guid tenantId, string number = "ORD-2026-000001",
        OrderStatus status = OrderStatus.Pending, DateTime? date = null, string shippingEmail = "a@b.c") => new()
        {
            TenantId = tenantId,
            CustomerId = Guid.NewGuid(),
            OrderNumber = number,
            Status = status,
            OrderDate = date ?? DateTime.UtcNow,
            ShippingFirstName = "A",
            ShippingLastName = "B",
            ShippingEmail = shippingEmail,
            ShippingPhone = "1",
            ShippingStreet = "s",
            ShippingCity = "c",
            ShippingState = "st",
            ShippingZipCode = "z",
            ShippingCountry = "US",
            CardLast4 = "1111",
            Subtotal = 100m,
            Tax = 10m,
            ShippingCost = 0m,
            Total = 110m,
            Items = { new OrderItem { ProductId = Guid.NewGuid(), ProductName = "P", UnitPrice = 100m, Quantity = 1 } }
        };

    [Fact]
    public async Task GetByIdWithItemsAsync_ReturnsOrderWithItems()
    {
        await using ApplicationDbContext ctx = CreateContext();
        Order order = MakeOrder(_tenantId);
        ctx.Orders.Add(order);
        await ctx.SaveChangesAsync();

        var repo = new OrderRepository(ctx);
        Order? found = await repo.GetByIdWithItemsAsync(order.Id);

        found.Should().NotBeNull();
        found!.Items.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetPagedAsync_FiltersByStatusAndDateAndSearch()
    {
        await using ApplicationDbContext ctx = CreateContext();
        ctx.Orders.AddRange(
            MakeOrder(_tenantId, "ORD-2026-000001", OrderStatus.Pending, new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc)),
            MakeOrder(_tenantId, "ORD-2026-000002", OrderStatus.Shipped, new DateTime(2026, 2, 10, 0, 0, 0, DateTimeKind.Utc)),
            MakeOrder(_tenantId, "ORD-2026-000003", OrderStatus.Shipped, new DateTime(2026, 3, 10, 0, 0, 0, DateTimeKind.Utc)));
        await ctx.SaveChangesAsync();

        var repo = new OrderRepository(ctx);

        (IReadOnlyList<Order> Items, int TotalCount) byStatus = await repo.GetPagedAsync(new OrderFilter { TenantId = _tenantId, Status = OrderStatus.Shipped });
        byStatus.TotalCount.Should().Be(2);

        (IReadOnlyList<Order> Items, int TotalCount) byDate = await repo.GetPagedAsync(new OrderFilter
        { TenantId = _tenantId, From = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc), To = new DateTime(2026, 2, 28, 0, 0, 0, DateTimeKind.Utc) });
        byDate.TotalCount.Should().Be(1);

        (IReadOnlyList<Order> Items, int TotalCount) bySearch = await repo.GetPagedAsync(new OrderFilter { TenantId = _tenantId, Search = "000003" });
        bySearch.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task GetPagedAsync_TenantIsolation_ExcludesOtherTenants()
    {
        await using ApplicationDbContext ctx = CreateContext();
        ctx.Orders.Add(MakeOrder(Guid.NewGuid())); // other tenant
        await ctx.SaveChangesAsync();

        var repo = new OrderRepository(ctx);
        (IReadOnlyList<Order> Items, int TotalCount) result = await repo.GetPagedAsync(new OrderFilter { TenantId = _tenantId });

        result.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task GetPagedAsync_CustomerEmail_ReturnsOnlyMatchingOrders_WithCorrectTotalCount()
    {
        await using ApplicationDbContext ctx = CreateContext();
        ctx.Orders.AddRange(
            MakeOrder(_tenantId, "ORD-2026-000001", shippingEmail: "customer@example.com"),
            MakeOrder(_tenantId, "ORD-2026-000002", shippingEmail: "customer@example.com"),
            MakeOrder(_tenantId, "ORD-2026-000003", shippingEmail: "other@example.com"));
        await ctx.SaveChangesAsync();

        var repo = new OrderRepository(ctx);
        (IReadOnlyList<Order> Items, int TotalCount) result = await repo.GetPagedAsync(new OrderFilter
        { TenantId = _tenantId, CustomerEmail = "customer@example.com", Page = 1, PageSize = 1 });

        // TotalCount reflects all matching rows, not just the page returned.
        result.TotalCount.Should().Be(2);
        result.Items.Should().HaveCount(1);
        result.Items.Should().OnlyContain(o => o.ShippingEmail == "customer@example.com");
    }

    [Fact]
    public async Task CountForYearAsync_CountsOnlyTenantAndYear()
    {
        await using ApplicationDbContext ctx = CreateContext();
        ctx.Orders.AddRange(
            MakeOrder(_tenantId, "ORD-2026-000001", date: new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc)),
            MakeOrder(_tenantId, "ORD-2025-000001", date: new DateTime(2025, 5, 1, 0, 0, 0, DateTimeKind.Utc)));
        await ctx.SaveChangesAsync();

        var repo = new OrderRepository(ctx);
        (await repo.CountForYearAsync(_tenantId, 2026)).Should().Be(1);
    }
}
