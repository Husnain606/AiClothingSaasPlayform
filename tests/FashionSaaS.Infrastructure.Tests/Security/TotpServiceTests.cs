using FashionSaaS.Infrastructure.Services;
using FluentAssertions;

namespace FashionSaaS.Infrastructure.Tests.Security;

public class TotpServiceTests
{
    private readonly TotpService _service = new();

    [Fact]
    public void GenerateSetup_ReturnsNonEmptySecretAndUrl()
    {
        (var secret, var url) = _service.GenerateSetup("admin@test.com", "FashionSaaS");
        secret.Should().NotBeEmpty();
        url.Should().StartWith("otpauth://totp/");
    }

    [Fact]
    public void GenerateBackupCodes_Returns8Codes()
        => _service.GenerateBackupCodes().Should().HaveCount(8);

    [Fact]
    public void GenerateBackupCodes_AllUnique()
    {
        IReadOnlyList<string> codes = _service.GenerateBackupCodes();
        codes.Distinct(StringComparer.Ordinal).Should().HaveCount(8);
    }

    // Regression test for a real production incident: the prior TOTP implementation (OtpSharp)
    // threw TypeLoadException on System.Security.Cryptography.MemoryProtectionScope under .NET
    // 10 - Verify() had never actually been exercised by a test (only GenerateSetup/
    // GenerateBackupCodes were), so the break shipped undetected until a real end-to-end login
    // attempt hit it. These tests independently reimplement RFC 6238 (not by calling into
    // TotpService's own algorithm) so the test can't pass merely because it agrees with itself.
    [Fact]
    public void Verify_ValidCurrentCode_ReturnsTrue()
    {
        (var secretBase32, _) = _service.GenerateSetup("admin@test.com", "FashionSaaS");
        var code = ComputeReferenceTotp(secretBase32, DateTimeOffset.UtcNow);

        _service.Verify(secretBase32, code).Should().BeTrue();
    }

    [Fact]
    public void Verify_WrongCode_ReturnsFalse()
    {
        (var secretBase32, _) = _service.GenerateSetup("admin@test.com", "FashionSaaS");

        _service.Verify(secretBase32, "000000").Should().BeFalse();
    }

    [Fact]
    public void Verify_CodeFromDifferentSecret_ReturnsFalse()
    {
        (var secretBase32, _) = _service.GenerateSetup("admin@test.com", "FashionSaaS");
        (var otherSecretBase32, _) = _service.GenerateSetup("other@test.com", "FashionSaaS");
        var codeForOtherSecret = ComputeReferenceTotp(otherSecretBase32, DateTimeOffset.UtcNow);

        _service.Verify(secretBase32, codeForOtherSecret).Should().BeFalse();
    }

    // Independent RFC 6238 reference implementation (HMAC-SHA1, 30s step, 6 digits) - a
    // deliberately separate code path from TotpService's own Verify, so these tests validate
    // against the standard, not against TotpService's own possibly-broken interpretation of it.
    private static string ComputeReferenceTotp(string secretBase32, DateTimeOffset at)
    {
        var secret = Base32Decode(secretBase32);
        var counter = at.ToUnixTimeSeconds() / 30L;
        var counterBytes = BitConverter.GetBytes(counter);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(counterBytes);

        // CA5350 suppressed: HMAC-SHA1 is RFC 6238's mandated construction for TOTP - see the
        // identical justification on TotpService.ComputeTotp.
#pragma warning disable CA5350
        using var hmac = new System.Security.Cryptography.HMACSHA1(secret);
#pragma warning restore CA5350
        var hash = hmac.ComputeHash(counterBytes);
        var offset = hash[^1] & 0xF;
        var binaryCode = ((hash[offset] & 0x7F) << 24)
                         | ((hash[offset + 1] & 0xFF) << 16)
                         | ((hash[offset + 2] & 0xFF) << 8)
                         | (hash[offset + 3] & 0xFF);

        return (binaryCode % 1_000_000).ToString(System.Globalization.CultureInfo.InvariantCulture).PadLeft(6, '0');
    }

    private static byte[] Base32Decode(string input)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bits = 0;
        var value = 0;
        var result = new List<byte>();

        foreach (var c in input.ToUpperInvariant())
        {
            var idx = alphabet.IndexOf(c, StringComparison.Ordinal);
            value = (value << 5) | idx;
            bits += 5;

            if (bits >= 8)
            {
                bits -= 8;
                result.Add((byte)((value >> (bits)) & 255));
            }
        }

        return result.ToArray();
    }
}
