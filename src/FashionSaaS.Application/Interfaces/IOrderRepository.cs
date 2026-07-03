using FashionSaaS.Application.Orders.DTOs;
using FashionSaaS.Domain.Entities;

namespace FashionSaaS.Application.Interfaces;

public interface IOrderRepository
{
    Task AddAsync(Order order);
    Task<Order?> GetByIdWithItemsAsync(Guid id, CancellationToken ct = default);
    Task<(IReadOnlyList<Order> Items, int TotalCount)> GetPagedAsync(OrderFilter filter, CancellationToken ct = default);
    Task<int> CountForYearAsync(Guid tenantId, int year, CancellationToken ct = default); // for order number sequence
}
