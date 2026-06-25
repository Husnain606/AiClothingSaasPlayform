namespace FashionSaaS.Domain.Entities;

public class Tenant : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? LogoUrl { get; set; }
    public string? CoverImageUrl { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<User> Users { get; set; } = new List<User>();
    public ICollection<TenantSubscription> Subscriptions { get; set; } = new List<TenantSubscription>();
    public ICollection<BankAccount> BankAccounts { get; set; } = new List<BankAccount>();
}
