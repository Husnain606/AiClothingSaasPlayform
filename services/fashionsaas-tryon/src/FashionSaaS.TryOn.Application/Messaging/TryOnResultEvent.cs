namespace FashionSaaS.TryOn.Application.Messaging;

/// <summary>
/// Published exactly once per TryOnRequest, on EITHER outcome (unlike the old success-only
/// TryOnCompletedEvent) — the main API's consumer needs to notify the customer of a failure
/// too, not just a success.
/// </summary>
#pragma warning disable CA1054
public record TryOnResultEvent(
    Guid TryOnRequestId,
    Guid TenantId,
    Guid CustomerId,
    Guid ProductId,
    DateTime CreatedAt,
    bool IsSuccess,
    string? ResultImageUrl,
    string? FailureReason);
#pragma warning restore CA1054
