using FashionSaaS.Application.Customers.DTOs;
using FashionSaaS.Domain.Entities;

namespace FashionSaaS.Application.Interfaces;

public interface ICustomerRepository : IGenericRepository<Customer>
{
    Task<bool> EmailExistsAsync(Guid tenantId, string email, Guid? excludeId = null, CancellationToken ct = default);
    Task<(IReadOnlyList<Customer> Items, int Total)> GetPagedAsync(CustomerFilter filter, CancellationToken ct = default);
    Task<Customer> GetOrCreateByEmailAsync(Guid tenantId, string email, string firstName, string lastName, string? phone, CancellationToken ct = default);
}
