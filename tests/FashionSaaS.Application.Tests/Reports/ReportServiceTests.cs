using FashionSaaS.Application.Common;
using FashionSaaS.Application.Interfaces;
using FashionSaaS.Application.Reports;
using FashionSaaS.Application.Reports.DTOs;
using FashionSaaS.Domain.Entities;
using FashionSaaS.Domain.Enums;
using FashionSaaS.Infrastructure.Persistence;
using FashionSaaS.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace FashionSaaS.Application.Tests.Reports;

/// <summary>
/// ReportService tests run against the REAL ReportRepository over an EF Core in-memory
/// ApplicationDbContext (only ICurrentTenantService is mocked) because the value under
/// test is the aggregate math itself. One fixed seeded dataset; every test asserts exact numbers.
/// </summary>
public class ReportServiceTests
{
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();

    // Stable ids used in assertions
    private readonly Guid _catTops = Guid.NewGuid();
    private readonly Guid _catBottoms = Guid.NewGuid();
    private readonly Guid _catDenim = Guid.NewGuid();
    private readonly Guid _p1 = Guid.NewGuid();  // T-Shirt (Tops)
    private readonly Guid _p2 = Guid.NewGuid();  // Jeans (Bottoms)
    private readonly Guid _p3 = Guid.NewGuid();  // Skinny Denim (Denim, child of Bottoms)
    private readonly Guid _v1 = Guid.NewGuid();  // stock 3, active  -> low stock
    private readonly Guid _v2 = Guid.NewGuid();  // stock 50, active
    private readonly Guid _v3 = Guid.NewGuid();  // stock 2, INACTIVE -> excluded
    private readonly Guid _c1 = Guid.NewGuid();  // 3 non-cancelled orders (repeat)
    private readonly Guid _c2 = Guid.NewGuid();  // 1 non-cancelled order

    private static readonly DateTime From = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime To = new(2026, 2, 28, 23, 59, 59, DateTimeKind.Utc);

    private static ApplicationDbContext CreateContext(Guid tenantId, string dbName)
    {
        var currentTenant = new Mock<ICurrentTenantService>();
        currentTenant.Setup(c => c.TenantId).Returns(tenantId);

        DbContextOptions<ApplicationDbContext> options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new ApplicationDbContext(options, currentTenant.Object);
    }

    private static ReportService CreateService(ApplicationDbContext ctx, Guid? tenantId)
    {
        var currentTenant = new Mock<ICurrentTenantService>();
        currentTenant.Setup(c => c.TenantId).Returns(tenantId);
        var logger = new Mock<ILogger<ReportService>>();
        return new ReportService(new ReportRepository(ctx), currentTenant.Object, logger.Object);
    }

    /// <summary>Seeds the fixed two-tenant dataset and returns a tenant-A service + context.</summary>
    private async Task<(ReportService Service, ApplicationDbContext Ctx, string DbName)> SetupAsync()
    {
        var dbName = Guid.NewGuid().ToString();
        ApplicationDbContext ctx = CreateContext(_tenantA, dbName);
        await SeedAsync(ctx);
        return (CreateService(ctx, _tenantA), ctx, dbName);
    }

