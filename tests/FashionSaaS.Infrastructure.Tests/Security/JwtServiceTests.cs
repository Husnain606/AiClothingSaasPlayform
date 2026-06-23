using System.IdentityModel.Tokens.Jwt;
using FashionSaaS.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace FashionSaaS.Infrastructure.Tests.Security;

public class JwtServiceTests
{
    private readonly JwtService _service;

    public JwtServiceTests()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtSettings:Secret"] = "ThisIsAVeryLongSecretKeyThatIsAtLeast32CharactersLongForHS256",
                ["JwtSettings:Issuer"] = "FashionSaaS",
                ["JwtSettings:Audience"] = "FashionSaaS"
            })
            .Build();
        _service = new JwtService(config);
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
