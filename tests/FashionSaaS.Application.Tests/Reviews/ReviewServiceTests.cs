using FashionSaaS.Application.Common;
using FashionSaaS.Application.Interfaces;
using FashionSaaS.Application.Reviews;
using FashionSaaS.Application.Reviews.DTOs;
using FashionSaaS.Domain.Entities;
using FashionSaaS.Domain.Enums;
using FashionSaaS.Domain.Events;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FashionSaaS.Application.Tests.Reviews;

public class ReviewServiceTests
{
    private readonly Mock<IReviewRepository> _reviews = new();
    private readonly Mock<IProductRepository> _products = new();
    private readonly Mock<ICustomerRepository> _customers = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IAuditLogService> _audit = new();
    private readonly Mock<ICurrentTenantService> _tenant = new();
    private readonly Guid _tenantId = Guid.NewGuid();

    public ReviewServiceTests()
    {
        _tenant.SetupGet(t => t.TenantId).Returns(_tenantId);
    }

    private ReviewService CreateService() => new(
        _reviews.Object, _products.Object, _customers.Object, _uow.Object, _audit.Object, _tenant.Object,
        NullLogger<ReviewService>.Instance);

    private Review Review(ReviewStatus status = ReviewStatus.Pending) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = _tenantId,
        ProductId = Guid.NewGuid(),
        CustomerId = Guid.NewGuid(),
        Rating = 5,
        Status = status
    };

    // ── Approve ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ApproveAsync_PendingReview_TransitionsAndRaisesEventBeforeSave()
    {
        Review review = Review();
        _reviews.Setup(r => r.GetByIdAsync(review.Id)).ReturnsAsync(review);
        var eventPresentAtSave = false;
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback(() => eventPresentAtSave = review.DomainEvents.Any(e => e is ReviewModeratedEvent))
            .ReturnsAsync(1);

        ResponseData<ReviewResponse> result = await CreateService().ApproveAsync(review.Id, Guid.NewGuid(), "127.0.0.1", "ua");

        result.IsSuccess.Should().BeTrue();
        review.Status.Should().Be(ReviewStatus.Approved);
        eventPresentAtSave.Should().BeTrue("the ReviewModeratedEvent must be added before SaveChanges");
        review.DomainEvents.OfType<ReviewModeratedEvent>().Single().Status.Should().Be(ReviewStatus.Approved);
    }

    [Fact]
    public async Task ApproveAsync_AlreadyApproved_Returns409()
    {
        Review review = Review(ReviewStatus.Approved);
        _reviews.Setup(r => r.GetByIdAsync(review.Id)).ReturnsAsync(review);

        ResponseData<ReviewResponse> result = await CreateService().ApproveAsync(review.Id, Guid.NewGuid(), "127.0.0.1", "ua");

        result.StatusCode.Should().Be(409);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Reject ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RejectAsync_PendingReview_TransitionsAndRaisesEvent()
    {
        Review review = Review();
        _reviews.Setup(r => r.GetByIdAsync(review.Id)).ReturnsAsync(review);

        ResponseData<ReviewResponse> result = await CreateService().RejectAsync(review.Id,
            new RejectReviewRequest { Reason = "spam" }, Guid.NewGuid(), "127.0.0.1", "ua");

        result.IsSuccess.Should().BeTrue();
        review.Status.Should().Be(ReviewStatus.Rejected);
        review.DomainEvents.OfType<ReviewModeratedEvent>().Single().Status.Should().Be(ReviewStatus.Rejected);
    }

    [Fact]
    public async Task RejectAsync_OtherTenant_Returns404()
    {
        var review = new Review { Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), Status = ReviewStatus.Pending };
        _reviews.Setup(r => r.GetByIdAsync(review.Id)).ReturnsAsync(review);

        ResponseData<ReviewResponse> result = await CreateService().RejectAsync(review.Id,
            new RejectReviewRequest { Reason = "x" }, Guid.NewGuid(), "127.0.0.1", "ua");

        result.StatusCode.Should().Be(404);
    }

    // ── Delete ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_RemovesReview()
    {
        Review review = Review();
        _reviews.Setup(r => r.GetByIdAsync(review.Id)).ReturnsAsync(review);

        ResponseData<bool> result = await CreateService().DeleteAsync(review.Id, Guid.NewGuid(), "127.0.0.1", "ua");

        result.IsSuccess.Should().BeTrue();
        _reviews.Verify(r => r.DeleteAsync(review), Times.Once);
    }

    // ── GetAll (filter + paging) ──────────────────────────────────────────────────

    [Fact]
    public async Task GetAllAsync_FilterByStatus_EnforcesTenantScope_AndPages()
    {
        var filter = new ReviewFilter { Status = ReviewStatus.Pending, Page = 1, PageSize = 10, TenantId = Guid.NewGuid() };
        _reviews.Setup(r => r.GetPagedAsync(
                It.Is<ReviewFilter>(f => f.TenantId == _tenantId && f.Status == ReviewStatus.Pending),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<Review> { Review() }, 3));

        ResponseData<PagedResult<ReviewResponse>> result = await CreateService().GetAllAsync(filter);

        result.IsSuccess.Should().BeTrue();
        result.Data!.TotalCount.Should().Be(3);
        result.Data.Items.Should().HaveCount(1);
    }

    // ── Submit ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SubmitAsync_ValidRequest_CreatesPendingReviewAndRaisesReviewSubmittedEvent()
    {
        var product = new Product { Id = Guid.NewGuid(), TenantId = _tenantId, Name = "Tee", BasePrice = 20m };
        var customer = new Customer { Id = Guid.NewGuid(), TenantId = _tenantId, Email = "customer@example.com" };
        _products.Setup(r => r.GetByIdAsync(product.Id)).ReturnsAsync(product);
        _customers.Setup(r => r.GetOrCreateByEmailAsync(_tenantId, customer.Email, "Jane", "Doe", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);

        var request = new SubmitReviewRequest { ProductId = product.Id, Rating = 5, Title = "Great", Body = "Loved it" };
        var eventPresentAtSave = false;
        Review? savedReview = null;
        _reviews.Setup(r => r.AddAsync(It.IsAny<Review>()))
            .Callback<Review>(r => savedReview = r)
            .Returns(Task.CompletedTask);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback(() => eventPresentAtSave = savedReview!.DomainEvents.Any(e => e is ReviewSubmittedEvent))
            .ReturnsAsync(1);

        ResponseData<ReviewResponse> result = await CreateService().SubmitAsync(
            customer.Email, "Jane", "Doe", null, request, Guid.NewGuid(), "127.0.0.1", "ua");

        result.StatusCode.Should().Be(201);
        result.Data!.ProductId.Should().Be(product.Id);
        result.Data.CustomerId.Should().Be(customer.Id);
        result.Data.Status.Should().Be(ReviewStatus.Pending);
        eventPresentAtSave.Should().BeTrue("the ReviewSubmittedEvent must be added before SaveChanges");
        savedReview!.DomainEvents.OfType<ReviewSubmittedEvent>().Single().Rating.Should().Be(5);
        _reviews.Verify(r => r.AddAsync(It.IsAny<Review>()), Times.Once);
    }

    [Fact]
    public async Task SubmitAsync_TenantUnresolved_ReturnsFailure400()
    {
        _tenant.SetupGet(t => t.TenantId).Returns((Guid?)null);
        var request = new SubmitReviewRequest { ProductId = Guid.NewGuid(), Rating = 5 };

        ResponseData<ReviewResponse> result = await CreateService().SubmitAsync(
            "customer@example.com", "Jane", "Doe", null, request, Guid.NewGuid(), "127.0.0.1", "ua");

        result.StatusCode.Should().Be(400);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SubmitAsync_UnknownProduct_Returns404()
    {
        var productId = Guid.NewGuid();
        _products.Setup(r => r.GetByIdAsync(productId)).ReturnsAsync((Product?)null);
        var request = new SubmitReviewRequest { ProductId = productId, Rating = 5 };

        ResponseData<ReviewResponse> result = await CreateService().SubmitAsync(
            "customer@example.com", "Jane", "Doe", null, request, Guid.NewGuid(), "127.0.0.1", "ua");

        result.StatusCode.Should().Be(404);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SubmitAsync_ProductBelongsToAnotherTenant_Returns404()
    {
        var product = new Product { Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), Name = "Tee", BasePrice = 20m };
        _products.Setup(r => r.GetByIdAsync(product.Id)).ReturnsAsync(product);
        var request = new SubmitReviewRequest { ProductId = product.Id, Rating = 5 };

        ResponseData<ReviewResponse> result = await CreateService().SubmitAsync(
            "customer@example.com", "Jane", "Doe", null, request, Guid.NewGuid(), "127.0.0.1", "ua");

        result.StatusCode.Should().Be(404);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SubmitAsync_DuplicateReview_Returns409()
    {
        var product = new Product { Id = Guid.NewGuid(), TenantId = _tenantId, Name = "Tee", BasePrice = 20m };
        var customer = new Customer { Id = Guid.NewGuid(), TenantId = _tenantId, Email = "customer@example.com" };
        _products.Setup(r => r.GetByIdAsync(product.Id)).ReturnsAsync(product);
        _customers.Setup(r => r.GetOrCreateByEmailAsync(_tenantId, customer.Email, "Jane", "Doe", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);
        _reviews.Setup(r => r.ExistsByCustomerAndProductAsync(_tenantId, customer.Id, product.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var request = new SubmitReviewRequest { ProductId = product.Id, Rating = 5, Title = "Great", Body = "Loved it" };

        ResponseData<ReviewResponse> result = await CreateService().SubmitAsync(
            customer.Email, "Jane", "Doe", null, request, Guid.NewGuid(), "127.0.0.1", "ua");

        result.StatusCode.Should().Be(409);
        _reviews.Verify(r => r.AddAsync(It.IsAny<Review>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