    private async Task SeedAsync(ApplicationDbContext ctx)
    {
        // ---- Tenant A catalog ----
        ctx.Categories.AddRange(
            new Category { Id = _catTops, TenantId = _tenantA, Name = "Tops", Slug = "tops" },
            new Category { Id = _catBottoms, TenantId = _tenantA, Name = "Bottoms", Slug = "bottoms" },
            new Category { Id = _catDenim, TenantId = _tenantA, Name = "Denim", Slug = "denim", ParentCategoryId = _catBottoms });

        ctx.Products.AddRange(
            new Product { Id = _p1, TenantId = _tenantA, CategoryId = _catTops, Name = "T-Shirt", Slug = "t-shirt", BasePrice = 30m },
            new Product { Id = _p2, TenantId = _tenantA, CategoryId = _catBottoms, Name = "Jeans", Slug = "jeans", BasePrice = 40m },
            new Product { Id = _p3, TenantId = _tenantA, CategoryId = _catDenim, Name = "Skinny Denim", Slug = "skinny-denim", BasePrice = 50m });

        ctx.ProductVariants.AddRange(
            new ProductVariant { Id = _v1, TenantId = _tenantA, ProductId = _p1, Sku = "TS-S", Size = "S", Color = "Black", StockQuantity = 3, IsActive = true },
            new ProductVariant { Id = _v2, TenantId = _tenantA, ProductId = _p2, Sku = "JN-M", Size = "M", Color = "Blue", StockQuantity = 50, IsActive = true },
            new ProductVariant { Id = _v3, TenantId = _tenantA, ProductId = _p1, Sku = "TS-X", Size = "XL", Color = "Black", StockQuantity = 2, IsActive = false });

        ctx.Customers.AddRange(
            new Customer { Id = _c1, TenantId = _tenantA, FirstName = "Amal", LastName = "One", Email = "c1@shop.test", CreatedAt = new DateTime(2026, 1, 6, 0, 0, 0, DateTimeKind.Utc) },
            new Customer { Id = _c2, TenantId = _tenantA, FirstName = "Basim", LastName = "Two", Email = "c2@shop.test", CreatedAt = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc) });

        // ---- Tenant A orders (Jan: 3 orders incl. 1 cancelled; Feb: 2 orders) ----
        // 2026-01-05 is a Monday; Jan 7 = Wed, Jan 11 = Sun -> same Monday-start week bucket.
        ctx.Orders.AddRange(
            MakeOrder(_tenantA, _c1, "ORD-2026-000001", OrderStatus.Delivered, new DateTime(2026, 1, 7, 10, 0, 0, DateTimeKind.Utc), 100m,
                (_p1, "T-Shirt", 30m, 2), (_p2, "Jeans", 40m, 1)),
            MakeOrder(_tenantA, _c1, "ORD-2026-000002", OrderStatus.Confirmed, new DateTime(2026, 1, 11, 12, 0, 0, DateTimeKind.Utc), 50m,
                (_p1, "T-Shirt", 5m, 10)),
            MakeOrder(_tenantA, _c2, "ORD-2026-000003", OrderStatus.Cancelled, new DateTime(2026, 1, 20, 9, 0, 0, DateTimeKind.Utc), 999m,
                (_p1, "T-Shirt", 99.9m, 10)),
            MakeOrder(_tenantA, _c2, "ORD-2026-000004", OrderStatus.Pending, new DateTime(2026, 2, 3, 8, 0, 0, DateTimeKind.Utc), 200m,
                (_p2, "Jeans", 25m, 8)),
            MakeOrder(_tenantA, _c1, "ORD-2026-000005", OrderStatus.Delivered, new DateTime(2026, 2, 10, 15, 0, 0, DateTimeKind.Utc), 150m,
                (_p1, "T-Shirt", 50m, 2), (_p3, "Skinny Denim", 50m, 1)));

        // ---- Tenant A reviews: 1 pending, 1 approved ----
        ctx.Reviews.AddRange(
            new Review { TenantId = _tenantA, ProductId = _p1, CustomerId = _c1, Rating = 5, Status = ReviewStatus.Pending },
            new Review { TenantId = _tenantA, ProductId = _p1, CustomerId = _c2, Rating = 4, Status = ReviewStatus.Approved });

