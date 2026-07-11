using FashionSaaS.Application.Customers.DTOs;
using FashionSaaS.Application.Interfaces;
using FashionSaaS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FashionSaaS.Infrastructure.Persistence.Repositories;

public class CustomerRepository(ApplicationDbContext context)
    : GenericRepository<Customer>(context), ICustomerRepository
{
    public async Task<bool> EmailExistsAsync(Guid tenantId, string email, Guid? excludeId = null, CancellationToken ct = default)
        => await DbSet.AnyAsync(
            c => c.TenantId == tenantId && c.Email == email && (excludeId == null || c.Id != excludeId),
            ct);

    public async Task<(IReadOnlyList<Customer> Items, int Total)> GetPagedAsync(CustomerFilter filter, CancellationToken ct = default)
    {
        IQueryable<Customer> query = DbSet
            .AsNoTracking()
            .AsQueryable()
            .Where(c => c.TenantId == filter.TenantId);

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            query = query.Where(c =>
                c.FirstName.Contains(term) ||
                c.LastName.Contains(term) ||
                c.Email.Contains(term));
        }

        var total = await query.CountAsync(ct);

        List<Customer> items = await query
            .OrderByDescending(c => c.CreatedAt)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    public async Task<Customer> GetOrCreateByEmailAsync(Guid tenantId, string email,
        string firstName, string lastName, string? phone, CancellationToken ct = default)
    {
        Customer? existing = await DbSet
            .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.Email == email, ct);
        if (existing is not null)
            return existing;

        var customer = new Customer
        {
            TenantId = tenantId,
            Email = email,
            FirstName = firstName,
            LastName = lastName,
            Phone = phone,
            IsActive = true
        };
        await DbSet.AddAsync(customer, ct);
        return customer;
    }
}
