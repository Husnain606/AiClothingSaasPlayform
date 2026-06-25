namespace FashionSaaS.Application.Configuration;

public class EncryptionSettings
{
    public const string SectionName = "EncryptionSettings";

    public string BankFieldKey { get; init; } = string.Empty;
}
