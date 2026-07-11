namespace FashionSaaS.Domain.Entities;

public class User : BaseEntity
{
    public Guid? TenantId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public bool IsEmailVerified { get; set; }
    public bool IsLocked { get; set; }

    public Tenant? Tenant { get; set; }
    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
    public ICollection<PasswordHistory> PasswordHistories { get; set; } = new List<PasswordHistory>();
    public ICollection<PasswordResetToken> PasswordResetTokens { get; set; } = new List<PasswordResetToken>();
    public UserMfaSettings? MfaSettings { get; set; }
}
