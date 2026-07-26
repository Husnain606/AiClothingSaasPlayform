using System.Text;

namespace FashionSaaS.Application.Orders;

/// <summary>
/// The allowlist of accepted payment-proof file types, plus magic-number verification.
/// A client-declared Content-Type is not trusted on its own: <see cref="HeaderMatches"/>
/// confirms the file's leading bytes actually match the declared type, so a renamed
/// executable can never reach storage. Allowlist, never a blocklist.
/// </summary>
public static class PaymentProofContentTypes
{
    /// <summary>Maximum accepted proof size (10 MB).</summary>
    public const long MaxFileSizeBytes = 10485760;

    public const string Jpeg = "image/jpeg";
    public const string Png = "image/png";
    public const string Webp = "image/webp";
    public const string Pdf = "application/pdf";

    private static readonly Dictionary<string, string> ExtensionsByContentType =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [Jpeg] = ".jpg",
            [Png] = ".png",
            [Webp] = ".webp",
            [Pdf] = ".pdf"
        };

    public static bool IsAllowed(string? contentType)
        => contentType is not null && ExtensionsByContentType.ContainsKey(contentType);

    public static string ExtensionFor(string contentType)
        => ExtensionsByContentType.TryGetValue(contentType, out var extension)
            ? extension
            : throw new ArgumentOutOfRangeException(nameof(contentType), "Unsupported payment proof content type.");

    /// <summary>
    /// True when <paramref name="header"/> (the file's leading bytes) carries the signature
    /// expected for <paramref name="contentType"/>. Pass at least the first 12 bytes.
    /// </summary>
    public static bool HeaderMatches(ReadOnlySpan<byte> header, string contentType)
    {
        if (!IsAllowed(contentType))
            return false;

        if (string.Equals(contentType, Jpeg, StringComparison.OrdinalIgnoreCase))
            return header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF;

        if (string.Equals(contentType, Png, StringComparison.OrdinalIgnoreCase))
        {
            return header.Length >= 8
                   && header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47
                   && header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A;
        }

        if (string.Equals(contentType, Webp, StringComparison.OrdinalIgnoreCase))
        {
            return header.Length >= 12
                   && StartsWithAscii(header, "RIFF")
                   && StartsWithAscii(header[8..], "WEBP");
        }

        // Pdf
        return header.Length >= 4 && StartsWithAscii(header, "%PDF");
    }

    private static bool StartsWithAscii(ReadOnlySpan<byte> bytes, string ascii)
    {
        if (bytes.Length < ascii.Length)
            return false;

        Span<byte> expected = stackalloc byte[ascii.Length];
        Encoding.ASCII.GetBytes(ascii, expected);
        return bytes[..ascii.Length].SequenceEqual(expected);
    }
}
