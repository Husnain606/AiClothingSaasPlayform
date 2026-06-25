using System.ComponentModel.DataAnnotations;

namespace FashionSaaS.Application.Configuration;

public class CloudinarySettings
{
    public const string SectionName = "Cloudinary";

    [Required]
    public string CloudName { get; init; } = string.Empty;

    [Required]
    public string ApiKey { get; init; } = string.Empty;

    [Required]
    public string ApiSecret { get; init; } = string.Empty;
}
