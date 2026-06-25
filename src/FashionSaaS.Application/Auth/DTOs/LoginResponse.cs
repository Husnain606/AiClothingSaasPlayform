namespace FashionSaaS.Application.Auth.DTOs;

public class LoginResponse
{
    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public bool MfaRequired { get; set; }
    public string? MfaToken { get; set; }
}
