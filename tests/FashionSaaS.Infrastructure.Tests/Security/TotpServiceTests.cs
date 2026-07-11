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
}