        // ---- Tenant A stock adjustments: two on Jan 7 (deltas -2, -3), one on Feb 3 (+5) ----
        ctx.StockAdjustments.AddRange(
            new StockAdjustment { TenantId = _tenantA, ProductVariantId = _v1, Delta = -2, ResultingQuantity = 3, Reason = StockAdjustmentReason.OrderPlaced, AdjustedByUserId = Guid.NewGuid(), CreatedAt = new DateTime(2026, 1, 7, 10, 0, 0, DateTimeKind.Utc) },
            new StockAdjustment { TenantId = _tenantA, ProductVariantId = _v2, Delta = -3, ResultingQuantity = 50, Reason = StockAdjustmentReason.OrderPlaced, AdjustedByUserId = Guid.NewGuid(), CreatedAt = new DateTime(2026, 1, 7, 10, 5, 0, DateTimeKind.Utc) },
            new StockAdjustment { TenantId = _tenantA, ProductVariantId = _v1, Delta = 5, ResultingQuantity = 8, Reason = StockAdjustmentReason.Restock, AdjustedByUserId = Guid.NewGuid(), CreatedAt = new DateTime(2026, 2, 3, 8, 0, 0, DateTimeKind.Utc) });

        // ---- Tenant B (must never leak into tenant A results) ----
        var catB = Guid.NewGuid();
        var pB = Guid.NewGuid();
        var vB = Guid.NewGuid();
        var cB = Guid.NewGuid();
        ctx.Categories.Add(new Category { Id = catB, TenantId = _tenantB, Name = "B-Cat", Slug = "b-cat" });
        ctx.Products.Add(new Product { Id = pB, TenantId = _tenantB, CategoryId = catB, Name = "B-Product", Slug = "b-product", BasePrice = 1000m });
        ctx.ProductVariants.Add(new ProductVariant { Id = vB, TenantId = _tenantB, ProductId = pB, Sku = "B-SKU", StockQuantity = 1, IsActive = true });
        ctx.Customers.Add(new Customer { Id = cB, TenantId = _tenantB, FirstName = "Other", LastName = "Tenant", Email = "b@other.test", CreatedAt = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc) });
        ctx.Orders.Add(MakeOrder(_tenantB, cB, "ORD-2026-000001", OrderStatus.Delivered, new DateTime(2026, 1, 15, 11, 0, 0, DateTimeKind.Utc), 1000m,
            (pB, "B-Product", 1000m, 1)));
        ctx.Reviews.Add(new Review { TenantId = _tenantB, ProductId = pB, CustomerId = cB, Rating = 1, Status = ReviewStatus.Pending });
        ctx.StockAdjustments.Add(new StockAdjustment { TenantId = _tenantB, ProductVariantId = vB, Delta = -1, ResultingQuantity = 1, Reason = StockAdjustmentReason.OrderPlaced, AdjustedByUserId = Guid.NewGuid(), CreatedAt = new DateTime(2026, 1, 15, 11, 0, 0, DateTimeKind.Utc) });

        await ctx.SaveChangesAsync();
    }

    private static Order MakeOrder(Guid tenantId, Guid customerId, string number, OrderStatus status, DateTime date, decimal total,
        params (Guid ProductId, string Name, decimal UnitPrice, int Qty)[] items)
    {
        var order = new Order
        {
            TenantId = tenantId,
            CustomerId = customerId,
            OrderNumber = number,
            Status = status,
            OrderDate = date,
            Subtotal = total,
            Total = total
        };
        foreach ((Guid productId, var name, var price, var qty) in items)
            order.Items.Add(new OrderItem { ProductId = productId, ProductName = name, UnitPrice = price, Quantity = qty });
        return order;
    }

    // ---------------------------------------------------------------- Summary

    [Fact]
    public async Task Summary_ExcludesCancelledRevenue()
    {
        (ReportService? service, ApplicationDbContext? ctx, var _) = await SetupAsync();
        await using ApplicationDbContext _ctx = ctx;

        ResponseData<SummaryReportDto> result = await service.GetSummaryAsync(From, To);

        result.IsSuccess.Should().BeTrue();
        SummaryReportDto s = result.Data!;
        s.Revenue.Should().Be(500m);          // 100 + 50 + 200 + 150 (999 cancelled excluded)
        s.OrderCount.Should().Be(4);          // cancelled excluded
        s.AvgOrderValue.Should().Be(125m);    // 500 / 4
        s.NewCustomers.Should().Be(2);
        s.PendingReviews.Should().Be(1);
        s.LowStockCount.Should().Be(1);       // only V1 (stock 3, active); inactive V3 excluded
    }

    [Fact]
    public async Task Summary_AvgOrderValue_ZeroWhenNoOrders()
    {
        (ReportService? service, ApplicationDbContext? ctx, var _) = await SetupAsync();
        await using ApplicationDbContext _ctx = ctx;

        ResponseData<SummaryReportDto> result = await service.GetSummaryAsync(
            new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 6, 30, 0, 0, 0, DateTimeKind.Utc));

        result.IsSuccess.Should().BeTrue();
        result.Data!.Revenue.Should().Be(0m);
        result.Data.OrderCount.Should().Be(0);
        result.Data.AvgOrderValue.Should().Be(0m); // no divide-by-zero
    }

    // ---------------------------------------------------------------- Sales over time

    [Fact]
    public async Task SalesOverTime_MonthBuckets_CorrectTotals()
    {
        (ReportService? service, ApplicationDbContext? ctx, var _) = await SetupAsync();
        await using ApplicationDbContext _ctx = ctx;

        ResponseData<List<SalesPointDto>> result = await service.GetSalesOverTimeAsync(From, To, ReportInterval.Month);

        result.IsSuccess.Should().BeTrue();
        List<SalesPointDto> points = result.Data!;
        points.Should().HaveCount(2);
        points[0].PeriodStart.Should().Be(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        points[0].Revenue.Should().Be(150m);  // 100 + 50 (cancelled 999 excluded)
        points[0].OrderCount.Should().Be(2);
        points[1].PeriodStart.Should().Be(new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc));
        points[1].Revenue.Should().Be(350m);  // 200 + 150
        points[1].OrderCount.Should().Be(2);
    }

    [Fact]
    public async Task SalesOverTime_WeekBuckets_MondayStart()
    {
        (ReportService? service, ApplicationDbContext? ctx, var _) = await SetupAsync();
        await using ApplicationDbContext _ctx = ctx;

        ResponseData<List<SalesPointDto>> result = await service.GetSalesOverTimeAsync(From, To, ReportInterval.Week);

        result.IsSuccess.Should().BeTrue();
        List<SalesPointDto> points = result.Data!;
        // Jan 7 (Wed) and Jan 11 (Sun) both fall in the Monday-start week of Jan 5.
        points.Should().HaveCount(3);
        points[0].PeriodStart.Should().Be(new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc));
        points[0].Revenue.Should().Be(150m);
        points[0].OrderCount.Should().Be(2);
        points[1].PeriodStart.Should().Be(new DateTime(2026, 2, 2, 0, 0, 0, DateTimeKind.Utc));  // Feb 3 is a Tuesday
        points[1].Revenue.Should().Be(200m);
        points[1].OrderCount.Should().Be(1);
        points[2].PeriodStart.Should().Be(new DateTime(2026, 2, 9, 0, 0, 0, DateTimeKind.Utc));  // Feb 10 is a Tuesday
        points[2].Revenue.Should().Be(150m);
        points[2].OrderCount.Should().Be(1);
    }

    // ---------------------------------------------------------------- Top products

    [Fact]
    public async Task TopProducts_ByUnits_OrdersCorrectly()
    {
        (ReportService? service, ApplicationDbContext? ctx, var _) = await SetupAsync();
        await using ApplicationDbContext _ctx = ctx;

        // P1: units 2+10+2 = 14, revenue 60+50+100 = 210
        // P2: units 1+8 = 9,     revenue 40+200 = 240
        // P3: units 1,           revenue 50
        ResponseData<List<TopProductDto>> byUnits = await service.GetTopProductsAsync(From, To, 10, "units");
        byUnits.IsSuccess.Should().BeTrue();
        byUnits.Data![0].ProductId.Should().Be(_p1);
        byUnits.Data[0].Units.Should().Be(14);
        byUnits.Data[0].Revenue.Should().Be(210m);
        byUnits.Data[1].ProductId.Should().Be(_p2);
        byUnits.Data[1].Units.Should().Be(9);

        ResponseData<List<TopProductDto>> byRevenue = await service.GetTopProductsAsync(From, To, 10, "revenue");
        byRevenue.IsSuccess.Should().BeTrue();
        byRevenue.Data![0].ProductId.Should().Be(_p2);   // 240 > 210
        byRevenue.Data[0].Revenue.Should().Be(240m);
        byRevenue.Data[0].ProductName.Should().Be("Jeans");
        byRevenue.Data[1].ProductId.Should().Be(_p1);
        byRevenue.Data.Should().HaveCount(3);

        ResponseData<List<TopProductDto>> takeOne = await service.GetTopProductsAsync(From, To, 1, "revenue");
        takeOne.Data.Should().HaveCount(1);
    }

    [Fact]
    public async Task TopProducts_InvalidBy_Returns400()
    {
        (ReportService? service, ApplicationDbContext? ctx, var _) = await SetupAsync();
        await using ApplicationDbContext _ctx = ctx;

        ResponseData<List<TopProductDto>> result = await service.GetTopProductsAsync(From, To, 10, "price");

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
    }

    // ---------------------------------------------------------------- Status breakdown

    [Fact]
    public async Task StatusBreakdown_CountsPerStatus()
    {
        (ReportService? service, ApplicationDbContext? ctx, var _) = await SetupAsync();
        await using ApplicationDbContext _ctx = ctx;

        ResponseData<List<StatusBreakdownDto>> result = await service.GetStatusBreakdownAsync(From, To);

        result.IsSuccess.Should().BeTrue();
        List<StatusBreakdownDto> rows = result.Data!;
        rows.Should().HaveCount(4);
        rows.Single(r => string.Equals(r.Status, "Pending", StringComparison.Ordinal)).Count.Should().Be(1);
        rows.Single(r => string.Equals(r.Status, "Pending", StringComparison.Ordinal)).Revenue.Should().Be(200m);
        rows.Single(r => string.Equals(r.Status, "Confirmed", StringComparison.Ordinal)).Count.Should().Be(1);
        rows.Single(r => string.Equals(r.Status, "Confirmed", StringComparison.Ordinal)).Revenue.Should().Be(50m);
        rows.Single(r => string.Equals(r.Status, "Delivered", StringComparison.Ordinal)).Count.Should().Be(2);
        rows.Single(r => string.Equals(r.Status, "Delivered", StringComparison.Ordinal)).Revenue.Should().Be(250m);
        rows.Single(r => string.Equals(r.Status, "Cancelled", StringComparison.Ordinal)).Count.Should().Be(1);   // breakdown reports all statuses
        rows.Single(r => string.Equals(r.Status, "Cancelled", StringComparison.Ordinal)).Revenue.Should().Be(999m);
    }

    // ---------------------------------------------------------------- Customer analytics

    [Fact]
    public async Task CustomerAnalytics_RepeatRate_Exact()
    {
        (ReportService? service, ApplicationDbContext? ctx, var _) = await SetupAsync();
        await using ApplicationDbContext _ctx = ctx;

        ResponseData<CustomerAnalyticsDto> result = await service.GetCustomerAnalyticsAsync(From, To, ReportInterval.Month);

        result.IsSuccess.Should().BeTrue();
        CustomerAnalyticsDto a = result.Data!;
        // C1 has 3 non-cancelled orders, C2 has 1 -> 1 of 2 customers repeat
        a.RepeatPurchaseRate.Should().Be(0.5);

        a.TopCustomers.Should().HaveCount(2);
        a.TopCustomers[0].CustomerId.Should().Be(_c1);
        a.TopCustomers[0].Email.Should().Be("c1@shop.test");
        a.TopCustomers[0].TotalSpend.Should().Be(300m);  // 100 + 50 + 150
        a.TopCustomers[0].OrderCount.Should().Be(3);
        a.TopCustomers[1].TotalSpend.Should().Be(200m);

        a.NewCustomersOverTime.Should().HaveCount(2);
        a.NewCustomersOverTime[0].PeriodStart.Should().Be(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        a.NewCustomersOverTime[0].OrderCount.Should().Be(1);
        a.NewCustomersOverTime[1].PeriodStart.Should().Be(new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc));
        a.NewCustomersOverTime[1].OrderCount.Should().Be(1);
    }

    // ---------------------------------------------------------------- Inventory trends

    [Fact]
    public async Task InventoryTrends_LowStockThreshold5()
    {
        (ReportService? service, ApplicationDbContext? ctx, var _) = await SetupAsync();
        await using ApplicationDbContext _ctx = ctx;

        ResponseData<InventoryTrendsDto> result = await service.GetInventoryTrendsAsync(From, To);

        result.IsSuccess.Should().BeTrue();
        List<LowStockItemDto> low = result.Data!.LowStock;
        low.Should().HaveCount(1);            // V1 only: stock 3 <= 5 and active; V3 inactive excluded
        low[0].VariantId.Should().Be(_v1);
        low[0].Sku.Should().Be("TS-S");
        low[0].ProductName.Should().Be("T-Shirt");
        low[0].StockQuantity.Should().Be(3);
    }

    [Fact]
    public async Task InventoryTrends_AdjustmentsBucketedByDay()
    {
        (ReportService? service, ApplicationDbContext? ctx, var _) = await SetupAsync();
        await using ApplicationDbContext _ctx = ctx;

        ResponseData<InventoryTrendsDto> result = await service.GetInventoryTrendsAsync(From, To);

        result.IsSuccess.Should().BeTrue();
        List<SalesPointDto> points = result.Data!.AdjustmentsOverTime;
        // OrderCount = number of adjustments in the day; Revenue = sum of |Delta|
        points.Should().HaveCount(2);
        points[0].PeriodStart.Should().Be(new DateTime(2026, 1, 7, 0, 0, 0, DateTimeKind.Utc));
        points[0].OrderCount.Should().Be(2);
        points[0].Revenue.Should().Be(5m);    // |-2| + |-3|
        points[1].PeriodStart.Should().Be(new DateTime(2026, 2, 3, 0, 0, 0, DateTimeKind.Utc));
        points[1].OrderCount.Should().Be(1);
        points[1].Revenue.Should().Be(5m);    // |+5|
    }

    // ---------------------------------------------------------------- Category sales

    [Fact]
    public async Task CategorySales_RollsUpDirectProducts()
    {
        (ReportService? service, ApplicationDbContext? ctx, var _) = await SetupAsync();
        await using ApplicationDbContext _ctx = ctx;

        ResponseData<List<CategorySalesDto>> result = await service.GetCategorySalesAsync(From, To, null);

        result.IsSuccess.Should().BeTrue();
        List<CategorySalesDto> rows = result.Data!;
        // Top-level categories only; each counts DIRECT products only.
        rows.Should().HaveCount(2);
        rows.Should().NotContain(r => r.CategoryId == _catDenim);

        CategorySalesDto bottoms = rows.Single(r => r.CategoryId == _catBottoms);
        bottoms.CategoryName.Should().Be("Bottoms");
        bottoms.Revenue.Should().Be(240m);    // P2 only — P3 (child Denim) NOT rolled up
        bottoms.Units.Should().Be(9);

        CategorySalesDto tops = rows.Single(r => r.CategoryId == _catTops);
        tops.Revenue.Should().Be(210m);
        tops.Units.Should().Be(14);
    }

    [Fact]
    public async Task CategorySales_DrillDown_ReturnsChildren()
    {
        (ReportService? service, ApplicationDbContext? ctx, var _) = await SetupAsync();
        await using ApplicationDbContext _ctx = ctx;

        ResponseData<List<CategorySalesDto>> result = await service.GetCategorySalesAsync(From, To, _catBottoms);

        result.IsSuccess.Should().BeTrue();
        List<CategorySalesDto> rows = result.Data!;
        rows.Should().HaveCount(1);
        rows[0].CategoryId.Should().Be(_catDenim);
        rows[0].CategoryName.Should().Be("Denim");
        rows[0].Revenue.Should().Be(50m);     // P3 x1 @50 in O5
        rows[0].Units.Should().Be(1);
    }

    // ---------------------------------------------------------------- Tenant isolation

    [Fact]
    public async Task TenantIsolation_OtherTenantExcluded()
    {
        (ReportService? serviceA, ApplicationDbContext? ctx, var dbName) = await SetupAsync();
        await using ApplicationDbContext _ctx = ctx;

        // Tenant A must not see tenant B's 1000 order / customer / review / variant.
        ResponseData<SummaryReportDto> summaryA = await serviceA.GetSummaryAsync(From, To);
        summaryA.Data!.Revenue.Should().Be(500m);
        summaryA.Data.OrderCount.Should().Be(4);
        summaryA.Data.NewCustomers.Should().Be(2);
        summaryA.Data.PendingReviews.Should().Be(1);
        summaryA.Data.LowStockCount.Should().Be(1);

        // A tenant-B scoped context/service sees ONLY tenant B's data.
        await using ApplicationDbContext ctxB = CreateContext(_tenantB, dbName);
        ReportService serviceB = CreateService(ctxB, _tenantB);
        ResponseData<SummaryReportDto> summaryB = await serviceB.GetSummaryAsync(From, To);
        summaryB.Data!.Revenue.Should().Be(1000m);
        summaryB.Data.OrderCount.Should().Be(1);
        summaryB.Data.NewCustomers.Should().Be(1);
        summaryB.Data.PendingReviews.Should().Be(1);
        summaryB.Data.LowStockCount.Should().Be(1);
    }

    // S125 false positive: prose section header, not commented-out code.
