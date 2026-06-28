using FashionSaaS.Application.ProductImages.DTOs;
using FluentValidation;

namespace FashionSaaS.Application.ProductImages.Validators;

public class ReorderImagesRequestValidator : AbstractValidator<ReorderImagesRequest>
{
    public ReorderImagesRequestValidator()
    {
        RuleFor(x => x.Ids)
            .NotEmpty().WithMessage("At least one image id is required.");
    }
}
