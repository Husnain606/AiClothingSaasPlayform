namespace FashionSaaS.Application.Auth.DTOs;

public class LoginMfaRequest
{
    public Guid UserId { get; set; }
    public string Code { get; set; } = string.Empty;
}
