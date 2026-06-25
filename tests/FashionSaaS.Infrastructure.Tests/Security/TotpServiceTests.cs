using FashionSaaS.Infrastructure.Services;
using FluentAssertions;

namespace FashionSaaS.Infrastructure.Tests.Security;

public class TotpServiceTests
{
    private readonly TotpService _service = new();

    [Fact]
    public void GenerateSetup_ReturnsNonEmptySecretAndUrl()
    {
        var (secret, url) = _service.GenerateSetup("admin@test.com", "FashionSaaS");
        secret.Should().NotBeEmpty();
        url.Should().StartWith("otpauth://totp/");
    }

    [Fact]
    public void GenerateBackupCodes_Returns8Codes()
        => _service.GenerateBackupCodes().Should().HaveCount(8);

    [Fact]
    public void GenerateBackupCodes_AllUnique()
    {
        var codes = _service.GenerateBackupCodes();
        codes.Distinct().Should().HaveCount(8);
    }
}
