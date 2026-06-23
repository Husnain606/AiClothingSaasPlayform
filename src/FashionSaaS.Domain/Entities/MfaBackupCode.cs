namespace FashionSaaS.Domain.Entities;

public class MfaBackupCode : BaseEntity
{
    public Guid UserMfaSettingsId { get; set; }
    public string CodeHash { get; set; } = string.Empty;
    public bool IsUsed { get; set; } = false;
    public DateTime? UsedAt { get; set; }

    public UserMfaSettings MfaSettings { get; set; } = null!;
}
