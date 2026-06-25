using FashionSaaS.Domain.Entities;

namespace FashionSaaS.Application.Interfaces;

public interface IPasswordResetTokenRepository : IGenericRepository<PasswordResetToken>
{
    Task<PasswordResetToken?> GetValidByHashAsync(string tokenHash);
    Task InvalidateAllByUserIdAsync(Guid userId);
}
