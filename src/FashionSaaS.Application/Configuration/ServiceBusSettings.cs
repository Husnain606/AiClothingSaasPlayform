using System.ComponentModel.DataAnnotations;

namespace FashionSaaS.Application.Configuration;

/// <summary>
/// Connection details for the try-on results subscription this API consumes. Deliberately a
/// separate class from the try-on microservice's same-named settings type — the two services are
/// independent deployables with no shared assembly, so each owns its own binding.
/// </summary>
public class ServiceBusSettings
{
    public const string SectionName = "ServiceBusSettings";

    [Required]
    public string ConnectionString { get; init; } = string.Empty;

    [Required]
    public string TopicName { get; init; } = "tryon-events";

    [Required]
    public string SubscriptionName { get; init; } = "main-api-tryon-results";
}
