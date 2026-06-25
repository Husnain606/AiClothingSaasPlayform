using System.Security.Cryptography;
using FashionSaaS.Application.Interfaces;
using OtpSharp;

namespace FashionSaaS.Infrastructure.Services;

public class TotpService : ITotpService
{
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public (string SecretBase32, string QrCodeUrl) GenerateSetup(string email, string issuer)
    {
        var secret = KeyGeneration.GenerateRandomKey(20);
        var secretBase32 = Base32Encode(secret);
        var qrUrl = $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(email)}" +
                    $"?secret={secretBase32}&issuer={Uri.EscapeDataString(issuer)}";
        return (secretBase32, qrUrl);
    }

    public bool Verify(string secretBase32, string code)
    {
        var secret = Base32Decode(secretBase32);
        var totp = new Totp(secret);
        return totp.VerifyTotp(code, out _, new VerificationWindow(1, 1));
    }

    public IReadOnlyList<string> GenerateBackupCodes()
        => Enumerable.Range(0, 8)
            .Select(_ => Convert.ToHexString(RandomNumberGenerator.GetBytes(5)).ToLower())
            .ToList();

    private static string Base32Encode(byte[] data)
    {
        if (data.Length == 0) return string.Empty;

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
        if (string.IsNullOrEmpty(input)) return [];

        var bits = 0;
        var value = 0;
        var result = new List<byte>();

        foreach (var c in input.ToUpperInvariant())
        {
            var idx = Base32Alphabet.IndexOf(c);
            if (idx < 0) throw new ArgumentException($"Invalid Base32 character: {c}");

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
