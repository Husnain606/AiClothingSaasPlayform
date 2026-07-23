using FashionSaaS.API.Middleware;
using FashionSaaS.Application.Interfaces;
using FashionSaaS.Domain.Entities;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Moq;

namespace FashionSaaS.API.Tests.Middleware;

/// <summary>
/// Covers the slug-based branch of TenantResolutionMiddleware that the new public
/// catalog routes (api/{slug}/categories, api/{slug}/products, api/{slug}/products/{id})
/// rely on. These routes place {slug} as the literal first URL segment specifically so
/// this branch (context.GetRouteValue("slug")) is populated by endpoint routing before
/// the middleware runs.
/// </summary>
public class TenantResolutionMiddlewareTests
{
    private readonly Mock<ITenantRepository> _tenantRepository = new();
    private readonly Mock<ICurrentTenantService> _currentTenantService = new();
    private bool _nextCalled;

    private TenantResolutionMiddleware CreateMiddleware() => new(_ =>
    {
        _nextCalled = true;
        return Task.CompletedTask;
    });

    private static DefaultHttpContext ContextWithSlug(string slug)
    {
        var context = new DefaultHttpContext();
        context.Request.RouteValues["slug"] = slug;
        context.Response.Body = new MemoryStream();
        return context;
    }

    [Fact]
    public async Task InvokeAsync_UnknownSlug_Returns404AndDoesNotCallNext()
    {
        HttpContext context = ContextWithSlug("does-not-exist");
        _tenantRepository.Setup(r => r.GetBySlugAsync("does-not-exist")).ReturnsAsync((Tenant?)null);

        await CreateMiddleware().InvokeAsync(context, _tenantRepository.Object, _currentTenantService.Object);

        context.Response.StatusCode.Should().Be(404);
        _nextCalled.Should().BeFalse();
        _currentTenantService.Verify(t => t.SetTenant(It.IsAny<Guid>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task InvokeAsync_SuspendedTenantSlug_Returns403AndDoesNotCallNext()
    {
        var tenant = new Tenant { Id = Guid.NewGuid(), Slug = "chic-boutique", IsActive = false };
        HttpContext context = ContextWithSlug("chic-boutique");
        _tenantRepository.Setup(r => r.GetBySlugAsync("chic-boutique")).ReturnsAsync(tenant);

        await CreateMiddleware().InvokeAsync(context, _tenantRepository.Object, _currentTenantService.Object);

        context.Response.StatusCode.Should().Be(403);
        _nextCalled.Should().BeFalse();
        _currentTenantService.Verify(t => t.SetTenant(It.IsAny<Guid>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task InvokeAsync_ActiveTenantSlug_ResolvesTenantAndCallsNext()
    {
        var tenant = new Tenant { Id = Guid.NewGuid(), Slug = "chic-boutique", IsActive = true };
        HttpContext context = ContextWithSlug("chic-boutique");
        _tenantRepository.Setup(r => r.GetBySlugAsync("chic-boutique")).ReturnsAsync(tenant);

        await CreateMiddleware().InvokeAsync(context, _tenantRepository.Object, _currentTenantService.Object);

        _nextCalled.Should().BeTrue();
        _currentTenantService.Verify(t => t.SetTenant(tenant.Id, tenant.Slug), Times.Once);
    }
}
