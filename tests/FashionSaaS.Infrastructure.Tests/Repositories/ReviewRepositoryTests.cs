using FashionSaaS.Application.Interfaces;
using FashionSaaS.Application.Reviews.DTOs;
using FashionSaaS.Domain.Entities;
using FashionSaaS.Domain.Enums;
using FashionSaaS.Infrastructure.Persistence;
using FashionSaaS.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace FashionSaaS.Infrastructure.Tests.Repositories;

public class ReviewRepositoryTests
{
    private Guid _tenantId = Guid.NewGuid();
    private Guid _productId = Guid.NewGuid();

    private ApplicationDbContext CreateContext()
    {
        var currentTenant = new Mock<ICurrentTenantService>();
        currentTenant.Setup(c => c.TenantId).Returns(_tenantId);

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options, currentTenant.Object);
    }

    private Customer CreateCustomer() => new Customer
    {
        TenantId = _tenantId,
        FirstName = "John",
        LastName = "Doe",
        Email = "john@example.com",
        IsActive = true
    };

    [Fact]
    public async Task GetPagedAsync_ProductWithReviews_ReturnsReviewsOrderedByDateDescending()
    {
        await using var ctx = CreateContext();
        var customer = CreateCustomer();
        ctx.Customers.Add(customer);

        var review1 = new Review
        {
            TenantId = _tenantId,
            ProductId = _productId,
            CustomerId = customer.Id,
            Rating = 5,
            Title = "Excellent",
            Body = "Excellent product",
            Status = ReviewStatus.Approved
        };
        var review2 = new Review
        {
            TenantId = _tenantId,
            ProductId = _productId,
            CustomerId = customer.Id,
            Rating = 4,
            Title = "Good",
            Body = "Good quality",
            Status = ReviewStatus.Approved
        };
        ctx.Reviews.AddRange(review1, review2);
        await ctx.SaveChangesAsync();

        var repo = new ReviewRepository(ctx);
        var filter = new ReviewFilter
        {
            TenantId = _tenantId,
            ProductId = _productId,
            Page = 1,
            PageSize = 20
        };
        var (items, total) = await repo.GetPagedAsync(filter);

        items.Should().HaveCount(2);
        total.Should().Be(2);
        items.Should().BeInDescendingOrder(x => x.CreatedAt);
    }

    [Fact]
    public async Task GetPagedAsync_FilterByStatus_ReturnsOnlyMatchingReviews()
    {
        await using var ctx = CreateContext();
        var customer = CreateCustomer();
        ctx.Customers.Add(customer);

        var approved = new Review
        {
            TenantId = _tenantId,
            ProductId = _productId,
            CustomerId = customer.Id,
            Rating = 5,
            Body = "Great!",
            Status = ReviewStatus.Approved
        };
        var pending = new Review
        {
            TenantId = _tenantId,
            ProductId = _productId,
            CustomerId = customer.Id,
            Rating = 3,
            Body = "Ok",
            Status = ReviewStatus.Pending
        };
        ctx.Reviews.AddRange(approved, pending);
        await ctx.SaveChangesAsync();

        var repo = new ReviewRepository(ctx);
        var filter = new ReviewFilter
        {
            TenantId = _tenantId,
            Status = ReviewStatus.Approved,
            Page = 1,
            PageSize = 20
        };
        var (items, total) = await repo.GetPagedAsync(filter);

        items.Should().HaveCount(1);
        total.Should().Be(1);
        items.First().Status.Should().Be(ReviewStatus.Approved);
    }

    [Fact]
    public async Task GetPagedAsync_FilterByProductId_ReturnsOnlyProductReviews()
    {
        await using var ctx = CreateContext();
        var customer = CreateCustomer();
        ctx.Customers.Add(customer);

        var otherProductId = Guid.NewGuid();
        var myReview = new Review
        {
            TenantId = _tenantId,
            ProductId = _productId,
            CustomerId = customer.Id,
            Rating = 5,
            Body = "Great!",
            Status = ReviewStatus.Approved
        };
        var otherReview = new Review
        {
            TenantId = _tenantId,
            ProductId = otherProductId,
            CustomerId = customer.Id,
            Rating = 2,
            Body = "Not good",
            Status = ReviewStatus.Approved
        };
        ctx.Reviews.AddRange(myReview, otherReview);
        await ctx.SaveChangesAsync();

        var repo = new ReviewRepository(ctx);
        var filter = new ReviewFilter
        {
            TenantId = _tenantId,
            ProductId = _productId,
            Page = 1,
            PageSize = 20
        };
        var (items, total) = await repo.GetPagedAsync(filter);

        items.Should().HaveCount(1);
        total.Should().Be(1);
        items.First().ProductId.Should().Be(_productId);
    }

    [Fact]
    public async Task GetPagedAsync_WithPagination_ReturnsPaginatedResults()
    {
        await using var ctx = CreateContext();
        var customer = CreateCustomer();
        ctx.Customers.Add(customer);

        for (int i = 1; i <= 5; i++)
        {
            var review = new Review
            {
                TenantId = _tenantId,
                ProductId = _productId,
                CustomerId = customer.Id,
                Rating = i,
                Body = $"Review {i}",
                Status = ReviewStatus.Approved
            };
            ctx.Reviews.Add(review);
        }
        await ctx.SaveChangesAsync();

        var repo = new ReviewRepository(ctx);
        var filter = new ReviewFilter
        {
            TenantId = _tenantId,
            ProductId = _productId,
            Page = 1,
            PageSize = 2
        };
        var (items, total) = await repo.GetPagedAsync(filter);

        items.Should().HaveCount(2);
        total.Should().Be(5);
    }
}
