using FashionSaaS.Application.Categories;
using FashionSaaS.Application.Categories.DTOs;
using FashionSaaS.Application.Interfaces;
using FashionSaaS.Infrastructure.Persistence;
using FashionSaaS.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace FashionSaaS.Infrastructure.Tests.Catalogs;

public class CatalogWorkflowTests
{
    private Guid _tenantId = Guid.NewGuid();
    private Guid _userId = Guid.NewGuid();
    private string _ipAddress = "127.0.0.1";
    private string _userAgent = "test-agent";

    private ApplicationDbContext CreateContext()
    {
        var currentTenant = new Mock<ICurrentTenantService>();
        currentTenant.Setup(c => c.TenantId).Returns(_tenantId);

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options, currentTenant.Object);
    }

    private (CategoryService, CategoryRepository, IUnitOfWork) GetCategoryDependencies(ApplicationDbContext ctx)
    {
        var auditLog = new Mock<IAuditLogService>();
        var currentTenant = new Mock<ICurrentTenantService>();
        currentTenant.Setup(c => c.TenantId).Returns(_tenantId);
        var logger = new Mock<ILogger<CategoryService>>();
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(async () => await ctx.SaveChangesAsync());

        var repo = new CategoryRepository(ctx);
        var service = new CategoryService(repo, unitOfWork.Object, auditLog.Object, currentTenant.Object, logger.Object);
        return (service, repo, unitOfWork.Object);
    }

    [Fact]
    public async Task CreateCategory_ValidRequest_SavesAndReturnsSuccess()
    {
        await using var ctx = CreateContext();
        var (service, _, _) = GetCategoryDependencies(ctx);

        var request = new CreateCategoryRequest
        {
            Name = "Apparel",
            Slug = "apparel",
            Description = "Clothing and accessories",
            SortOrder = 1
        };

        var response = await service.CreateAsync(request, _userId, _ipAddress, _userAgent);

        response.StatusCode.Should().Be(201);
        response.IsSuccess.Should().BeTrue();
        response.Data.Should().NotBeNull();
        response.Data!.Name.Should().Be("Apparel");

        var saved = await ctx.Categories.FirstOrDefaultAsync(c => c.Slug == "apparel");
        saved.Should().NotBeNull();
    }

    [Fact]
    public async Task CreateCategory_DuplicateSlug_Returns409()
    {
        await using var ctx = CreateContext();
        var (service, _, _) = GetCategoryDependencies(ctx);

        var request1 = new CreateCategoryRequest
        {
            Name = "Apparel",
            Slug = "apparel",
            SortOrder = 1
        };
        var response1 = await service.CreateAsync(request1, _userId, _ipAddress, _userAgent);
        response1.StatusCode.Should().Be(201);

        var request2 = new CreateCategoryRequest
        {
            Name = "Clothing",
            Slug = "apparel",
            SortOrder = 2
        };
        var response2 = await service.CreateAsync(request2, _userId, _ipAddress, _userAgent);

        response2.StatusCode.Should().Be(409);
        response2.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task CreateCategoryWithParent_ValidParent_SavesHierarchy()
    {
        await using var ctx = CreateContext();
        var (service, _, _) = GetCategoryDependencies(ctx);

        var parentRequest = new CreateCategoryRequest
        {
            Name = "Apparel",
            Slug = "apparel",
            SortOrder = 1
        };
        var parentResponse = await service.CreateAsync(parentRequest, _userId, _ipAddress, _userAgent);
        parentResponse.StatusCode.Should().Be(201);
        var parentId = parentResponse.Data!.Id;

        var childRequest = new CreateCategoryRequest
        {
            Name = "Shirts",
            Slug = "shirts",
            ParentCategoryId = parentId,
            SortOrder = 1
        };
        var childResponse = await service.CreateAsync(childRequest, _userId, _ipAddress, _userAgent);

        childResponse.StatusCode.Should().Be(201);
        var saved = await ctx.Categories.FirstOrDefaultAsync(c => c.Slug == "shirts");
        saved.Should().NotBeNull();
        saved!.ParentCategoryId.Should().Be(parentId);
    }
}
