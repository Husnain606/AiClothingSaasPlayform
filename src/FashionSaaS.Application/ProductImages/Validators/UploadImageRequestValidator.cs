using FashionSaaS.Application.ProductImages.DTOs;
using FluentValidation;

namespace FashionSaaS.Application.ProductImages.Validators;

/// <summary>
/// Validates the metadata shape of an image upload only. The actual file content-type and
/// size limits are enforced at the API boundary in Task 18 (the controller inspects the
/// IFormFile before handing a stream to the service).
/// </summary>
public class UploadImageRequestValidator : AbstractValidator<UploadImageRequest>
{
    public UploadImageRequestValidator()
    {
        RuleFor(x => x.ProductId)
            .NotEmpty().WithMessage("ProductId is required.");

        RuleFor(x => x.AltText)
            .MaximumLength(500).WithMessage("AltText must not exceed 500 characters.")
            .When(x => x.AltText is not null);
    }
}
