using FashionSaaS.Domain.Entities;

namespace FashionSaaS.Application.Interfaces;

public interface IUserRepository : IGenericRepository<User>
{
    Task<User?> GetByEmailAsync(string email);
    Task<User?> GetByIdWithRolesAsync(Guid id);
    Task<bool> EmailExistsAsync(string email);
    Task<IReadOnlyList<User>> GetByTenantAsync(Guid tenantId);
    Task<int> GetRecentFailedLoginCountAsync(string email, int windowMinutes = 15);

    /// <summary>
    /// Explicitly tracks a brand-new <see cref="UserMfaSettings"/> as Added. Required because
    /// assigning it only via <c>user.MfaSettings = settings</c> on an already-tracked User causes
    /// EF Core to mark it Unchanged (its client-generated Id looks like an existing row), producing
    /// an UPDATE for a row that was never inserted.
    /// </summary>
    Task AddMfaSettingsAsync(UserMfaSettings settings);

    /// <summary>
    /// Explicitly tracks brand-new <see cref="MfaBackupCode"/> rows as Added — same reasoning as
    /// <see cref="AddMfaSettingsAsync"/>, for backup codes attached via navigation.
    /// </summary>
    Task AddMfaBackupCodesAsync(IEnumerable<MfaBackupCode> codes);
}
