using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.Tokens;

namespace FashionSaaS.TryOn.Api.Tests;

public class ChatAuthenticationAcceptanceTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string DevSecret = "DEV-ONLY-PlaceholderSecretKeyThatIs32Chars!!";

    private static string IssueToken(Guid tenantId, Guid customerId, int aiUsageLimit)
    {
        SymmetricSecurityKey key = new(Encoding.UTF8.GetBytes(DevSecret));
        SigningCredentials credentials = new(key, SecurityAlgorithms.HmacSha256);
        Claim[] claims =
        [
            new Claim(JwtRegisteredClaimNames.Sub, customerId.ToString()),
            new Claim("tenant_id", tenantId.ToString()),
            new Claim("ai_usage_limit", aiUsageLimit.ToString(System.Globalization.CultureInfo.InvariantCulture))
        ];
        JwtSecurityToken token = new("FashionSaaS", "FashionSaaSUsers", claims,
            expires: DateTime.UtcNow.AddMinutes(5), signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    [Fact]
    public async Task PostChat_NoToken_Returns401()
    {
        HttpClient client = factory.CreateClient();
        using StringContent content = new("{}", Encoding.UTF8, "application/json");
        HttpResponseMessage response = await client.PostAsync(new Uri("/api/chat", UriKind.Relative), content);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PostChat_ValidTokenSignedWithSharedSecret_PassesAuthentication()
    {
        var token = IssueToken(Guid.NewGuid(), Guid.NewGuid(), aiUsageLimit: 10);
        HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using StringContent content = new("{}", Encoding.UTF8, "application/json");
        HttpResponseMessage response = await client.PostAsync(new Uri("/api/chat", UriKind.Relative), content);

        // An empty JSON body fails model validation (400), not authentication (401/403) —
        // this proves the JWT passed the pipeline and the request reached the controller.
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }
}
