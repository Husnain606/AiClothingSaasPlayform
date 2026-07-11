using FashionSaaS.Application.Reports.DTOs;

namespace FashionSaaS.Application.Interfaces;

/// <summary>
/// Raw tenant-scoped aggregate queries for reporting. Callers (ReportService) own
/// tenant resolution and range validation; implementations own the aggregate math.
/// All ranges are inclusive [from, to] against UTC timestamps.
/// </summary>
public interface IReportRepository
{
    Task<SummaryReportDto> GetSummaryAsync(Guid tenantId, DateTime from, DateTime to, CancellationToken ct = default);

    Task<List<SalesPointDto>> GetSalesOverTimeAsync(Guid tenantId, DateTime from, DateTime to, ReportInterval interval, CancellationToken ct = default);

    /// <param name="tenantId"></param>

    /// <param name="from"></param>
    /// <param name="to"></param>
    /// <param name="take"></param>    /// <param name="by">"revenue" or "units" (validated by the service).</param>
    /// <param name="ct"></param>
    Task<List<TopProductDto>> GetTopProductsAsync(Guid tenantId, DateTime from, DateTime to, int take, string by, CancellationToken ct = default);

    Task<List<StatusBreakdownDto>> GetStatusBreakdownAsync(Guid tenantId, DateTime from, DateTime to, CancellationToken ct = default);

    Task<CustomerAnalyticsDto> GetCustomerAnalyticsAsync(Guid tenantId, DateTime from, DateTime to, ReportInterval interval, CancellationToken ct = default);

    Task<InventoryTrendsDto> GetInventoryTrendsAsync(Guid tenantId, DateTime from, DateTime to, CancellationToken ct = default);

    /// <param name="tenantId"></param>

    /// <param name="from"></param>
    /// <param name="to"></param>    /// <param name="categoryId">null → top-level categories; otherwise → direct children of that category.</param>
    /// <param name="ct"></param>
    Task<List<CategorySalesDto>> GetCategorySalesAsync(Guid tenantId, DateTime from, DateTime to, Guid? categoryId, CancellationToken ct = default);
}
