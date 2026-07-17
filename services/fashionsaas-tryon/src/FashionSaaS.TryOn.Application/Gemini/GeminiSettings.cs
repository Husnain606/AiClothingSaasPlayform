using System.ComponentModel.DataAnnotations;

namespace FashionSaaS.TryOn.Application.Gemini;

public class GeminiSettings
{
    public const string SectionName = "GeminiSettings";

    [Required]
    public string ApiKey { get; init; } = string.Empty;

    [Required]
    public string BaseUrl { get; init; } = "https://generativelanguage.googleapis.com";

    [Required]
    public string Model { get; init; } = "gemini-2.5-flash-image";

    /// <summary>
    /// Hostnames the client-supplied <c>GarmentImageUrl</c> is allowed to resolve to (SSRF guard —
    /// TryOnRequestFormValidator rejects any URL whose host isn't in this list). Defaults to Cloudinary's
    /// fixed secure-delivery host (see FashionSaaS.Infrastructure.Services.CloudinaryImageStorageService,
    /// which always produces URLs of the form "https://res.cloudinary.com/{cloud-name}/..." regardless
    /// of cloud name).
    /// </summary>
    [Required]
    public string[] AllowedGarmentImageHosts { get; init; } = ["res.cloudinary.com"];
}
