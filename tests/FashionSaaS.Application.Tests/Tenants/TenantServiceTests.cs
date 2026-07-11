using FashionSaaS.Application.Common;
using FashionSaaS.Application.Interfaces;
using FashionSaaS.Application.Tenants;
using FashionSaaS.Application.Tenants.DTOs;
using FashionSaaS.Domain.Entities;
using FluentAssertions;
using Moq;

namespace FashionSaaS.Application.Tests.Tenants;

public class TenantServiceTests
{
    private readonly Mock<ITenantRepository> _tenantRepo = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IAuditLogService> _audit = new();
    private readonly Mock<IEmailService> _email = new();

    private TenantService CreateService() => new(_tenantRepo.Object, _uow.Object, _audit.Object, _email.Object);

    [Fact]
    public async Task CreateAsync_NewSlug_ReturnsSuccess()
    {
        _tenantRepo.Setup(r => r.SlugExistsAsync("nike")).ReturnsAsync(false);
        _tenantRepo.Setup(r => r.EmailExistsAsync("admin@nike.com")).ReturnsAsync(false);

        TenantService service = CreateService();
        ResponseData<TenantResponse> result = await service.CreateAsync(new CreateTenantRequest
        {
            Name = "Nike",
            Slug = "nike",
            Email = "admin@nike.com"
        }, Guid.NewGuid(), "127.0.0.1", "Mozilla");

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(201);
        _tenantRepo.Verify(r => r.AddAsync(It.IsAny<Tenant>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_DuplicateSlug_ReturnsConflict()
    {
        _tenantRepo.Setup(r => r.SlugExistsAsync("nike")).ReturnsAsync(true);

        TenantService service = CreateService();
        ResponseData<TenantResponse> result = await service.CreateAsync(new CreateTenantRequest
        {
            Name = "Nike",
            Slug = "nike",
            Email = "admin@nike.com"
        }, Guid.NewGuid(), "127.0.0.1", "Mozilla");

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task SuspendAsync_ActiveTenant_SuspendsTenant()
    {
        var tenant = new Tenant { Id = Guid.NewGuid(), IsActive = true, Email = "admin@nike.com", Name = "Nike" };
        _tenantRepo.Setup(r => r.GetByIdAsync(tenant.Id)).ReturnsAsync(tenant);

        TenantService service = CreateService();
        ResponseData<bool> result = await service.SuspendAsync(tenant.Id, Guid.NewGuid(), "127.0.0.1", "Mozilla");

        result.IsSuccess.Should().BeTrue();
        tenant.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task CreateAsync_MalformedSlug_Returns400BeforeUniquenessCheck()
    {
        // "My Brand!" contains uppercase, spaces and special characters — TenantSlug should reject it
        TenantService service = CreateService();
        ResponseData<TenantResponse> result = await service.CreateAsync(new CreateTenantRequest
        {
            Name = "My Brand",
            Slug = "My Brand!",
            Email = "admin@mybrand.com"
        }, Guid.NewGuid(), "127.0.0.1", "Mozilla");

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        // The slug uniqueness check must NOT have been called
        _tenantRepo.Verify(r => r.SlugExistsAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task SuspendAsync_AlreadySuspendedTenant_Returns409()
    {
        var tenant = new Tenant { Id = Guid.NewGuid(), IsActive = false, Email = "admin@nike.com", Name = "Nike" };
        _tenantRepo.Setup(r => r.GetByIdAsync(tenant.Id)).ReturnsAsync(tenant);

        TenantService service = CreateService();
        ResponseData<bool> result = await service.SuspendAsync(tenant.Id, Guid.NewGuid(), "127.0.0.1", "Mozilla");

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
        _tenantRepo.Verify(r => r.UpdateAsync(It.IsAny<Tenant>()), Times.Never);
    }
}
