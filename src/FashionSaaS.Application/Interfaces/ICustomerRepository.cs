using FashionSaaS.Domain.Entities;

namespace FashionSaaS.Application.Interfaces;

public interface ICustomerRepository : IGenericRepository<Customer>
{
    Task<bool> EmailExistsAsync(Guid tenantId, string email, Guid? excludeId = null, CancellationToken ct = default);
}
