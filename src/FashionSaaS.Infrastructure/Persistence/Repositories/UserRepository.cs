using FashionSaaS.Application.Interfaces;
using FashionSaaS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FashionSaaS.Infrastructure.Persistence.Repositories;

public class UserRepository(ApplicationDbContext context)
    : GenericRepository<User>(context), IUserRepository
{
    public async Task<User?> GetByEmailAsync(string email)
        => await DbSet.FirstOrDefaultAsync(u => u.Email == email);

    public async Task<User?> GetByIdWithRolesAsync(Guid id)
        => await DbSet.Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .Include(u => u.MfaSettings)
            .Include(u => u.Tenant)
            .FirstOrDefaultAsync(u => u.Id == id);

    public async Task<bool> EmailExistsAsync(string email)
        => await DbSet.AnyAsync(u => u.Email == email);

    public async Task<IReadOnlyList<User>> GetByTenantAsync(Guid tenantId)
        => await DbSet.Where(u => u.TenantId == tenantId).ToListAsync();

    public async Task<int> GetRecentFailedLoginCountAsync(string email, int windowMinutes = 15)
    {
        DateTime since = DateTime.UtcNow.AddMinutes(-windowMinutes);
        return await Context.UserLoginAttempts
            .Where(a => a.Email == email && !a.IsSuccess && a.CreatedAt >= since)
            .CountAsync();
    }
}
