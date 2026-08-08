using System.ComponentModel.DataAnnotations;

namespace FashionSaaS.TryOn.Application.Gemini;

public class GeminiSettings
{
    public const string SectionName = "GeminiSettings";

    [Required]
    public string ApiKey { get; init; } = string.Empty;

    [Required]
    public string BaseUrl { get; init; } = "https://generativelanguage.googleapis.com";

    /// <summary>
    /// Model used for text-generation calls (chatbot replies, and measurement's structured-JSON
    /// response). Decided default, not provisional — confirmed against Google's current model
    /// catalog (design spec §7).
    /// </summary>
    [Required]
    public string TextModel { get; init; } = "gemini-2.5-flash";

    /// <summary>
    /// Total character budget across the client-held chat history sent on each <c>/api/chat</c>
    /// call (design spec §6.2), on top of the fixed "last 20 messages" cap. Decided default, not
    /// provisional — configurable per-tenant/per-deployment via this setting.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int ChatHistoryMaxTotalChars { get; init; } = 8_000;

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
