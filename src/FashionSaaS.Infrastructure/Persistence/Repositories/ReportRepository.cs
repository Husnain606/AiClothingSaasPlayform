using FashionSaaS.Application.Interfaces;
using FashionSaaS.Application.Reports.DTOs;
using FashionSaaS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FashionSaaS.Infrastructure.Persistence.Repositories;

/// <summary>
/// Tenant-scoped reporting aggregates.
///
/// Aggregation strategy: time-bucketed queries use a two-step pattern — filter and
/// project the minimal columns in SQL (<c>AsNoTracking</c>), then bucket + sum in memory —
/// because the Monday-start week calculation is not translatable by EF. This is an
/// explicit, documented trade-off: acceptable at current per-tenant scale and it keeps
/// the bucketing math in one testable helper. Non-bucketed aggregates (status, product,
/// customer, category GroupBy) remain fully translatable LINQ.
/// </summary>
public class ReportRepository(ApplicationDbContext context) : IReportRepository
{
    private const int LowStockThreshold = 5;

    public async Task<SummaryReportDto> GetSummaryAsync(Guid tenantId, DateTime from, DateTime to, CancellationToken ct = default)
    {
        var totals = await NonCancelledOrders(tenantId, from, to)
            .Select(o => o.Total)
            .ToListAsync(ct);

        var revenue = totals.Sum();
        var orderCount = totals.Count;

        var newCustomers = await context.Customers.AsNoTracking()
            .CountAsync(c => c.TenantId == tenantId && c.CreatedAt >= from && c.CreatedAt <= to, ct);

        var pendingReviews = await context.Reviews.AsNoTracking()
            .CountAsync(r => r.TenantId == tenantId && r.Status == ReviewStatus.Pending, ct);

        var lowStockCount = await context.ProductVariants.AsNoTracking()
            .CountAsync(v => v.TenantId == tenantId && v.IsActive && v.StockQuantity <= LowStockThreshold, ct);

        return new SummaryReportDto
        {
            Revenue = revenue,
            OrderCount = orderCount,
            AvgOrderValue = orderCount == 0 ? 0m : revenue / orderCount,
            NewCustomers = newCustomers,
            PendingReviews = pendingReviews,
            LowStockCount = lowStockCount
        };
    }

    public async Task<List<SalesPointDto>> GetSalesOverTimeAsync(Guid tenantId, DateTime from, DateTime to, ReportInterval interval, CancellationToken ct = default)
    {
        // SQL: filter + project (OrderDate, Total); memory: bucket + sum.
        var rows = await NonCancelledOrders(tenantId, from, to)
            .Select(o => new { o.OrderDate, o.Total })
            .ToListAsync(ct);

        return BucketPoints(rows.Select(r => (r.OrderDate, r.Total)), interval);
    }

    public async Task<List<TopProductDto>> GetTopProductsAsync(Guid tenantId, DateTime from, DateTime to, int take, string by, CancellationToken ct = default)
    {
        // Grouped by the OrderItem snapshot (ProductId, ProductName) so aggregates
        // survive later product edits/deletes.
        var rows = await NonCancelledOrders(tenantId, from, to)
            .SelectMany(o => o.Items)
            .GroupBy(i => new { i.ProductId, i.ProductName })
            .Select(g => new TopProductDto
            {
                ProductId = g.Key.ProductId,
                ProductName = g.Key.ProductName,
                Revenue = g.Sum(i => i.UnitPrice * i.Quantity),
                Units = g.Sum(i => i.Quantity)
            })
            .ToListAsync(ct);

        var ordered = string.Equals(by, "units", StringComparison.OrdinalIgnoreCase)
            ? rows.OrderByDescending(r => r.Units).ThenByDescending(r => r.Revenue)
            : rows.OrderByDescending(r => r.Revenue).ThenByDescending(r => r.Units);

        return ordered.Take(take).ToList();
    }

    public async Task<List<StatusBreakdownDto>> GetStatusBreakdownAsync(Guid tenantId, DateTime from, DateTime to, CancellationToken ct = default)
    {
        // Breakdown intentionally includes Cancelled — it reports composition per status;
        // the revenue *metric* elsewhere excludes cancelled orders.
        var rows = await context.Orders.AsNoTracking()
            .Where(o => o.TenantId == tenantId && o.OrderDate >= from && o.OrderDate <= to)
            .GroupBy(o => o.Status)
            .Select(g => new { Status = g.Key, Count = g.Count(), Revenue = g.Sum(o => o.Total) })
            .ToListAsync(ct);

        return rows
            .OrderBy(r => r.Status)
            .Select(r => new StatusBreakdownDto { Status = r.Status.ToString(), Count = r.Count, Revenue = r.Revenue })
            .ToList();
    }

    public async Task<CustomerAnalyticsDto> GetCustomerAnalyticsAsync(Guid tenantId, DateTime from, DateTime to, ReportInterval interval, CancellationToken ct = default)
    {
        var createdDates = await context.Customers.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.CreatedAt >= from && c.CreatedAt <= to)
            .Select(c => c.CreatedAt)
            .ToListAsync(ct);

        var perCustomer = await NonCancelledOrders(tenantId, from, to)
            .GroupBy(o => o.CustomerId)
            .Select(g => new { CustomerId = g.Key, OrderCount = g.Count(), TotalSpend = g.Sum(o => o.Total) })
            .ToListAsync(ct);

        var purchasers = perCustomer.Count;
        var repeaters = perCustomer.Count(c => c.OrderCount >= 2);

