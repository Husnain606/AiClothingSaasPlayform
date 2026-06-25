using FashionSaaS.Application.Interfaces;
using FashionSaaS.Domain.Entities;
using FashionSaaS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FashionSaaS.Infrastructure.Persistence.Repositories;

public class RoleRepository(ApplicationDbContext context)
    : GenericRepository<Role>(context), IRoleRepository
{
    public async Task<Role?> GetByRoleTypeAsync(RoleType roleType, CancellationToken cancellationToken = default)
        => await DbSet.FirstOrDefaultAsync(r => r.Name == roleType, cancellationToken);
}
