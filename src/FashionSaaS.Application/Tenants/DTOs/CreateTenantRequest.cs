namespace FashionSaaS.Application.Tenants.DTOs;

public class CreateTenantRequest
{
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? LogoUrl { get; set; }
    public string? CoverImageUrl { get; set; }
}
