using FashionSaaS.Application.Common;
using FashionSaaS.Application.Interfaces;
using FashionSaaS.Application.Reviews.DTOs;
using FashionSaaS.Domain.Entities;
using FashionSaaS.Domain.Enums;
using FashionSaaS.Domain.Events;
using Microsoft.Extensions.Logging;

namespace FashionSaaS.Application.Reviews;

/// <summary>
/// Review moderation. Input-shape validation (rejection reason shape) is handled by
/// FluentValidation at the API boundary (CONVENTIONS §8); this service enforces the
/// moderation business rules: tenant scoping and status transitions. Approve and Reject
/// flip <see cref="ReviewStatus"/> on a tracked entity and raise a
/// <see cref="ReviewModeratedEvent"/> BEFORE SaveChanges so the UnitOfWork dispatches it
/// as part of the same commit. Only Approved reviews are surfaced to the storefront
/// (Phase 3) — GetAll exposes a status filter so moderators can list pending ones.
/// Customer-submitted creation arrives in Phase 3.
/// </summary>
public class ReviewService(
    IReviewRepository reviewRepository,
    IProductRepository productRepository,
    ICustomerRepository customerRepository,
    IUnitOfWork unitOfWork,
    IAuditLogService auditLogService,
    ICurrentTenantService currentTenant,
    ILogger<ReviewService> logger)
{
    /// <summary>
    /// Customer-facing review submission (Phase 7). Resolves/creates the tenant-scoped
    /// <see cref="Customer"/> record from the authenticated user's email — the same
    /// get-or-create pattern <c>OrderService.CreateAsync</c> uses — because
    /// <see cref="Review.CustomerId"/> references <see cref="Customer"/>, not the JWT
    /// user id directly. Creates the review in <see cref="ReviewStatus.Pending"/> (this
    /// codebase's existing moderation entry point — see <see cref="ApproveAsync"/>/
    /// <see cref="RejectAsync"/>) and raises <see cref="ReviewSubmittedEvent"/> before
    /// SaveChanges so it rides the same commit.
    /// </summary>
    public async Task<ResponseData<ReviewResponse>> SubmitAsync(string customerEmail, string customerFirstName,
        string customerLastName, string? customerPhone, SubmitReviewRequest request, Guid actingUserId,
        string ipAddress, string userAgent, CancellationToken ct = default)
    {
        if (currentTenant.TenantId is not { } tenantId)
            return ResponseData<ReviewResponse>.Failure("Tenant could not be resolved.", 400);

        Product? product = await productRepository.GetByIdAsync(request.ProductId);
        if (product is null || product.TenantId != tenantId)
            return ResponseData<ReviewResponse>.Failure("Product not found.", 404);

        Customer customer = await customerRepository.GetOrCreateByEmailAsync(
            tenantId, customerEmail, customerFirstName, customerLastName, customerPhone, ct);

        var alreadyReviewed = await reviewRepository.ExistsByCustomerAndProductAsync(
            tenantId, customer.Id, request.ProductId, ct);
        if (alreadyReviewed)
            return ResponseData<ReviewResponse>.Failure("You have already reviewed this product.", 409);

        var review = new Review
        {
            TenantId = tenantId,
            ProductId = request.ProductId,
            CustomerId = customer.Id,
            Rating = request.Rating,
            Title = request.Title,
            Body = request.Body,
            Status = ReviewStatus.Pending
        };
        review.AddDomainEvent(new ReviewSubmittedEvent(review.Id, tenantId, request.ProductId, request.Rating));

        await reviewRepository.AddAsync(review);
        await unitOfWork.SaveChangesAsync(ct);

        await auditLogService.LogAsync(actingUserId, tenantId, "ReviewSubmitted", "Review", review.Id,
            null, new { review.ProductId, review.Rating }, ipAddress, userAgent);

        logger.LogInformation("Review {ReviewId} submitted for product {ProductId}", review.Id, request.ProductId);
        return ResponseData<ReviewResponse>.Success(MapToResponse(review), "Review submitted.", 201);
    }
    public async Task<ResponseData<ReviewResponse>> ApproveAsync(Guid id,
        Guid moderatedByUserId, string ipAddress, string userAgent, CancellationToken ct = default)
    {
        if (currentTenant.TenantId is not { } tenantId)
            return ResponseData<ReviewResponse>.Failure("Tenant could not be resolved.", 400);

        // Tracked load so the status mutation and the domain event flow through SaveChanges.
        Review? review = await reviewRepository.GetByIdAsync(id);
        if (review is null || review.TenantId != tenantId)
            return ResponseData<ReviewResponse>.Failure("Review not found.", 404);

        if (review.Status == ReviewStatus.Approved)
            return ResponseData<ReviewResponse>.Failure("Review is already approved.", 409);

        ReviewStatus previous = review.Status;
        review.Status = ReviewStatus.Approved;
        review.AddDomainEvent(new ReviewModeratedEvent(review.Id, tenantId, ReviewStatus.Approved));

        await reviewRepository.UpdateAsync(review);
        await unitOfWork.SaveChangesAsync(ct);

        await auditLogService.LogAsync(moderatedByUserId, tenantId, "ReviewApproved", "Review", review.Id,
            new { Status = previous }, new { Status = ReviewStatus.Approved }, ipAddress, userAgent);

        logger.LogInformation("Review {ReviewId} approved for tenant {TenantId}", review.Id, tenantId);
        return ResponseData<ReviewResponse>.Success(MapToResponse(review), "Review approved.");
    }

    public async Task<ResponseData<ReviewResponse>> RejectAsync(Guid id, RejectReviewRequest request,
        Guid moderatedByUserId, string ipAddress, string userAgent, CancellationToken ct = default)
    {
        if (currentTenant.TenantId is not { } tenantId)
            return ResponseData<ReviewResponse>.Failure("Tenant could not be resolved.", 400);

        Review? review = await reviewRepository.GetByIdAsync(id);
        if (review is null || review.TenantId != tenantId)
            return ResponseData<ReviewResponse>.Failure("Review not found.", 404);

        if (review.Status == ReviewStatus.Rejected)
            return ResponseData<ReviewResponse>.Failure("Review is already rejected.", 409);

        ReviewStatus previous = review.Status;
        review.Status = ReviewStatus.Rejected;
        review.AddDomainEvent(new ReviewModeratedEvent(review.Id, tenantId, ReviewStatus.Rejected));

        await reviewRepository.UpdateAsync(review);
        await unitOfWork.SaveChangesAsync(ct);

        await auditLogService.LogAsync(moderatedByUserId, tenantId, "ReviewRejected", "Review", review.Id,
            new { Status = previous }, new { Status = ReviewStatus.Rejected, request.Reason }, ipAddress, userAgent);

        logger.LogInformation("Review {ReviewId} rejected for tenant {TenantId}", review.Id, tenantId);
        return ResponseData<ReviewResponse>.Success(MapToResponse(review), "Review rejected.");
    }

    public async Task<ResponseData<bool>> DeleteAsync(Guid id,
        Guid deletedByUserId, string ipAddress, string userAgent, CancellationToken ct = default)
    {
        if (currentTenant.TenantId is not { } tenantId)
            return ResponseData<bool>.Failure("Tenant could not be resolved.", 400);

        Review? review = await reviewRepository.GetByIdAsync(id);
        if (review is null || review.TenantId != tenantId)
            return ResponseData<bool>.Failure("Review not found.", 404);

        await reviewRepository.DeleteAsync(review);
        await unitOfWork.SaveChangesAsync(ct);

        await auditLogService.LogAsync(deletedByUserId, tenantId, "ReviewDeleted", "Review", review.Id,
            new { review.ProductId, review.CustomerId, review.Rating, review.Status }, null, ipAddress, userAgent);

        logger.LogInformation("Review {ReviewId} deleted for tenant {TenantId}", review.Id, tenantId);
        return ResponseData<bool>.Success(true, "Review deleted.");
    }

    public async Task<ResponseData<PagedResult<ReviewResponse>>> GetAllAsync(ReviewFilter filter,
        CancellationToken ct = default)
    {
        if (currentTenant.TenantId is not { } tenantId)
            return ResponseData<PagedResult<ReviewResponse>>.Failure("Tenant could not be resolved.", 400);

        // Enforce tenant scope regardless of the inbound filter value. The optional
        // Status filter lets moderators list Pending reviews (storefront sees Approved only).
        filter.TenantId = tenantId;

        (IReadOnlyList<Review>? items, var total) = await reviewRepository.GetPagedAsync(filter, ct);

        var page = new PagedResult<ReviewResponse>
        {
            Items = items.Select(MapToResponse).ToList(),
            TotalCount = total,
            Page = filter.Page,
            PageSize = filter.PageSize
        };

        return ResponseData<PagedResult<ReviewResponse>>.Success(page);
    }

    public async Task<ResponseData<ReviewResponse>> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        if (currentTenant.TenantId is not { } tenantId)
            return ResponseData<ReviewResponse>.Failure("Tenant could not be resolved.", 400);

        Review? review = await reviewRepository.GetByIdAsync(id);
        if (review is null || review.TenantId != tenantId)
            return ResponseData<ReviewResponse>.Failure("Review not found.", 404);

        return ResponseData<ReviewResponse>.Success(MapToResponse(review));
    }

    private static ReviewResponse MapToResponse(Review r) => new()
    {
        Id = r.Id,
        ProductId = r.ProductId,
        CustomerId = r.CustomerId,
        Rating = r.Rating,
        Title = r.Title,
        Body = r.Body,
        Status = r.Status,
        CreatedAt = r.CreatedAt
    };
}
