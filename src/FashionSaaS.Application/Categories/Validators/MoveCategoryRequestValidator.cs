using FashionSaaS.Application.Categories.DTOs;
using FluentValidation;

namespace FashionSaaS.Application.Categories.Validators;

public class MoveCategoryRequestValidator : AbstractValidator<MoveCategoryRequest>
{
    public MoveCategoryRequestValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("Category Id is required.");

        // NewParentId may be null (move to root). When supplied it must differ from Id;
        // full cycle detection (descendant check) is a service-layer business rule.
        RuleFor(x => x.NewParentId)
            .NotEqual(x => x.Id).When(x => x.NewParentId.HasValue)
            .WithMessage("A category cannot be its own parent.");
    }
}
