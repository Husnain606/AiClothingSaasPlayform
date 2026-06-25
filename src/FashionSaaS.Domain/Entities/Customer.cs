namespace FashionSaaS.Domain.Entities;

public class Customer : BaseEntity
{
    public Guid TenantId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<Review> Reviews { get; set; } = new List<Review>();
    public Wishlist? Wishlist { get; set; }
}
