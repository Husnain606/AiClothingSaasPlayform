namespace FashionSaaS.Application.Products.DTOs;

/// <summary>
/// Query shape for the public, unauthenticated catalog-listing endpoint. Deliberately
/// omits <c>Status</c> and <c>TenantId</c> (unlike <see cref="ProductFilter"/>) so a
/// caller can never request anything other than published products or another tenant's
/// catalog: the public controller maps this onto a <see cref="ProductFilter"/> with
/// <c>Status</c> hardcoded to <see cref="FashionSaaS.Domain.Enums.ProductStatus.Active"/>
/// before delegating to the same <c>ProductService.GetAllAsync</c> the admin catalog uses.
/// </summary>
public class PublicProductFilter
{
    public string? Search { get; set; }
    public Guid? CategoryId { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
