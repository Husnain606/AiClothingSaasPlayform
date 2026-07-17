using System.ComponentModel.DataAnnotations;

namespace FashionSaaS.TryOn.Application.Messaging;

public class ServiceBusSettings
{
    public const string SectionName = "ServiceBusSettings";

    [Required]
    public string ConnectionString { get; init; } = string.Empty;

    [Required]
    public string TopicName { get; init; } = "tryon-events";
}
