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
}
