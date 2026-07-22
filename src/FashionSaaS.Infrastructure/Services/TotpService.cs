using System.Globalization;
using System.Security.Cryptography;
using FashionSaaS.Application.Interfaces;

namespace FashionSaaS.Infrastructure.Services;

public class TotpService : ITotpService
{
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
    private const int StepSeconds = 30;
    private const int CodeDigits = 6;

    public (string SecretBase32, string QrCodeUrl) GenerateSetup(string email, string issuer)
    {
        var secret = RandomNumberGenerator.GetBytes(20);
        var secretBase32 = Base32Encode(secret);
        var qrUrl = $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(email)}" +
                    $"?secret={secretBase32}&issuer={Uri.EscapeDataString(issuer)}";
        return (secretBase32, qrUrl);
    }

    // Hand-rolled RFC 6238 TOTP (HMAC-SHA1, 30s step, 6 digits) using only BCL crypto primitives.
    // OtpSharp (the prior implementation) throws TypeLoadException on
    // System.Security.Cryptography.MemoryProtectionScope under .NET 10 - it is fundamentally
    // incompatible with this runtime, not merely outdated. This replacement removes that
    // dependency entirely rather than patch around a broken library, per this project's
    // standing preference for a minimal hand-rolled BCL solution over a third-party package.
    public bool Verify(string secretBase32, string code)
    {
        if (string.IsNullOrEmpty(code) || code.Length != CodeDigits || !code.All(char.IsAsciiDigit))
            return false;

        var secret = Base32Decode(secretBase32);
        var currentStep = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / StepSeconds;

        // +/-1 step window (matches the prior VerificationWindow(1, 1) tolerance).
        for (var stepOffset = -1; stepOffset <= 1; stepOffset++)
        {
            if (string.Equals(ComputeTotp(secret, currentStep + stepOffset), code, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static string ComputeTotp(byte[] secret, long counter)
    {
        var counterBytes = BitConverter.GetBytes(counter);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(counterBytes);

        // CA5350 suppressed: HMAC-SHA1 is RFC 6238's mandated construction for TOTP, not a
        // discretionary hash choice — every standard TOTP authenticator (Google Authenticator,
        // Authy, etc.) relies on it. This is HMAC keyed-hashing for a one-time-code MAC, not
        // SHA-1 used for collision resistance, so the "weak algorithm" risk this rule targets
        // does not apply here.
#pragma warning disable CA5350
        using var hmac = new HMACSHA1(secret);
#pragma warning restore CA5350
        var hash = hmac.ComputeHash(counterBytes);

        var offset = hash[^1] & 0xF;
        var binaryCode = ((hash[offset] & 0x7F) << 24)
                         | ((hash[offset + 1] & 0xFF) << 16)
                         | ((hash[offset + 2] & 0xFF) << 8)
                         | (hash[offset + 3] & 0xFF);

        var code = binaryCode % (int)Math.Pow(10, CodeDigits);
        return code.ToString(CultureInfo.InvariantCulture).PadLeft(CodeDigits, '0');
    }

    // CA1308 suppressed: backup codes are deliberately displayed to the user in lowercase hex
    // — a display-format choice, not a security comparison key normalized against attack risk.
#pragma warning disable CA1308
    public IReadOnlyList<string> GenerateBackupCodes()
        => Enumerable.Range(0, 8)
            .Select(_ => Convert.ToHexString(RandomNumberGenerator.GetBytes(5)).ToLowerInvariant())
            .ToList();
#pragma warning restore CA1308

    private static string Base32Encode(byte[] data)
    {
        if (data.Length == 0)
            return string.Empty;

        var bits = 0;
        var value = 0;
        var result = new System.Text.StringBuilder();

        foreach (var b in data)
        {
            value = (value << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                bits -= 5;
                result.Append(Base32Alphabet[(value >> bits) & 31]);
            }
        }

        if (bits > 0)
            result.Append(Base32Alphabet[(value << (5 - bits)) & 31]);

        return result.ToString();
    }

    private static byte[] Base32Decode(string input)
    {
        if (string.IsNullOrEmpty(input))
            return [];

        var bits = 0;
        var value = 0;
        var result = new List<byte>();

        foreach (var c in input.ToUpperInvariant())
        {
            var idx = Base32Alphabet.IndexOf(c, StringComparison.Ordinal);
            if (idx < 0)
                throw new ArgumentException($"Invalid Base32 character: {c}", nameof(input));

            value = (value << 5) | idx;
            bits += 5;

            if (bits >= 8)
            {
                bits -= 8;
                result.Add((byte)((value >> bits) & 255));
            }
        }

        return result.ToArray();
    }
}
