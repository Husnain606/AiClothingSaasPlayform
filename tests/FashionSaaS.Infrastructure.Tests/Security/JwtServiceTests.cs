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
}
