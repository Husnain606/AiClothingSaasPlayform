using FashionSaaS.Application.Common;
using FashionSaaS.Application.Interfaces;
using FashionSaaS.Application.Reports.DTOs;
using FashionSaaS.Application.Reports.Validators;
using Microsoft.Extensions.Logging;

namespace FashionSaaS.Application.Reports;

/// <summary>
/// Tenant-scoped reporting facade: resolves the current tenant, applies the shared
/// range guard (from ≤ to, span ≤ 366 days → 400 on violation) once for all 7 queries,
/// and delegates the aggregate math to <see cref="IReportRepository"/>.
/// </summary>
public class ReportService(
    IReportRepository reportRepository,
    ICurrentTenantService currentTenant,
    ILogger<ReportService> logger)
{
    public Task<ResponseData<SummaryReportDto>> GetSummaryAsync(DateTime from, DateTime to, CancellationToken ct = default)
        => RunAsync("Summary", from, to, tenantId => reportRepository.GetSummaryAsync(tenantId, from, to, ct));

    public Task<ResponseData<List<SalesPointDto>>> GetSalesOverTimeAsync(DateTime from, DateTime to, ReportInterval interval, CancellationToken ct = default)
        => RunAsync("SalesOverTime", from, to, tenantId => reportRepository.GetSalesOverTimeAsync(tenantId, from, to, interval, ct));

    public Task<ResponseData<List<TopProductDto>>> GetTopProductsAsync(DateTime from, DateTime to, int take, string by, CancellationToken ct = default)
    {
        // CA1308 suppressed: normalizedBy flows into reportRepository.GetTopProductsAsync,
        // where it's compared/queried against lowercase-stored values — flipping to
        // ToUpperInvariant here without also verifying every downstream consumer risks a
        // silent query-matching regression outside this method's visibility.
#pragma warning disable CA1308
        var normalizedBy = by?.Trim().ToLowerInvariant();
#pragma warning restore CA1308
        if (normalizedBy is not ("revenue" or "units"))
            return Task.FromResult(ResponseData<List<TopProductDto>>.Failure("'by' must be 'revenue' or 'units'.", 400));
        if (take < 1)
            return Task.FromResult(ResponseData<List<TopProductDto>>.Failure("'take' must be at least 1.", 400));

        return RunAsync("TopProducts", from, to, tenantId => reportRepository.GetTopProductsAsync(tenantId, from, to, take, normalizedBy, ct));
    }

    public Task<ResponseData<List<StatusBreakdownDto>>> GetStatusBreakdownAsync(DateTime from, DateTime to, CancellationToken ct = default)
        => RunAsync("StatusBreakdown", from, to, tenantId => reportRepository.GetStatusBreakdownAsync(tenantId, from, to, ct));

    public Task<ResponseData<CustomerAnalyticsDto>> GetCustomerAnalyticsAsync(DateTime from, DateTime to, ReportInterval interval, CancellationToken ct = default)
        => RunAsync("CustomerAnalytics", from, to, tenantId => reportRepository.GetCustomerAnalyticsAsync(tenantId, from, to, interval, ct));

    public Task<ResponseData<InventoryTrendsDto>> GetInventoryTrendsAsync(DateTime from, DateTime to, CancellationToken ct = default)
        => RunAsync("InventoryTrends", from, to, tenantId => reportRepository.GetInventoryTrendsAsync(tenantId, from, to, ct));

    public Task<ResponseData<List<CategorySalesDto>>> GetCategorySalesAsync(DateTime from, DateTime to, Guid? categoryId, CancellationToken ct = default)
        => RunAsync("CategorySales", from, to, tenantId => reportRepository.GetCategorySalesAsync(tenantId, from, to, categoryId, ct));

    /// <summary>Single guard used by all report methods: tenant resolution + range validation.</summary>
    private async Task<ResponseData<T>> RunAsync<T>(string report, DateTime from, DateTime to, Func<Guid, Task<T>> query)
    {
        if (currentTenant.TenantId is not { } tenantId)
            return ResponseData<T>.Failure("Tenant could not be resolved.", 400);

        if (ReportRangeValidator.Validate(from, to) is { } error)
            return ResponseData<T>.Failure(error, 400);

        T? data = await query(tenantId);
        logger.LogInformation("Report {Report} generated for tenant {TenantId} ({From:u}..{To:u})", report, tenantId, from, to);
        return ResponseData<T>.Success(data);
    }
}
