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
}
