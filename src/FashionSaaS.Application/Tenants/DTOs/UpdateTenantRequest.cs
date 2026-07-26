namespace FashionSaaS.Application.Tenants.DTOs;

public class UpdateTenantRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? LogoUrl { get; set; }
    public string? CoverImageUrl { get; set; }
    public string? PaymentInstructions { get; set; }
}
