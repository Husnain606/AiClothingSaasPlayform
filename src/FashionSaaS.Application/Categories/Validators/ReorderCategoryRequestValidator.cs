using FashionSaaS.Application.Categories.DTOs;
using FluentValidation;

namespace FashionSaaS.Application.Categories.Validators;

public class ReorderCategoryRequestValidator : AbstractValidator<ReorderCategoryRequest>
{
    public ReorderCategoryRequestValidator()
    {
        RuleFor(x => x.Items)
            .NotEmpty().WithMessage("At least one category order item is required.");

        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.Id)
                .NotEmpty().WithMessage("Category Id is required.");
            item.RuleFor(i => i.SortOrder)
                .GreaterThanOrEqualTo(0).WithMessage("SortOrder must be zero or greater.");
        });
    }
}
