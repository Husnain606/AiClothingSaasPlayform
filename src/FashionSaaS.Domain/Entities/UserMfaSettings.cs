namespace FashionSaaS.Domain.Entities;

public class UserMfaSettings : BaseEntity
{
    public Guid UserId { get; set; }
    public bool IsEnabled { get; set; } = false;
    public string? TotpSecretEncrypted { get; set; }
    public bool IsEnrolled { get; set; } = false;

    public User User { get; set; } = null!;
    public ICollection<MfaBackupCode> BackupCodes { get; set; } = new List<MfaBackupCode>();
}
