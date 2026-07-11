using FashionSaaS.Application.Interfaces;
using FashionSaaS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FashionSaaS.Infrastructure.Persistence.Repositories;

public class PasswordResetTokenRepository(ApplicationDbContext context)
    : GenericRepository<PasswordResetToken>(context), IPasswordResetTokenRepository
{
    public async Task<PasswordResetToken?> GetValidByHashAsync(string tokenHash)
        => await DbSet.FirstOrDefaultAsync(t =>
            t.TokenHash == tokenHash && !t.IsUsed && t.ExpiresAt > DateTime.UtcNow);

    public async Task InvalidateAllByUserIdAsync(Guid userId)
    {
        List<PasswordResetToken> tokens = await DbSet.Where(t => t.UserId == userId && !t.IsUsed).ToListAsync();
        foreach (PasswordResetToken? t in tokens)
            t.IsUsed = true;
    }
}
