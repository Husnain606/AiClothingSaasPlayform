namespace FashionSaaS.Domain.Entities;

public class PasswordHistory : BaseEntity
{
    public Guid UserId { get; set; }
    public string PasswordHash { get; set; } = string.Empty;

    public User User { get; set; } = null!;
}
