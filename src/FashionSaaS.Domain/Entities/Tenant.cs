namespace FashionSaaS.Domain.Entities;

public class Tenant : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? LogoUrl { get; set; }
    public string? CoverImageUrl { get; set; }

    /// <summary>
    /// Free-text instructions telling customers where to send payment (bank details, wallet
    /// reference, etc.). Authored by the tenant and shown publicly at checkout, which is why
    /// the encrypted BankAccount record is never exposed to customers.
    /// </summary>
    public string? PaymentInstructions { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<User> Users { get; set; } = new List<User>();
    public ICollection<TenantSubscription> Subscriptions { get; set; } = new List<TenantSubscription>();
    public ICollection<BankAccount> BankAccounts { get; set; } = new List<BankAccount>();
}
