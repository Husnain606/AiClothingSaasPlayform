using FashionSaaS.Domain.Entities;

namespace FashionSaaS.Application.Interfaces;

public interface IBankAccountRepository : IGenericRepository<BankAccount>
{
    Task<BankAccount?> GetByTenantIdAsync(Guid? tenantId);
    Task<BankAccount?> GetPlatformAccountAsync();
}
