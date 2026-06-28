using FashionSaaS.Application.Categories.DTOs;
using FluentValidation;

namespace FashionSaaS.Application.Categories.Validators;

public class CreateCategoryRequestValidator : AbstractValidator<CreateCategoryRequest>
{
    // Lowercase alphanumeric words separated by single hyphens.
    private const string SlugPattern = "^[a-z0-9]+(?:-[a-z0-9]+)*$";

    public CreateCategoryRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required.")
            .MaximumLength(200).WithMessage("Name must not exceed 200 characters.");

        RuleFor(x => x.Slug)
            .NotEmpty().WithMessage("Slug is required.")
            .MaximumLength(200).WithMessage("Slug must not exceed 200 characters.")
            .Matches(SlugPattern)
            .WithMessage("Slug must be lowercase alphanumeric with single hyphens (e.g. 'mens-shoes').");

        RuleFor(x => x.Description)
            .MaximumLength(2000).WithMessage("Description must not exceed 2000 characters.");

        RuleFor(x => x.SortOrder)
            .GreaterThanOrEqualTo(0).WithMessage("SortOrder must be zero or greater.");
    }
}
