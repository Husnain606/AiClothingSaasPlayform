using FashionSaaS.Application.Reviews.DTOs;
using FluentValidation;

namespace FashionSaaS.Application.Reviews.Validators;

public class RejectReviewRequestValidator : AbstractValidator<RejectReviewRequest>
{
    public RejectReviewRequestValidator()
    {
        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("Reason is required.")
            .MaximumLength(500).WithMessage("Reason must not exceed 500 characters.");
    }
}
