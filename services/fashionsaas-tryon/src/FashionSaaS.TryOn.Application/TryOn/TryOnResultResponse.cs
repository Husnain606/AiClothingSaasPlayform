namespace FashionSaaS.TryOn.Application.TryOn;

// CA1054 suppressed: ResultImageDataUri is a `data:` URI carrying a multi-megabyte base64 image
// payload as a plain string, serialized straight into the JSON API response — modeling it as
// System.Uri would add no value (nothing parses its Scheme/Host) and forces an unnecessary
// string<->Uri round trip for a payload this large — 2026-07-17.
#pragma warning disable CA1054
public record TryOnResultResponse(string ResultImageDataUri);
#pragma warning restore CA1054
