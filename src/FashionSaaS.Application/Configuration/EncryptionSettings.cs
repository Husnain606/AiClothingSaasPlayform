using System.ComponentModel.DataAnnotations;

namespace FashionSaaS.Application.Configuration;

public class EncryptionSettings
{
    public const string SectionName = "EncryptionSettings";

    [Required]
    public string BankFieldKey { get; init; } = string.Empty;
}
