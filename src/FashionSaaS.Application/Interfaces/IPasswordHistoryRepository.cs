using FashionSaaS.Domain.Entities;

namespace FashionSaaS.Application.Interfaces;

public interface IPasswordHistoryRepository : IGenericRepository<PasswordHistory>
{
    Task<IReadOnlyList<PasswordHistory>> GetLastNAsync(Guid userId, int count);
}
