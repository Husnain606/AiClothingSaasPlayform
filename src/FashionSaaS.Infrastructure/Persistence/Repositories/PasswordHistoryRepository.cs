using FashionSaaS.Application.Interfaces;
using FashionSaaS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FashionSaaS.Infrastructure.Persistence.Repositories;

public class PasswordHistoryRepository(ApplicationDbContext context)
    : GenericRepository<PasswordHistory>(context), IPasswordHistoryRepository
{
    public async Task<IReadOnlyList<PasswordHistory>> GetLastNAsync(Guid userId, int count)
        => await DbSet.Where(p => p.UserId == userId)
            .OrderByDescending(p => p.CreatedAt)
            .Take(count)
            .ToListAsync();
}
