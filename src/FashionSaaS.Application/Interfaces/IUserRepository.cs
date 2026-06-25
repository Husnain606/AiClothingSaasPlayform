using FashionSaaS.Domain.Entities;

namespace FashionSaaS.Application.Interfaces;

public interface IUserRepository : IGenericRepository<User>
{
    Task<User?> GetByEmailAsync(string email);
    Task<User?> GetByIdWithRolesAsync(Guid id);
    Task<bool> EmailExistsAsync(string email);
    Task<IReadOnlyList<User>> GetByTenantAsync(Guid tenantId);
    Task<int> GetRecentFailedLoginCountAsync(string email, int windowMinutes = 15);
}
