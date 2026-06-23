using FashionSaaS.Domain.Entities;

namespace FashionSaaS.Application.Interfaces;

public interface IRefreshTokenRepository : IGenericRepository<RefreshToken>
{
    Task<RefreshToken?> GetActiveByUserIdAsync(Guid userId);
    Task RevokeAllByUserIdAsync(Guid userId);
}
