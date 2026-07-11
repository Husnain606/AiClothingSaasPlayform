using FashionSaaS.Application.Interfaces;
using FashionSaaS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FashionSaaS.Infrastructure.Persistence.Repositories;

public class RefreshTokenRepository(ApplicationDbContext context)
    : GenericRepository<RefreshToken>(context), IRefreshTokenRepository
{
    public async Task<RefreshToken?> GetActiveByUserIdAsync(Guid userId)
        => await DbSet.Where(r => r.UserId == userId && !r.IsRevoked && r.ExpiresAt > DateTime.UtcNow)
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync();

    public async Task RevokeAllByUserIdAsync(Guid userId)
    {
        List<RefreshToken> tokens = await DbSet.Where(r => r.UserId == userId && !r.IsRevoked).ToListAsync();
        foreach (RefreshToken? token in tokens)
        {
            token.IsRevoked = true;
            token.RevokedAt = DateTime.UtcNow;
        }
    }
}
