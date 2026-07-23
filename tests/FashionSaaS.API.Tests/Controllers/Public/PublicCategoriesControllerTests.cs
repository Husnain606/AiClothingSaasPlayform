using FashionSaaS.API.Controllers.Public;
using FashionSaaS.Application.Categories;
using FashionSaaS.Application.Categories.DTOs;
using FashionSaaS.Application.Common;
using FashionSaaS.Application.Interfaces;
using FashionSaaS.Domain.Entities;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FashionSaaS.API.Tests.Controllers.Public;

/// <summary>
/// Verifies the public storefront categories endpoint reuses the real CategoryService
/// (tenant-scoped via ICurrentTenantService, already set by TenantResolutionMiddleware
/// from the {slug} route segment) and returns the tenant's categories as-is.
/// </summary>
public class PublicCategoriesControllerTests
{
    private readonly Mock<ICategoryRepository> _repo = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IAuditLogService> _audit = new();
    private readonly Mock<ICurrentTenantService> _tenant = new();
    private readonly Guid _tenantId = Guid.NewGuid();

    public PublicCategoriesControllerTests()
    {
        _tenant.SetupGet(t => t.TenantId).Returns(_tenantId);
    }

    private CategoryService CreateService() =>
        new(_repo.Object, _uow.Object, _audit.Object, _tenant.Object, NullLogger<CategoryService>.Instance);

    [Fact]
    public async Task GetAll_ValidSlugResolvedTenant_ReturnsTenantsCategories()
    {
        var womensTops = new Category { Id = Guid.NewGuid(), TenantId = _tenantId, Name = "Women's Tops", Slug = "womens-tops" };
        var mensOuterwear = new Category { Id = Guid.NewGuid(), TenantId = _tenantId, Name = "Men's Outerwear", Slug = "mens-outerwear" };
        _repo.Setup(r => r.GetTreeAsync(_tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Category> { womensTops, mensOuterwear });

        var controller = new PublicCategoriesController(CreateService());
        var result = await controller.GetAll(CancellationToken.None) as ObjectResult;

        result.Should().NotBeNull();
        result!.StatusCode.Should().Be(200);
        var body = result.Value as ResponseData<IReadOnlyList<CategoryResponse>>;
        body!.IsSuccess.Should().BeTrue();
        body.Data.Should().HaveCount(2);
        body.Data!.Select(c => c.Name).Should().Contain(["Women's Tops", "Men's Outerwear"]);
    }
}
