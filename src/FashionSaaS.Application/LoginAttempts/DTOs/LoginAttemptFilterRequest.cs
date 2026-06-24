namespace FashionSaaS.Application.LoginAttempts.DTOs;

public class LoginAttemptFilterRequest
{
    public string? Email { get; set; }
    public string? IpAddress { get; set; }
    public bool? IsSuccess { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}
