using FashionSaaS.TryOn.Application.Gemini;
using FluentValidation;
using Microsoft.Extensions.Options;

namespace FashionSaaS.TryOn.Application.TryOn;

public class TryOnRequestFormValidator : AbstractValidator<TryOnRequestForm>
{
    private static readonly string[] AllowedContentTypes = ["image/jpeg", "image/png"];
    private const long MaxPhotoBytes = 10 * 1024 * 1024; // 10 MB

    public TryOnRequestFormValidator(IOptions<GeminiSettings> geminiOptions)
    {
        var allowedGarmentImageHosts = geminiOptions.Value.AllowedGarmentImageHosts;

        RuleFor(x => x.Photo)
            .Must(f => AllowedContentTypes.Contains(f.ContentType))
            .WithMessage("Photo must be a JPEG or PNG image.")
            .Must(f => f.Length > 0 && f.Length <= MaxPhotoBytes)
            .WithMessage("Photo must be between 1 byte and 10 MB.");

        // SSRF guard: an absolute-HTTPS check alone lets an authenticated client point this server-side
        // fetch at arbitrary hosts (internal or external). The host must also match a configured allowlist
        // (production default: Cloudinary's fixed delivery host — see GeminiSettings.AllowedGarmentImageHosts).
        RuleFor(x => x.GarmentImageUrl)
            .NotEmpty()
            .Must(url => Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
            .WithMessage("GarmentImageUrl must be a valid HTTPS URL.")
            .Must(url => Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
                         && allowedGarmentImageHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase))
            .WithMessage("GarmentImageUrl host is not in the allowed list of image hosts.");

        RuleFor(x => x.ProductId).NotEmpty();
    }
}
