using FashionSaaS.Application.Common;
using FashionSaaS.Application.Interfaces;
using FashionSaaS.Application.Wishlists;
using FashionSaaS.Application.Wishlists.DTOs;
using FashionSaaS.Domain.Entities;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FashionSaaS.Application.Tests.Wishlists;

public class WishlistServiceTests
{
    private readonly Mock<IWishlistRepository> _wishlists = new();
    private readonly Mock<ICustomerRepository> _customers = new();
    private readonly Mock<IProductRepository> _products = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IAuditLogService> _audit = new();
    private readonly Mock<ICurrentTenantService> _tenant = new();
    private readonly Guid _tenantId = Guid.NewGuid();

    public WishlistServiceTests()
    {
        _tenant.SetupGet(t => t.TenantId).Returns(_tenantId);
    }

    private WishlistService CreateService() => new(
        _wishlists.Object, _customers.Object, _products.Object, _uow.Object,
        _audit.Object, _tenant.Object, NullLogger<WishlistService>.Instance);

    // ── GetByCustomer ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetByCustomerAsync_ReturnsWishlistWithEnrichedItems()
    {
        var customerId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        _customers.Setup(r => r.GetByIdAsync(customerId))
            .ReturnsAsync(new Customer { Id = customerId, TenantId = _tenantId });

        var wishlist = new Wishlist
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            CustomerId = customerId,
            Items = new List<WishlistItem>
            {
                new() { Id = Guid.NewGuid(), TenantId = _tenantId, ProductId = productId }
            }
        };
        _wishlists.Setup(r => r.GetByCustomerAsync(customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(wishlist);

        var product = new Product
        {
            Id = productId,
            TenantId = _tenantId,
            Name = "Blue Tee",
            Slug = "blue-tee",
            BasePrice = 25m,
            Images = new List<ProductImage>
            {
                new() { Id = Guid.NewGuid(), TenantId = _tenantId, ProductId = productId, Url = "http://img/1", IsPrimary = true }
            }
        };
        _products.Setup(r => r.GetByIdWithDetailsAsync(productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);

        ResponseData<WishlistResponse> result = await CreateService().GetByCustomerAsync(customerId);

        result.IsSuccess.Should().BeTrue();
        result.Data!.CustomerId.Should().Be(customerId);
        result.Data.Items.Should().HaveCount(1);
        WishlistItemResponse item = result.Data.Items.Single();
        item.ProductName.Should().Be("Blue Tee");
        item.ProductBasePrice.Should().Be(25m);
        item.PrimaryImageUrl.Should().Be("http://img/1");
    }

    [Fact]
    public async Task GetByCustomerAsync_CustomerOtherTenant_Returns404()
    {
        var customerId = Guid.NewGuid();
        _customers.Setup(r => r.GetByIdAsync(customerId))
            .ReturnsAsync(new Customer { Id = customerId, TenantId = Guid.NewGuid() });

        ResponseData<WishlistResponse> result = await CreateService().GetByCustomerAsync(customerId);

        result.StatusCode.Should().Be(404);
    }

    // ── RemoveItem ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RemoveItemAsync_RemovesItem_AndWritesAudit()
    {
        var item = new WishlistItem { Id = Guid.NewGuid(), TenantId = _tenantId, ProductId = Guid.NewGuid() };
        _wishlists.Setup(r => r.GetItemAsync(item.Id, It.IsAny<CancellationToken>())).ReturnsAsync(item);

        ResponseData<bool> result = await CreateService().RemoveItemAsync(item.Id, Guid.NewGuid(), "127.0.0.1", "ua");

        result.IsSuccess.Should().BeTrue();
        _wishlists.Verify(r => r.RemoveItemAsync(item), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _audit.Verify(a => a.LogAsync(It.IsAny<Guid?>(), _tenantId, "WishlistItemRemoved", "WishlistItem",
            item.Id, It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task RemoveItemAsync_ItemOtherTenant_Returns404()
    {
        var item = new WishlistItem { Id = Guid.NewGuid(), TenantId = Guid.NewGuid() };
        _wishlists.Setup(r => r.GetItemAsync(item.Id, It.IsAny<CancellationToken>())).ReturnsAsync(item);

        ResponseData<bool> result = await CreateService().RemoveItemAsync(item.Id, Guid.NewGuid(), "127.0.0.1", "ua");

        result.StatusCode.Should().Be(404);
        _wishlists.Verify(r => r.RemoveItemAsync(It.IsAny<WishlistItem>()), Times.Never);
    }

    [Fact]
    public async Task RemoveItemAsync_NotFound_Returns404()
    {
        _wishlists.Setup(r => r.GetItemAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WishlistItem?)null);

        ResponseData<bool> result = await CreateService().RemoveItemAsync(Guid.NewGuid(), Guid.NewGuid(), "127.0.0.1", "ua");

        result.StatusCode.Should().Be(404);
    }
}
