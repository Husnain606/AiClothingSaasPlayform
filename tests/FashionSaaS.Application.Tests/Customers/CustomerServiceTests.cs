using FashionSaaS.Application.Common;
using FashionSaaS.Application.Customers;
using FashionSaaS.Application.Customers.DTOs;
using FashionSaaS.Application.Interfaces;
using FashionSaaS.Domain.Entities;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FashionSaaS.Application.Tests.Customers;

public class CustomerServiceTests
{
    private readonly Mock<ICustomerRepository> _customers = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IAuditLogService> _audit = new();
    private readonly Mock<ICurrentTenantService> _tenant = new();
    private readonly Guid _tenantId = Guid.NewGuid();

    public CustomerServiceTests()
    {
        _tenant.SetupGet(t => t.TenantId).Returns(_tenantId);
    }

    private CustomerService CreateService() => new(
        _customers.Object, _uow.Object, _audit.Object, _tenant.Object,
        NullLogger<CustomerService>.Instance);

    private Customer Customer(string email = "a@b.com") => new()
    {
        Id = Guid.NewGuid(),
        TenantId = _tenantId,
        FirstName = "Ann",
        LastName = "Lee",
        Email = email,
        IsActive = true
    };

    // ── Create ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_NewEmail_Succeeds()
    {
        _customers.Setup(r => r.EmailExistsAsync(_tenantId, "new@b.com", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        ResponseData<CustomerResponse> result = await CreateService().CreateAsync(
            new CreateCustomerRequest { FirstName = "Ann", LastName = "Lee", Email = "new@b.com" },
            Guid.NewGuid(), "127.0.0.1", "ua");

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(201);
        _customers.Verify(r => r.AddAsync(It.IsAny<Customer>()), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_DuplicateEmail_Returns409()
    {
        _customers.Setup(r => r.EmailExistsAsync(_tenantId, "dup@b.com", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        ResponseData<CustomerResponse> result = await CreateService().CreateAsync(
            new CreateCustomerRequest { FirstName = "Ann", LastName = "Lee", Email = "dup@b.com" },
            Guid.NewGuid(), "127.0.0.1", "ua");

        result.StatusCode.Should().Be(409);
        _customers.Verify(r => r.AddAsync(It.IsAny<Customer>()), Times.Never);
    }

    // ── Update ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_DuplicateEmailExcludingSelf_Returns409()
    {
        Customer customer = Customer();
        _customers.Setup(r => r.GetByIdAsync(customer.Id)).ReturnsAsync(customer);
        _customers.Setup(r => r.EmailExistsAsync(_tenantId, "taken@b.com", customer.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        ResponseData<CustomerResponse> result = await CreateService().UpdateAsync(customer.Id,
            new UpdateCustomerRequest { FirstName = "Ann", LastName = "Lee", Email = "taken@b.com" },
            Guid.NewGuid(), "127.0.0.1", "ua");

        result.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task UpdateAsync_OtherTenant_Returns404()
    {
        var customer = new Customer { Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), Email = "x@b.com" };
        _customers.Setup(r => r.GetByIdAsync(customer.Id)).ReturnsAsync(customer);

        ResponseData<CustomerResponse> result = await CreateService().UpdateAsync(customer.Id,
            new UpdateCustomerRequest { FirstName = "Ann", LastName = "Lee", Email = "x@b.com" },
            Guid.NewGuid(), "127.0.0.1", "ua");

        result.StatusCode.Should().Be(404);
    }

    // ── Deactivate ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeactivateAsync_SetsInactive_AndWritesAudit()
    {
        Customer customer = Customer();
        _customers.Setup(r => r.GetByIdAsync(customer.Id)).ReturnsAsync(customer);

        ResponseData<bool> result = await CreateService().DeactivateAsync(customer.Id, Guid.NewGuid(), "127.0.0.1", "ua");

        result.IsSuccess.Should().BeTrue();
        customer.IsActive.Should().BeFalse();
        _audit.Verify(a => a.LogAsync(It.IsAny<Guid?>(), _tenantId, "CustomerDeactivated", "Customer",
            customer.Id, It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
    }

    // ── GetAll (paging) ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAllAsync_ReturnsPagedResult_WithTenantScopeEnforced()
    {
        var filter = new CustomerFilter { Page = 2, PageSize = 5, TenantId = Guid.NewGuid() };
        _customers.Setup(r => r.GetPagedAsync(It.Is<CustomerFilter>(f => f.TenantId == _tenantId), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<Customer> { Customer() }, 11));

        ResponseData<PagedResult<CustomerResponse>> result = await CreateService().GetAllAsync(filter);

        result.IsSuccess.Should().BeTrue();
        result.Data!.TotalCount.Should().Be(11);
        result.Data.Page.Should().Be(2);
        result.Data.PageSize.Should().Be(5);
        result.Data.Items.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetByIdAsync_OtherTenant_Returns404()
    {
        var customer = new Customer { Id = Guid.NewGuid(), TenantId = Guid.NewGuid() };
        _customers.Setup(r => r.GetByIdAsync(customer.Id)).ReturnsAsync(customer);

        ResponseData<CustomerResponse> result = await CreateService().GetByIdAsync(customer.Id);

        result.StatusCode.Should().Be(404);
    }
}
