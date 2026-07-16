using System.Security.Claims;
using FashionSaaS.TryOn.Infrastructure.Security;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Moq;

namespace FashionSaaS.TryOn.Infrastructure.Tests;

public class CurrentTryOnContextTests
{
    private static CurrentTryOnContext CreateContext(params Claim[] claims)
    {
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);
        var httpContext = new DefaultHttpContext { User = principal };
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(a => a.HttpContext).Returns(httpContext);
        return new CurrentTryOnContext(accessor.Object);
    }

    [Fact]
    public void TenantId_ReadsFromTenantIdClaim()
    {
        var tenantId = Guid.NewGuid();
        CurrentTryOnContext context = CreateContext(new Claim("tenant_id", tenantId.ToString()));
        context.TenantId.Should().Be(tenantId);
    }

    [Fact]
    public void CustomerId_ReadsFromNameIdentifierClaim()
    {
        var customerId = Guid.NewGuid();
        CurrentTryOnContext context = CreateContext(new Claim(ClaimTypes.NameIdentifier, customerId.ToString()));
        context.CustomerId.Should().Be(customerId);
    }

    [Fact]
    public void AiUsageLimit_ReadsFromAiUsageLimitClaim()
    {
        CurrentTryOnContext context = CreateContext(new Claim("ai_usage_limit", "500"));
        context.AiUsageLimit.Should().Be(500);
    }

    [Fact]
    public void AiUsageLimit_MissingClaim_DefaultsToZero()
    {
        CurrentTryOnContext context = CreateContext();
        context.AiUsageLimit.Should().Be(0);
    }

    [Fact]
    public void IsAuthenticated_NoIdentity_ReturnsFalse()
    {
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(a => a.HttpContext).Returns((HttpContext?)null);
        var context = new CurrentTryOnContext(accessor.Object);
        context.IsAuthenticated.Should().BeFalse();
    }
}
