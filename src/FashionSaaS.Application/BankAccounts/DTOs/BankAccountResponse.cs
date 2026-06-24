namespace FashionSaaS.Application.BankAccounts.DTOs;

public class BankAccountResponse
{
    public Guid Id { get; set; }
    public Guid? TenantId { get; set; }
    public string AccountTitle { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;  // always masked ****1234
    public string BankName { get; set; } = string.Empty;
    public string BranchCode { get; set; } = string.Empty;
    public string Iban { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}
