using FashionSaaS.Domain.Entities;

namespace FashionSaaS.Application.Interfaces;

public interface ILoginAttemptRepository : IGenericRepository<UserLoginAttempt>
{
    Task<IReadOnlyList<UserLoginAttempt>> GetByEmailAsync(string email, int limit = 50);
    Task<IReadOnlyList<string>> GetRecentIpsByUserEmailAsync(string email, int limit = 20);
    Task<int> GetRecentFailureCountAsync(string email, int windowMinutes);
    Task ResetRecentFailedAttemptsAsync(string email, CancellationToken cancellationToken = default);
}
