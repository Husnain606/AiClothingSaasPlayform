namespace FashionSaaS.Application.Mfa.DTOs;

public class MfaSetupResponse
{
    public string QrCodeUrl { get; set; } = string.Empty;
    public string SecretBase32 { get; set; } = string.Empty;
}
