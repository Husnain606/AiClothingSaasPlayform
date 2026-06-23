using FashionSaaS.Application.Interfaces;
using FashionSaaS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FashionSaaS.Infrastructure.Persistence.Repositories;

public class LoginAttemptRepository(ApplicationDbContext context)
    : GenericRepository<UserLoginAttempt>(context), ILoginAttemptRepository
{
    public async Task<IReadOnlyList<UserLoginAttempt>> GetByEmailAsync(string email, int limit = 50)
        => await DbSet.Where(a => a.Email == email)
            .OrderByDescending(a => a.CreatedAt).Take(limit).ToListAsync();

    public async Task<IReadOnlyList<string>> GetRecentIpsByUserEmailAsync(string email, int limit = 20)
        => await DbSet.Where(a => a.Email == email && a.IsSuccess)
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => a.IpAddress)
            .Distinct()
            .Take(limit)
            .ToListAsync();

    public async Task<int> GetRecentFailureCountAsync(string email, int windowMinutes)
    {
        var since = DateTime.UtcNow.AddMinutes(-windowMinutes);
        return await DbSet.Where(a => a.Email == email && !a.IsSuccess && a.CreatedAt >= since)
            .CountAsync();
    }

    public async Task ResetRecentFailedAttemptsAsync(string email, CancellationToken cancellationToken = default)
    {
        // Use the same 15-minute window that AuthService uses for lockout evaluation.
        var since = DateTime.UtcNow.AddMinutes(-15);
        var failedAttempts = await DbSet
            .Where(a => a.Email == email && !a.IsSuccess && a.CreatedAt >= since)
            .ToListAsync(cancellationToken);
        DbSet.RemoveRange(failedAttempts);
    }
}
