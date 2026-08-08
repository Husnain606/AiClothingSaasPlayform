using System.ComponentModel.DataAnnotations;

namespace FashionSaaS.TryOn.Application.HuggingFace;

public class HuggingFaceSettings
{
    public const string SectionName = "HuggingFaceSettings";

    /// <summary>Base URL of your own duplicated Space, e.g. https://your-username-your-space.hf.space</summary>
    [Required]
    public string SpaceUrl { get; init; } = string.Empty;

    [Required]
    public string ApiToken { get; init; } = string.Empty;
}
