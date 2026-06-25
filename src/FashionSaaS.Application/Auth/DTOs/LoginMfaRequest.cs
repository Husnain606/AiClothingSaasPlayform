namespace FashionSaaS.Application.Auth.DTOs;

public class LoginMfaRequest
{
    public string MfaToken { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
}
