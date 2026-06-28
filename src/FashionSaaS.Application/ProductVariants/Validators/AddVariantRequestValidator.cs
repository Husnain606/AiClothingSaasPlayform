using FashionSaaS.Application.ProductVariants.DTOs;
using FluentValidation;

namespace FashionSaaS.Application.ProductVariants.Validators;

public class AddVariantRequestValidator : AbstractValidator<AddVariantRequest>
{
    public AddVariantRequestValidator()
    {
        RuleFor(x => x.ProductId)
            .NotEmpty().WithMessage("ProductId is required.");

        RuleFor(x => x.Size)
            .NotEmpty().WithMessage("Size is required.")
            .MaximumLength(100).WithMessage("Size must not exceed 100 characters.");

        RuleFor(x => x.Color)
            .NotEmpty().WithMessage("Color is required.")
            .MaximumLength(100).WithMessage("Color must not exceed 100 characters.");

        RuleFor(x => x.Sku)
            .NotEmpty().WithMessage("Sku is required.")
            .MaximumLength(100).WithMessage("Sku must not exceed 100 characters.");

        RuleFor(x => x.StockQuantity)
            .GreaterThanOrEqualTo(0).WithMessage("StockQuantity must be zero or greater.");

        RuleFor(x => x.PriceOverride)
            .GreaterThanOrEqualTo(0).WithMessage("PriceOverride must be zero or greater.")
            .When(x => x.PriceOverride.HasValue);
    }
}
