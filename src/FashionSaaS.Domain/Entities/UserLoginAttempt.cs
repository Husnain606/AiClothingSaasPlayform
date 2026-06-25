namespace FashionSaaS.Domain.Entities;

public class UserLoginAttempt : BaseEntity
{
    public string Email { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public string UserAgent { get; set; } = string.Empty;
    public bool IsSuccess { get; set; }
    public string? FailureReason { get; set; }
}
