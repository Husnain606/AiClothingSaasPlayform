namespace FashionSaaS.Application.Tenants.DTOs;

public class TenantResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? LogoUrl { get; set; }
    public string? PaymentInstructions { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}
