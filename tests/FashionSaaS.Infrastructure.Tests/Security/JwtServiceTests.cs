using FashionSaaS.Application.Configuration;
using FashionSaaS.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace FashionSaaS.Infrastructure.Tests.Security;

public class JwtServiceTests
{
    private readonly JwtService _service;

    public JwtServiceTests()
    {
        _service = new JwtService(Options.Create(new JwtSettings
        {
            Secret = "ThisIsAVeryLongSecretKeyThatIsAtLeast32CharactersLongForHS256",
            Issuer = "FashionSaaS",
            Audience = "FashionSaaS"
        }));
    }

    [Fact]
    public void GenerateRefreshToken_ReturnsNonEmptyString()
    {
        var token = _service.GenerateRefreshToken();
        token.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GenerateRefreshToken_IsBase64()
    {
        var token = _service.GenerateRefreshToken();
        var action = () => Convert.FromBase64String(token);
        action.Should().NotThrow();
    }

    // -------------------------------------------------------------------------
    // B1 — GenerateMfaChallengeToken / ValidateMfaChallengeToken
    // -------------------------------------------------------------------------

    [Fact]
    public void GenerateMfaChallengeToken_ValidateRoundtrip_ReturnsSameUserId()
    {
        var userId = Guid.NewGuid();
        var token = _service.GenerateMfaChallengeToken(userId);

        token.Should().NotBeNullOrEmpty();
        var result = _service.ValidateMfaChallengeToken(token);

        result.Should().Be(userId);
    }

    [Fact]
    public void ValidateMfaChallengeToken_InvalidToken_ReturnsNull()
    {
        var result = _service.ValidateMfaChallengeToken("not.a.valid.token");
        result.Should().BeNull();
    }

    [Fact]
    public void ValidateMfaChallengeToken_AccessTokenSubstituted_ReturnsNull()
    {
        // An access token must NOT be accepted as an MFA-challenge token
        // (it lacks purpose=mfa_challenge)
        var user = new FashionSaaS.Domain.Entities.User
        {
            Id = Guid.NewGuid(), Email = "test@test.com",
            PasswordHash = "hash", IsActive = true
        };
        var accessToken = _service.GenerateAccessToken(user, new List<string> { "AdminOwner" });

        var result = _service.ValidateMfaChallengeToken(accessToken);
        result.Should().BeNull();
    }

    // -------------------------------------------------------------------------
    // B5/D4 — GetPrincipalFromExpiredToken validates issuer + audience
    // -------------------------------------------------------------------------

    [Fact]
    public void GetPrincipalFromExpiredToken_WrongIssuer_ReturnsNull()
    {
        // Token signed with wrong issuer must be rejected
        var otherService = new JwtService(Options.Create(new JwtSettings
        {
            Secret = "ThisIsAVeryLongSecretKeyThatIsAtLeast32CharactersLongForHS256",
            Issuer = "OtherIssuer",
            Audience = "FashionSaaS"
        }));
        var user = new FashionSaaS.Domain.Entities.User
        {
            Id = Guid.NewGuid(), Email = "test@test.com",
            PasswordHash = "hash", IsActive = true
        };
        var token = otherService.GenerateAccessToken(user, new List<string>());

        var result = _service.GetPrincipalFromExpiredToken(token);
        result.Should().BeNull();
    }

    [Fact]
    public void GetPrincipalFromExpiredToken_CorrectToken_ReturnsPrincipal()
    {
        var user = new FashionSaaS.Domain.Entities.User
        {
            Id = Guid.NewGuid(), Email = "test@test.com",
            PasswordHash = "hash", IsActive = true
        };
        var token = _service.GenerateAccessToken(user, new List<string>());

        var principal = _service.GetPrincipalFromExpiredToken(token);
        principal.Should().NotBeNull();
    }
}
