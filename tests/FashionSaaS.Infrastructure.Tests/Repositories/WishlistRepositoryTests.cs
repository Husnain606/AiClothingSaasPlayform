using FashionSaaS.Application.Interfaces;
using FashionSaaS.Domain.Entities;
using FashionSaaS.Infrastructure.Persistence;
using FashionSaaS.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace FashionSaaS.Infrastructure.Tests.Repositories;

public class WishlistRepositoryTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _customerId = Guid.NewGuid();

    private ApplicationDbContext CreateContext()
    {
        var currentTenant = new Mock<ICurrentTenantService>();
        currentTenant.Setup(c => c.TenantId).Returns(_tenantId);

        DbContextOptions<ApplicationDbContext> options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options, currentTenant.Object);
    }

    [Fact]
    public async Task GetByCustomerAsync_CustomerWithWishlist_ReturnsWishlistWithItems()
    {
        await using ApplicationDbContext ctx = CreateContext();
        var customer = new Customer
        {
            Id = _customerId,
            TenantId = _tenantId,
            FirstName = "John",
            LastName = "Doe",
            Email = "john@example.com",
            IsActive = true
        };
        var wishlist = new Wishlist
        {
            TenantId = _tenantId,
            CustomerId = _customerId
        };
        var wishlistItem = new WishlistItem
        {
            TenantId = _tenantId,
            WishlistId = wishlist.Id,
            ProductId = Guid.NewGuid()
        };
        wishlist.Items.Add(wishlistItem);
        ctx.Customers.Add(customer);
        ctx.Wishlists.Add(wishlist);
        await ctx.SaveChangesAsync();

        var repo = new WishlistRepository(ctx);
        Wishlist? result = await repo.GetByCustomerAsync(_customerId);

        result.Should().NotBeNull();
        result!.CustomerId.Should().Be(_customerId);
        result.Items.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetByCustomerAsync_CustomerWithNoWishlist_ReturnsNull()
    {
        await using ApplicationDbContext ctx = CreateContext();

        var repo = new WishlistRepository(ctx);
        Wishlist? result = await repo.GetByCustomerAsync(_customerId);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetItemAsync_ExistingItem_ReturnsItem()
    {
        await using ApplicationDbContext ctx = CreateContext();
        var wishlist = new Wishlist
        {
            TenantId = _tenantId,
            CustomerId = _customerId
        };
        var wishlistItem = new WishlistItem
        {
            TenantId = _tenantId,
            WishlistId = wishlist.Id,
            ProductId = Guid.NewGuid()
        };
        wishlist.Items.Add(wishlistItem);
        ctx.Wishlists.Add(wishlist);
        await ctx.SaveChangesAsync();

        var repo = new WishlistRepository(ctx);
        WishlistItem? result = await repo.GetItemAsync(wishlistItem.Id);

        result.Should().NotBeNull();
        result!.Id.Should().Be(wishlistItem.Id);
    }

    [Fact]
    public async Task GetItemAsync_NonExistentItem_ReturnsNull()
    {
        await using ApplicationDbContext ctx = CreateContext();

        var repo = new WishlistRepository(ctx);
        WishlistItem? result = await repo.GetItemAsync(Guid.NewGuid());

        result.Should().BeNull();
    }

    [Fact]
    public async Task RemoveItemAsync_ExistingItem_RemovesFromContext()
    {
        await using ApplicationDbContext ctx = CreateContext();
        var wishlist = new Wishlist
        {
            TenantId = _tenantId,
            CustomerId = _customerId
        };
        var wishlistItem = new WishlistItem
        {
            TenantId = _tenantId,
            WishlistId = wishlist.Id,
            ProductId = Guid.NewGuid()
        };
        wishlist.Items.Add(wishlistItem);
        ctx.Wishlists.Add(wishlist);
        await ctx.SaveChangesAsync();

        var repo = new WishlistRepository(ctx);
        WishlistItem? item = await repo.GetItemAsync(wishlistItem.Id);
        item.Should().NotBeNull();

        await repo.RemoveItemAsync(item!);
        await ctx.SaveChangesAsync();

        WishlistItem? deleted = await ctx.WishlistItems.FindAsync(wishlistItem.Id);
        deleted.Should().BeNull();
    }
}
