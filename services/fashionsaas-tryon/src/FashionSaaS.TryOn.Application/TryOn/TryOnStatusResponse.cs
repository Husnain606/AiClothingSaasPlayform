namespace FashionSaaS.TryOn.Application.TryOn;

#pragma warning disable CA1054
public record TryOnStatusResponse(string Status, string? ResultImageUrl, string? FailureReason);
#pragma warning restore CA1054
