using FashionSaaS.Domain.Entities;

namespace FashionSaaS.Application.Interfaces;

public interface IOrderPaymentProofRepository : IGenericRepository<OrderPaymentProof>
{
    Task<OrderPaymentProof?> GetByOrderIdAsync(Guid orderId, CancellationToken ct = default);
}
