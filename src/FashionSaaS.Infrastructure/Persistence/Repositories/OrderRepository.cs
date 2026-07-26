using FashionSaaS.Application.Interfaces;
using FashionSaaS.Application.Orders.DTOs;
using FashionSaaS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FashionSaaS.Infrastructure.Persistence.Repositories;

public class OrderRepository(ApplicationDbContext context) : IOrderRepository
{
    public async Task AddAsync(Order order) => await context.Orders.AddAsync(order);

    public Task<Order?> GetByIdWithItemsAsync(Guid id, CancellationToken ct = default) =>
        context.Orders.Include(o => o.Items).Include(o => o.PaymentProof).FirstOrDefaultAsync(o => o.Id == id, ct);

    public async Task<(IReadOnlyList<Order> Items, int TotalCount)> GetPagedAsync(
        OrderFilter filter, CancellationToken ct = default)
    {
        IQueryable<Order> query = context.Orders.AsNoTracking().Include(o => o.Items).AsQueryable();

        if (filter.TenantId is { } tenantId)
            query = query.Where(o => o.TenantId == tenantId);
        if (filter.Status is { } status)
            query = query.Where(o => o.Status == status);
        if (filter.From is { } from)
            query = query.Where(o => o.OrderDate >= from);
        if (filter.To is { } to)
            query = query.Where(o => o.OrderDate <= to);
        if (filter.CustomerId is { } customerId)
            query = query.Where(o => o.CustomerId == customerId);
        if (!string.IsNullOrWhiteSpace(filter.CustomerEmail))
            query = query.Where(o => o.ShippingEmail == filter.CustomerEmail);
        if (!string.IsNullOrWhiteSpace(filter.Search))
            query = query.Where(o => o.OrderNumber.Contains(filter.Search));

        var total = await query.CountAsync(ct);
        List<Order> items = await query
            .OrderByDescending(o => o.OrderDate)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    public Task<int> CountForYearAsync(Guid tenantId, int year, CancellationToken ct = default) =>
        context.Orders.CountAsync(o => o.TenantId == tenantId && o.OrderDate.Year == year, ct);
}
