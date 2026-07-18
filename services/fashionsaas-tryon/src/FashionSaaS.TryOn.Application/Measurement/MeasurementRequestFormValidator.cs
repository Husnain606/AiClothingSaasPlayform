using FluentValidation;

namespace FashionSaaS.TryOn.Application.Measurement;

public class MeasurementRequestFormValidator : AbstractValidator<MeasurementRequestForm>
{
    private static readonly string[] AllowedContentTypes = ["image/jpeg", "image/png"];
    private const long MaxPhotoBytes = 10 * 1024 * 1024; // 10 MB, matches TryOnRequestFormValidator

    public MeasurementRequestFormValidator()
    {
        RuleFor(x => x.Photo)
            .Must(f => AllowedContentTypes.Contains(f.ContentType))
            .WithMessage("Photo must be a JPEG or PNG image.")
            .Must(f => f.Length > 0 && f.Length <= MaxPhotoBytes)
            .WithMessage("Photo must be between 1 byte and 10 MB.");

        RuleFor(x => x.HeightCm)
            .InclusiveBetween(50, 250)
            .When(x => x.HeightCm.HasValue)
            .WithMessage("HeightCm must be between 50 and 250 if provided.");
    }
}
