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
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IAuditLogService> _audit = new();
    private readonly Mock<ICurrentTenantService> _tenant = new();
    private readonly Guid _tenantId = Guid.NewGuid();

    public ReviewServiceTests()
    {
        _tenant.SetupGet(t => t.TenantId).Returns(_tenantId);
    }

    private ReviewService CreateService() => new(
        _reviews.Object, _uow.Object, _audit.Object, _tenant.Object,
        NullLogger<ReviewService>.Instance);

    private Review Review(ReviewStatus status = ReviewStatus.Pending) => new()
    {
        Id = Guid.NewGuid(), TenantId = _tenantId, ProductId = Guid.NewGuid(), CustomerId = Guid.NewGuid(),
        Rating = 5, Status = status
    };

    // ── Approve ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ApproveAsync_PendingReview_TransitionsAndRaisesEventBeforeSave()
    {
        var review = Review();
        _reviews.Setup(r => r.GetByIdAsync(review.Id)).ReturnsAsync(review);
        var eventPresentAtSave = false;
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback(() => eventPresentAtSave = review.DomainEvents.Any(e => e is ReviewModeratedEvent))
            .ReturnsAsync(1);

        var result = await CreateService().ApproveAsync(review.Id, Guid.NewGuid(), "127.0.0.1", "ua");

        result.IsSuccess.Should().BeTrue();
        review.Status.Should().Be(ReviewStatus.Approved);
        eventPresentAtSave.Should().BeTrue("the ReviewModeratedEvent must be added before SaveChanges");
        review.DomainEvents.OfType<ReviewModeratedEvent>().Single().Status.Should().Be(ReviewStatus.Approved);
    }

    [Fact]
    public async Task ApproveAsync_AlreadyApproved_Returns409()
    {
        var review = Review(ReviewStatus.Approved);
        _reviews.Setup(r => r.GetByIdAsync(review.Id)).ReturnsAsync(review);

        var result = await CreateService().ApproveAsync(review.Id, Guid.NewGuid(), "127.0.0.1", "ua");

        result.StatusCode.Should().Be(409);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Reject ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RejectAsync_PendingReview_TransitionsAndRaisesEvent()
    {
        var review = Review();
        _reviews.Setup(r => r.GetByIdAsync(review.Id)).ReturnsAsync(review);

        var result = await CreateService().RejectAsync(review.Id,
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

        var result = await CreateService().RejectAsync(review.Id,
            new RejectReviewRequest { Reason = "x" }, Guid.NewGuid(), "127.0.0.1", "ua");

        result.StatusCode.Should().Be(404);
    }

    // ── Delete ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_RemovesReview()
    {
        var review = Review();
        _reviews.Setup(r => r.GetByIdAsync(review.Id)).ReturnsAsync(review);

        var result = await CreateService().DeleteAsync(review.Id, Guid.NewGuid(), "127.0.0.1", "ua");

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

        var result = await CreateService().GetAllAsync(filter);

        result.IsSuccess.Should().BeTrue();
        result.Data!.TotalCount.Should().Be(3);
        result.Data.Items.Should().HaveCount(1);
    }
}
