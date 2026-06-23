using FashionSaaS.Domain.Entities;
using FashionSaaS.Domain.Enums;

namespace FashionSaaS.Application.Interfaces;

public interface IRoleRepository : IGenericRepository<Role>
{
    Task<Role?> GetByRoleTypeAsync(RoleType roleType, CancellationToken cancellationToken = default);
}
