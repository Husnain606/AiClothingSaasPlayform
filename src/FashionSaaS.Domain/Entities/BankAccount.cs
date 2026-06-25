namespace FashionSaaS.Domain.Entities;

public class BankAccount : BaseEntity
{
    public Guid? TenantId { get; set; }
    public bool IsActive { get; set; } = true;

    // All five fields stored as AES-256-GCM encrypted ciphertext
    public string AccountTitleEncrypted { get; set; } = string.Empty;
    public string AccountNumberEncrypted { get; set; } = string.Empty;
    public string BankNameEncrypted { get; set; } = string.Empty;
    public string BranchCodeEncrypted { get; set; } = string.Empty;
    public string IbanEncrypted { get; set; } = string.Empty;

    public Tenant? Tenant { get; set; }
}