        var top = perCustomer
            .OrderByDescending(c => c.TotalSpend)
            .ThenByDescending(c => c.OrderCount)
            .Take(5)
            .ToList();

        var topIds = top.Select(t => t.CustomerId).ToList();
        var emails = await context.Customers.AsNoTracking()
            .Where(c => c.TenantId == tenantId && topIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Email })
            .ToDictionaryAsync(c => c.Id, c => c.Email, ct);

        return new CustomerAnalyticsDto
        {
            NewCustomersOverTime = BucketPoints(createdDates.Select(d => (d, 0m)), interval),
            RepeatPurchaseRate = purchasers == 0 ? 0d : (double)repeaters / purchasers,
            TopCustomers = top.Select(t => new TopCustomerDto
            {
                CustomerId = t.CustomerId,
                Email = emails.GetValueOrDefault(t.CustomerId, string.Empty),
                TotalSpend = t.TotalSpend,
                OrderCount = t.OrderCount
            }).ToList()
        };
    }

    public async Task<InventoryTrendsDto> GetInventoryTrendsAsync(Guid tenantId, DateTime from, DateTime to, CancellationToken ct = default)
    {
        // Adjustment activity per day: OrderCount = number of adjustments,
        // Revenue = Σ |Delta| (absolute stock quantity moved).
        var adjustments = await context.StockAdjustments.AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.CreatedAt >= from && a.CreatedAt <= to)
            .Select(a => new { a.CreatedAt, a.Delta })
            .ToListAsync(ct);

        var lowStock = await context.ProductVariants.AsNoTracking()
            .Where(v => v.TenantId == tenantId && v.IsActive && v.StockQuantity <= LowStockThreshold)
            .OrderBy(v => v.StockQuantity)
            .Select(v => new LowStockItemDto
            {
                VariantId = v.Id,
                ProductName = v.Product!.Name,
                Sku = v.Sku,
                StockQuantity = v.StockQuantity
            })
            .ToListAsync(ct);

        return new InventoryTrendsDto
        {
            AdjustmentsOverTime = BucketPoints(adjustments.Select(a => (a.CreatedAt, (decimal)Math.Abs(a.Delta))), ReportInterval.Day),
            LowStock = lowStock
        };
    }

    public async Task<List<CategorySalesDto>> GetCategorySalesAsync(Guid tenantId, DateTime from, DateTime to, Guid? categoryId, CancellationToken ct = default)
    {
        // null → top-level categories; else → direct children of the given category.
        // Each row aggregates the category's DIRECT products only (no descendant roll-up).
        var categories = await context.Categories.AsNoTracking()
            .Where(c => c.TenantId == tenantId
                        && (categoryId == null ? c.ParentCategoryId == null : c.ParentCategoryId == categoryId))
            .Select(c => new { c.Id, c.Name })
            .ToListAsync(ct);

        var categoryIds = categories.Select(c => c.Id).ToList();

        var sales = await NonCancelledOrders(tenantId, from, to)
            .SelectMany(o => o.Items)
            .Join(context.Products.Where(p => p.TenantId == tenantId),
                item => item.ProductId,
                product => product.Id,
                (item, product) => new { product.CategoryId, item.UnitPrice, item.Quantity })
            .Where(x => categoryIds.Contains(x.CategoryId))
            .GroupBy(x => x.CategoryId)
            .Select(g => new { CategoryId = g.Key, Revenue = g.Sum(x => x.UnitPrice * x.Quantity), Units = g.Sum(x => x.Quantity) })
            .ToListAsync(ct);

        var salesByCategory = sales.ToDictionary(s => s.CategoryId);

        return categories
            .Select(c => new CategorySalesDto
            {
                CategoryId = c.Id,
                CategoryName = c.Name,
                Revenue = salesByCategory.TryGetValue(c.Id, out var s) ? s.Revenue : 0m,
                Units = salesByCategory.TryGetValue(c.Id, out var u) ? u.Units : 0
            })
            .OrderByDescending(c => c.Revenue)
            .ToList();
    }

    // ---------------------------------------------------------------- helpers

    private IQueryable<Domain.Entities.Order> NonCancelledOrders(Guid tenantId, DateTime from, DateTime to)
        => context.Orders.AsNoTracking()
            .Where(o => o.TenantId == tenantId
                        && o.Status != OrderStatus.Cancelled
                        && o.OrderDate >= from
                        && o.OrderDate <= to);

    /// <summary>
    /// Single bucketing helper used by every interval-aware query.
    /// Day = calendar date; Week = Monday-start; Month = first of month (all UTC dates).
    /// Not EF-translatable by design — callers project raw rows in SQL first.
    /// </summary>
    private static DateTime BucketStart(DateTime date, ReportInterval interval) => interval switch
    {
        ReportInterval.Week => date.AddDays(-(((int)date.DayOfWeek + 6) % 7)).Date,
        ReportInterval.Month => new DateTime(date.Year, date.Month, 1),
        _ => date.Date
    };

    /// <summary>Buckets (timestamp, value) rows: Revenue = Σ value, OrderCount = row count per bucket.</summary>
    private static List<SalesPointDto> BucketPoints(IEnumerable<(DateTime Date, decimal Value)> rows, ReportInterval interval)
        => rows
            .GroupBy(r => BucketStart(r.Date, interval))
            .OrderBy(g => g.Key)
            .Select(g => new SalesPointDto
            {
                PeriodStart = g.Key,
                Revenue = g.Sum(r => r.Value),
                OrderCount = g.Count()
            })
            .ToList();
}
