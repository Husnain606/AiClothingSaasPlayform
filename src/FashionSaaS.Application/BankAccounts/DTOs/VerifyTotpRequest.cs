namespace FashionSaaS.Application.BankAccounts.DTOs;

public class VerifyTotpRequest
{
    public string TotpCode { get; set; } = string.Empty;
}
