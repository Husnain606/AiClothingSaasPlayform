using FashionSaaS.TryOn.Application.Quota;
using FashionSaaS.TryOn.Domain;
using FashionSaaS.TryOn.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FashionSaaS.TryOn.Infrastructure.Quota;

public class UsageQuotaService(TryOnDbContext dbContext) : IUsageQuotaService
{
    public async Task<int> GetUsedThisMonthAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        DateTime startOfMonth = new(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        // Counts Processing as well as Completed: a submitted render has already consumed the
        // upstream capacity it costs, and since going async it stays Processing for minutes (up to
        // the worker's 10-minute timeout). Counting only Completed would let a tenant at its limit
        // fire an unbounded burst of submits, every one of which sees an unchanged used-count -
        // i.e. the AI quota would be trivially bypassable for the whole render window.
        // Failed rows are excluded so a failure never bills against the quota.
        var tryOnCount = await dbContext.TryOnRequests
            .Where(t => t.TenantId == tenantId && t.Status != TryOnStatus.Failed && t.CreatedAt >= startOfMonth)
            .CountAsync(cancellationToken).ConfigureAwait(false);

        var measurementCount = await dbContext.MeasurementRequests
            .Where(m => m.TenantId == tenantId && m.Status == MeasurementStatus.Completed && m.CreatedAt >= startOfMonth)
            .CountAsync(cancellationToken).ConfigureAwait(false);

        var chatCount = await dbContext.ChatRequests
            .Where(c => c.TenantId == tenantId && c.Status == ChatRequestStatus.Completed && c.CreatedAt >= startOfMonth)
            .CountAsync(cancellationToken).ConfigureAwait(false);

        return tryOnCount + measurementCount + chatCount;
    }
}