#pragma warning disable S125
    // ---------------------------------------------------------------- Range validation (guard shared by all 7 methods;
    // exercised via two representative methods)
#pragma warning restore S125

    [Fact]
    public async Task Range_FromAfterTo_Returns400()
    {
        (ReportService? service, ApplicationDbContext? ctx, var _) = await SetupAsync();
        await using ApplicationDbContext _ctx = ctx;

        ResponseData<SummaryReportDto> result = await service.GetSummaryAsync(To, From);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Range_DefaultDateTime_Returns400()
    {
        // Regression test: an omitted (not just malformed) 'from'/'to' query parameter binds to
        // default(DateTime) rather than failing model validation - this must be rejected
        // explicitly rather than silently returning an empty/zero report as a false "success".
        (ReportService? service, ApplicationDbContext? ctx, var _) = await SetupAsync();
        await using ApplicationDbContext _ctx = ctx;

        ResponseData<SummaryReportDto> result = await service.GetSummaryAsync(default, default);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Range_Over366Days_Returns400()
    {
        (ReportService? service, ApplicationDbContext? ctx, var _) = await SetupAsync();
        await using ApplicationDbContext _ctx = ctx;

        ResponseData<List<SalesPointDto>> result = await service.GetSalesOverTimeAsync(
            new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            ReportInterval.Day);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task UnresolvedTenant_Returns400()
    {
        (ReportService _, ApplicationDbContext? ctx, var _) = await SetupAsync();
        await using ApplicationDbContext _ctx = ctx;
        ReportService service = CreateService(ctx, tenantId: null);

        ResponseData<SummaryReportDto> result = await service.GetSummaryAsync(From, To);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
    }
}
