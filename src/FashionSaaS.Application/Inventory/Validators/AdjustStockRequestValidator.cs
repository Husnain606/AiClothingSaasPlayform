using FashionSaaS.Application.Inventory.DTOs;
using FluentValidation;

namespace FashionSaaS.Application.Inventory.Validators;

public class AdjustStockRequestValidator : AbstractValidator<AdjustStockRequest>
{
    public AdjustStockRequestValidator()
    {
        RuleFor(x => x.VariantId)
            .NotEmpty().WithMessage("VariantId is required.");

        RuleFor(x => x.Delta)
            .NotEqual(0).WithMessage("Delta must be non-zero.");

        RuleFor(x => x.Reason)
            .IsInEnum().WithMessage("Reason must be a valid stock adjustment reason.");
    }
}
