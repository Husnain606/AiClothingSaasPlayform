using FluentValidation;

namespace FashionSaaS.TryOn.Application.TryOn;

public class TryOnRequestFormValidator : AbstractValidator<TryOnRequestForm>
{
    private static readonly string[] AllowedContentTypes = ["image/jpeg", "image/png"];
    private const long MaxPhotoBytes = 10 * 1024 * 1024; // 10 MB

    public TryOnRequestFormValidator()
    {
        RuleFor(x => x.Photo)
            .Must(f => AllowedContentTypes.Contains(f.ContentType))
            .WithMessage("Photo must be a JPEG or PNG image.")
            .Must(f => f.Length > 0 && f.Length <= MaxPhotoBytes)
            .WithMessage("Photo must be between 1 byte and 10 MB.");

        RuleFor(x => x.GarmentImageUrl)
            .NotEmpty()
            .Must(url => Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
            .WithMessage("GarmentImageUrl must be a valid HTTPS URL.");

        RuleFor(x => x.ProductId).NotEmpty();
    }
}
