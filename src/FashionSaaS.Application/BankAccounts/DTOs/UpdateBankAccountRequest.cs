namespace FashionSaaS.Application.BankAccounts.DTOs;

public class UpdateBankAccountRequest
{
    public string AccountTitle { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;
    public string BankName { get; set; } = string.Empty;
    public string BranchCode { get; set; } = string.Empty;
    public string Iban { get; set; } = string.Empty;
    public string CurrentPassword { get; set; } = string.Empty;
}
