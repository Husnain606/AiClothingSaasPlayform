using FashionSaaS.Application.Interfaces;
using FashionSaaS.Domain.Entities;
using FashionSaaS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FashionSaaS.Infrastructure.Persistence.Repositories;

public class PaymentRepository(ApplicationDbContext context)
    : GenericRepository<SubscriptionPayment>(context), IPaymentRepository
{
    public async Task<IReadOnlyList<SubscriptionPayment>> GetPendingOverdueAsync(DateTime asOf)
        => await DbSet.Include(p => p.Tenant)
            .Where(p => p.Status == PaymentStatus.Pending && p.DueDate < asOf)
            .ToListAsync();

    public async Task<IReadOnlyList<SubscriptionPayment>> GetDueSoonAsync(DateTime targetDate)
        => await DbSet.Include(p => p.Tenant)
            .Where(p => p.Status == PaymentStatus.Pending &&
                p.DueDate.Date == targetDate.Date)
            .ToListAsync();

    public async Task<IReadOnlyList<SubscriptionPayment>> GetBySubscriptionAsync(Guid subscriptionId)
        => await DbSet.Where(p => p.SubscriptionId == subscriptionId)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync();
}
