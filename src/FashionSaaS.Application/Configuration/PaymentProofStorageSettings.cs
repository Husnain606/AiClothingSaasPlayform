using System.ComponentModel.DataAnnotations;

namespace FashionSaaS.Application.Configuration;

public class PaymentProofStorageSettings
{
    public const string SectionName = "PaymentProofStorage";

    /// <summary>
    /// Root directory for locally stored payment proofs. Relative paths resolve against the
    /// content root. Ignored once an Azure Blob implementation replaces the local one.
    /// </summary>
    [Required]
    public string RootPath { get; init; } = string.Empty;

    [Range(1, 104857600)]
    public long MaxFileSizeBytes { get; init; } = 10485760;
}
