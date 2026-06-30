using FashionSaaS.Application.Customers.DTOs;
using FashionSaaS.Application.Interfaces;
using FashionSaaS.Domain.Entities;
using FashionSaaS.Infrastructure.Persistence;
using FashionSaaS.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace FashionSaaS.Infrastructure.Tests.Repositories;

public class CustomerRepositoryTests
{
    private Guid _tenantId = Guid.NewGuid();

    private ApplicationDbContext CreateContext()
    {
        var currentTenant = new Mock<ICurrentTenantService>();
        currentTenant.Setup(c => c.TenantId).Returns(_tenantId);

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options, currentTenant.Object);
    }

    [Fact]
    public async Task EmailExistsAsync_ExistingEmail_ReturnsTrue()
    {
        await using var ctx = CreateContext();
        var customer = new Customer
        {
            TenantId = _tenantId,
            FirstName = "John",
            LastName = "Doe",
            Email = "john@example.com",
            IsActive = true
        };
        ctx.Customers.Add(customer);
        await ctx.SaveChangesAsync();

        var repo = new CustomerRepository(ctx);
        var exists = await repo.EmailExistsAsync(_tenantId, "john@example.com");

        exists.Should().BeTrue();
    }

    [Fact]
    public async Task EmailExistsAsync_NonExistentEmail_ReturnsFalse()
    {
        await using var ctx = CreateContext();

        var repo = new CustomerRepository(ctx);
        var exists = await repo.EmailExistsAsync(_tenantId, "notfound@example.com");

        exists.Should().BeFalse();
    }

    [Fact]
    public async Task EmailExistsAsync_ExcludeId_IgnoresSpecificCustomer()
    {
        await using var ctx = CreateContext();
        var customer = new Customer
        {
            TenantId = _tenantId,
            FirstName = "John",
            LastName = "Doe",
            Email = "john@example.com",
            IsActive = true
        };
        ctx.Customers.Add(customer);
        await ctx.SaveChangesAsync();

        var repo = new CustomerRepository(ctx);
        var exists = await repo.EmailExistsAsync(_tenantId, "john@example.com", customer.Id);

        exists.Should().BeFalse();
    }

    [Fact]
    public async Task GetPagedAsync_WithPagination_ReturnsPaginatedResults()
    {
        await using var ctx = CreateContext();
        for (int i = 1; i <= 5; i++)
        {
            var customer = new Customer
            {
                TenantId = _tenantId,
                FirstName = $"Customer{i}",
                LastName = "Test",
                Email = $"customer{i}@example.com",
                IsActive = true
            };
            ctx.Customers.Add(customer);
        }
        await ctx.SaveChangesAsync();

        var repo = new CustomerRepository(ctx);
        var filter = new CustomerFilter
        {
            TenantId = _tenantId,
            Page = 1,
            PageSize = 2
        };
        var (items, total) = await repo.GetPagedAsync(filter);

        items.Should().HaveCount(2);
        total.Should().Be(5);
    }

    [Fact]
    public async Task GetPagedAsync_WithSearchTerm_FiltersResults()
    {
        await using var ctx = CreateContext();
        var john = new Customer
        {
            TenantId = _tenantId,
            FirstName = "John",
            LastName = "Doe",
            Email = "john@example.com",
            IsActive = true
        };
        var jane = new Customer
        {
            TenantId = _tenantId,
            FirstName = "Jane",
            LastName = "Smith",
            Email = "jane@example.com",
            IsActive = true
        };
        ctx.Customers.AddRange(john, jane);
        await ctx.SaveChangesAsync();

        var repo = new CustomerRepository(ctx);
        var filter = new CustomerFilter
        {
            TenantId = _tenantId,
            Search = "John",
            Page = 1,
            PageSize = 20
        };
        var (items, total) = await repo.GetPagedAsync(filter);

        items.Should().HaveCount(1);
        total.Should().Be(1);
        items.First().FirstName.Should().Be("John");
    }

    [Fact]
    public async Task GetPagedAsync_DifferentTenant_ReturnsNoResults()
    {
        await using var ctx = CreateContext();
        var otherTenantId = Guid.NewGuid();
        var customer = new Customer
        {
            TenantId = otherTenantId,
            FirstName = "John",
            LastName = "Doe",
            Email = "john@example.com",
            IsActive = true
        };
        ctx.Customers.Add(customer);
        await ctx.SaveChangesAsync();

        var repo = new CustomerRepository(ctx);
        var filter = new CustomerFilter
        {
            TenantId = _tenantId,
            Page = 1,
            PageSize = 20
        };
        var (items, total) = await repo.GetPagedAsync(filter);

        items.Should().BeEmpty();
        total.Should().Be(0);
    }
}
