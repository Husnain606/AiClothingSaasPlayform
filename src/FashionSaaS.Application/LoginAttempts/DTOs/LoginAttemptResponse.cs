namespace FashionSaaS.Application.LoginAttempts.DTOs;

public class LoginAttemptResponse
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public bool IsSuccess { get; set; }
    public string? FailureReason { get; set; }
    public DateTime CreatedAt { get; set; }
}
