using FashionSaaS.Application.Interfaces;
using FashionSaaS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FashionSaaS.Infrastructure.Persistence.Repositories;

public class OrderPaymentProofRepository(ApplicationDbContext context)
    : GenericRepository<OrderPaymentProof>(context), IOrderPaymentProofRepository
{
    public async Task<OrderPaymentProof?> GetByOrderIdAsync(Guid orderId, CancellationToken ct = default)
        => await DbSet
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.OrderId == orderId, ct);
}
