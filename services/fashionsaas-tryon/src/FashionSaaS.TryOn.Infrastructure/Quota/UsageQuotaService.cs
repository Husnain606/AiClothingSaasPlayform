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

        var tryOnCount = await dbContext.TryOnRequests
            .Where(t => t.TenantId == tenantId && t.Status == TryOnStatus.Completed && t.CreatedAt >= startOfMonth)
            .CountAsync(cancellationToken).ConfigureAwait(false);

        return tryOnCount;
    }
}
