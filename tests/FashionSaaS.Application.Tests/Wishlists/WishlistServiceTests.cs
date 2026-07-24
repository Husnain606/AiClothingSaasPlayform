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

    // ── GetMine (customer-facing) ────────────────────────────────────────────────────

    [Fact]
    public async Task GetMineAsync_NoWishlistYet_ReturnsEmptyWishlist()
    {
        var customer = new Customer { Id = Guid.NewGuid(), TenantId = _tenantId, Email = "c@test.com" };
        _customers.Setup(r => r.GetOrCreateByEmailAsync(_tenantId, "c@test.com", "Customer", "", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);
        _wishlists.Setup(r => r.GetByCustomerAsync(customer.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Wishlist?)null);

        ResponseData<WishlistResponse> result = await CreateService().GetMineAsync("c@test.com", "Customer", "", null);

        result.IsSuccess.Should().BeTrue();
        result.Data!.CustomerId.Should().Be(customer.Id);
        result.Data.Items.Should().BeEmpty();
    }

    // ── AddItem (customer-facing) ────────────────────────────────────────────────────

    [Fact]
    public async Task AddItemAsync_NoExistingWishlist_CreatesWishlistAndAddsItem()
    {
        var customer = new Customer { Id = Guid.NewGuid(), TenantId = _tenantId, Email = "c@test.com" };
        var product = new Product { Id = Guid.NewGuid(), TenantId = _tenantId, Name = "Tee", Slug = "tee", BasePrice = 20m };
        _products.Setup(r => r.GetByIdWithDetailsAsync(product.Id, It.IsAny<CancellationToken>())).ReturnsAsync(product);
        _customers.Setup(r => r.GetOrCreateByEmailAsync(_tenantId, "c@test.com", "Customer", "", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);
        _wishlists.Setup(r => r.GetByCustomerAsync(customer.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Wishlist?)null);

        ResponseData<WishlistItemResponse> result = await CreateService().AddItemAsync(
            "c@test.com", "Customer", "", null, new AddWishlistItemRequest { ProductId = product.Id },
            Guid.NewGuid(), "127.0.0.1", "ua");

        result.StatusCode.Should().Be(201);
        result.Data!.ProductId.Should().Be(product.Id);
        _wishlists.Verify(r => r.AddAsync(It.Is<Wishlist>(w => w.CustomerId == customer.Id)), Times.Once);
        _wishlists.Verify(r => r.AddItemAsync(It.Is<WishlistItem>(i => i.ProductId == product.Id)), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AddItemAsync_ProductAlreadyInWishlist_Returns409()
    {
        var customer = new Customer { Id = Guid.NewGuid(), TenantId = _tenantId, Email = "c@test.com" };
        var product = new Product { Id = Guid.NewGuid(), TenantId = _tenantId, Name = "Tee", Slug = "tee", BasePrice = 20m };
        _products.Setup(r => r.GetByIdWithDetailsAsync(product.Id, It.IsAny<CancellationToken>())).ReturnsAsync(product);
        _customers.Setup(r => r.GetOrCreateByEmailAsync(_tenantId, "c@test.com", "Customer", "", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);

        var existingWishlist = new Wishlist
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            CustomerId = customer.Id,
            Items = new List<WishlistItem> { new() { ProductId = product.Id, ProductVariantId = null } }
        };
        _wishlists.Setup(r => r.GetByCustomerAsync(customer.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingWishlist);

        ResponseData<WishlistItemResponse> result = await CreateService().AddItemAsync(
            "c@test.com", "Customer", "", null, new AddWishlistItemRequest { ProductId = product.Id },
            Guid.NewGuid(), "127.0.0.1", "ua");

        result.StatusCode.Should().Be(409);
        _wishlists.Verify(r => r.AddItemAsync(It.IsAny<WishlistItem>()), Times.Never);
    }

    [Fact]
    public async Task AddItemAsync_ProductNotFound_Returns404()
    {
        _products.Setup(r => r.GetByIdWithDetailsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Product?)null);

        ResponseData<WishlistItemResponse> result = await CreateService().AddItemAsync(
            "c@test.com", "Customer", "", null, new AddWishlistItemRequest { ProductId = Guid.NewGuid() },
            Guid.NewGuid(), "127.0.0.1", "ua");

        result.StatusCode.Should().Be(404);
    }

    // ── RemoveMyItem (customer-facing, ownership-checked) ───────────────────────────

    [Fact]
    public async Task RemoveMyItemAsync_ItemBelongsToCaller_Succeeds()
    {
        var customer = new Customer { Id = Guid.NewGuid(), TenantId = _tenantId, Email = "c@test.com" };
        _customers.Setup(r => r.GetOrCreateByEmailAsync(_tenantId, "c@test.com", "", "", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);

        var wishlist = new Wishlist { Id = Guid.NewGuid(), TenantId = _tenantId, CustomerId = customer.Id };
        var item = new WishlistItem { Id = Guid.NewGuid(), TenantId = _tenantId, WishlistId = wishlist.Id };
        _wishlists.Setup(r => r.GetItemAsync(item.Id, It.IsAny<CancellationToken>())).ReturnsAsync(item);
        _wishlists.Setup(r => r.GetByIdAsync(wishlist.Id)).ReturnsAsync(wishlist);

        ResponseData<bool> result = await CreateService().RemoveMyItemAsync("c@test.com", item.Id, Guid.NewGuid(), "127.0.0.1", "ua");

        result.IsSuccess.Should().BeTrue();
        _wishlists.Verify(r => r.RemoveItemAsync(item), Times.Once);
    }

    [Fact]
    public async Task RemoveMyItemAsync_ItemBelongsToSomeoneElse_Returns404_AndDoesNotRemove()
    {
        var caller = new Customer { Id = Guid.NewGuid(), TenantId = _tenantId, Email = "c@test.com" };
        _customers.Setup(r => r.GetOrCreateByEmailAsync(_tenantId, "c@test.com", "", "", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(caller);

        var someoneElsesWishlist = new Wishlist { Id = Guid.NewGuid(), TenantId = _tenantId, CustomerId = Guid.NewGuid() };
        var item = new WishlistItem { Id = Guid.NewGuid(), TenantId = _tenantId, WishlistId = someoneElsesWishlist.Id };
        _wishlists.Setup(r => r.GetItemAsync(item.Id, It.IsAny<CancellationToken>())).ReturnsAsync(item);
        _wishlists.Setup(r => r.GetByIdAsync(someoneElsesWishlist.Id)).ReturnsAsync(someoneElsesWishlist);

        ResponseData<bool> result = await CreateService().RemoveMyItemAsync("c@test.com", item.Id, Guid.NewGuid(), "127.0.0.1", "ua");

        result.StatusCode.Should().Be(404);
        _wishlists.Verify(r => r.RemoveItemAsync(It.IsAny<WishlistItem>()), Times.Never);
    }
}
