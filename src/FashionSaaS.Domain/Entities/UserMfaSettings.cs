namespace FashionSaaS.Domain.Entities;

public class UserMfaSettings : BaseEntity
{
    public Guid UserId { get; set; }
    public bool IsEnabled { get; set; }
    public string? TotpSecretEncrypted { get; set; }
    public bool IsEnrolled { get; set; }

    public User User { get; set; } = null!;
    public ICollection<MfaBackupCode> BackupCodes { get; set; } = new List<MfaBackupCode>();
}
