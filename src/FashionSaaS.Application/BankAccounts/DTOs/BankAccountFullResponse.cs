namespace FashionSaaS.Application.BankAccounts.DTOs;

/// <summary>
/// Bank account response that carries the FULL, unmasked AccountNumber.
/// This DTO is only populated by <see cref="BankAccountService.GetFullAsync"/> and
/// must never be returned to callers without prior AdminOwner / SuperAdmin authorization
/// checks (enforced at the controller layer).
/// </summary>
public class BankAccountFullResponse
{
    public Guid Id { get; set; }
    public Guid? TenantId { get; set; }
    public string AccountTitle { get; set; } = string.Empty;

    /// <summary>Full, decrypted account number — SENSITIVE. Callers must enforce authorization.</summary>
    public string AccountNumber { get; set; } = string.Empty;

    public string BankName { get; set; } = string.Empty;
    public string BranchCode { get; set; } = string.Empty;
    public string Iban { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}
