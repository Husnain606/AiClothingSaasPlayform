using FashionSaaS.Application.Reviews.DTOs;
using FashionSaaS.Application.Reviews.Validators;
using FluentAssertions;

namespace FashionSaaS.Application.Tests.Reviews;

public class ReviewValidatorTests
{
    private readonly RejectReviewRequestValidator _reject = new();

    [Fact]
    public void Reject_WithReason_Passes()
        => _reject.Validate(new RejectReviewRequest { Reason = "spam" }).IsValid.Should().BeTrue();

    [Fact]
    public void Reject_BlankReason_Fails()
        => _reject.Validate(new RejectReviewRequest { Reason = "" })
            .Errors.Should().Contain(e => e.PropertyName == nameof(RejectReviewRequest.Reason));

    [Fact]
    public void Reject_LongReason_Fails()
        => _reject.Validate(new RejectReviewRequest { Reason = new string('x', 501) })
            .IsValid.Should().BeFalse();

    private readonly SubmitReviewRequestValidator _submit = new();

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    public void Submit_RatingOutOfRange_Fails(int rating)
        => _submit.Validate(new SubmitReviewRequest { ProductId = Guid.NewGuid(), Rating = rating })
            .Errors.Should().Contain(e => e.PropertyName == nameof(SubmitReviewRequest.Rating));

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    public void Submit_RatingInRange_Passes(int rating)
        => _submit.Validate(new SubmitReviewRequest { ProductId = Guid.NewGuid(), Rating = rating })
            .IsValid.Should().BeTrue();

    [Fact]
    public void Submit_MissingProductId_Fails()
        => _submit.Validate(new SubmitReviewRequest { ProductId = Guid.Empty, Rating = 5 })
            .Errors.Should().Contain(e => e.PropertyName == nameof(SubmitReviewRequest.ProductId));

    [Fact]
    public void Submit_LongTitle_Fails()
        => _submit.Validate(new SubmitReviewRequest { ProductId = Guid.NewGuid(), Rating = 5, Title = new string('x', 201) })
            .IsValid.Should().BeFalse();

    [Fact]
    public void Submit_LongBody_Fails()
        => _submit.Validate(new SubmitReviewRequest { ProductId = Guid.NewGuid(), Rating = 5, Body = new string('x', 2001) })
            .IsValid.Should().BeFalse();
}
