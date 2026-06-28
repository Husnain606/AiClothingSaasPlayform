using FashionSaaS.Application.Discounts.DTOs;
using FashionSaaS.Domain.Enums;
using FluentValidation;

namespace FashionSaaS.Application.Discounts.Validators;

public class UpdateDiscountRequestValidator : AbstractValidator<UpdateDiscountRequest>
{
    public UpdateDiscountRequestValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("Code is required.")
            .MaximumLength(100).WithMessage("Code must not exceed 100 characters.");

        RuleFor(x => x.Type)
            .IsInEnum().WithMessage("Type must be a valid discount type.");

        RuleFor(x => x.Value)
            .GreaterThan(0).WithMessage("Value must be greater than zero.");

        // Cross-field: a percentage discount cannot exceed 100%.
        RuleFor(x => x.Value)
            .LessThanOrEqualTo(100)
            .When(x => x.Type == DiscountType.Percentage)
            .WithMessage("Percentage discount value must not exceed 100.");

        RuleFor(x => x.MinOrderAmount)
            .GreaterThanOrEqualTo(0).WithMessage("MinOrderAmount must be zero or greater.")
            .When(x => x.MinOrderAmount.HasValue);

        RuleFor(x => x.MaxRedemptions)
            .GreaterThanOrEqualTo(1).WithMessage("MaxRedemptions must be at least 1.")
            .When(x => x.MaxRedemptions.HasValue);

        // Cross-field: the active window must be a valid range.
        RuleFor(x => x.StartsAt)
            .LessThan(x => x.EndsAt).WithMessage("StartsAt must be before EndsAt.");
    }
}
