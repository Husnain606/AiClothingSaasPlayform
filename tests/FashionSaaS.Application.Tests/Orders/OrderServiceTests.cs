using FashionSaaS.Application.Common;
using FashionSaaS.Application.Interfaces;
using FashionSaaS.Application.Mapping;
using FashionSaaS.Application.Orders;
using FashionSaaS.Application.Orders.DTOs;
using FashionSaaS.Domain.Entities;
using FashionSaaS.Domain.Enums;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FashionSaaS.Application.Tests.Orders;

public class OrderServiceTests
{
    static OrderServiceTests()
    {
        // Order.Adapt<OrderDto>() relies on the OrderMappings IRegister profile being
        // scanned into Mapster's global config — normally done once at API startup.
        MappingConfiguration.GetMappingConfig();
    }

    private readonly Mock<IOrderRepository> _orders = new();
    private readonly Mock<ICustomerRepository> _customers = new();
    private readonly Mock<IProductRepository> _products = new();
    private readonly Mock<IProductVariantRepository> _variants = new();
    private readonly Mock<IStockAdjustmentRepository> _stockAdjustments = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IAuditLogService> _audit = new();
    private readonly Mock<ICurrentTenantService> _tenant = new();
    private readonly Guid _tenantId = Guid.NewGuid();

    public OrderServiceTests()
    {
        _tenant.SetupGet(t => t.TenantId).Returns(_tenantId);
    }

    private OrderService CreateService() => new(
        _orders.Object, _customers.Object, _products.Object, _variants.Object,
        _stockAdjustments.Object, _uow.Object, _audit.Object, _tenant.Object,
        NullLogger<OrderService>.Instance);

    private static ShippingAddressDto ValidAddress() => new()
    {
        FirstName = "Jane",
        LastName = "Doe",
        Email = "customer@example.com",
        Phone = "1234567890",
        Street = "1 Main St",
        City = "Doha",
        State = "DA",
        ZipCode = "00000",
        Country = "QA"
    };

    private static CreateOrderPaymentDto ValidPayment() => new()
    {
        CardholderName = "Jane Doe",
        CardNumber = "****1111"
    };

    private static CreateOrderRequest ValidRequestFor(Guid productId, int quantity, string? size = null, string? color = null)
    {
        return new CreateOrderRequest
        {
            ShippingAddress = ValidAddress(),
            PaymentInfo = ValidPayment(),
            Items =
            [
                new CreateOrderItemRequest
                {
                    ProductId = productId,
                    Quantity = quantity,
                    Variant = size is null && color is null ? null : new OrderVariantDto { Size = size, Color = color }
                }
            ]
        };
    }

    private Customer Customer() => new() { Id = Guid.NewGuid(), TenantId = _tenantId, Email = "customer@example.com", FirstName = "Jane", LastName = "Doe" };

    private void SetupCustomer(Customer customer) =>
        _customers.Setup(r => r.GetOrCreateByEmailAsync(_tenantId, customer.Email, customer.FirstName, customer.LastName, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);

    // ── CreateAsync ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_ValidRequest_Returns201_WithComputedTotals()
    {
        var product1 = new Product { Id = Guid.NewGuid(), TenantId = _tenantId, Name = "Tee", BasePrice = 20m, Status = ProductStatus.Active };
        var product2 = new Product { Id = Guid.NewGuid(), TenantId = _tenantId, Name = "Hoodie", BasePrice = 50m, Status = ProductStatus.Active };
        var variant1 = new ProductVariant { Id = Guid.NewGuid(), ProductId = product1.Id, Size = "M", Color = "Red", StockQuantity = 10 };
        var variant2 = new ProductVariant { Id = Guid.NewGuid(), ProductId = product2.Id, Size = "L", Color = "Blue", StockQuantity = 5 };

        _products.Setup(r => r.GetByIdAsync(product1.Id)).ReturnsAsync(product1);
        _products.Setup(r => r.GetByIdAsync(product2.Id)).ReturnsAsync(product2);
        _variants.Setup(r => r.GetByProductAsync(product1.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductVariant> { variant1 });
        _variants.Setup(r => r.GetByProductAsync(product2.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductVariant> { variant2 });
        _orders.Setup(r => r.CountForYearAsync(_tenantId, It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(0);
        Customer customer = Customer();
        SetupCustomer(customer);

        var request = new CreateOrderRequest
        {
            ShippingAddress = ValidAddress(),
            PaymentInfo = ValidPayment(),
            Items =
            [
                new CreateOrderItemRequest { ProductId = product1.Id, Quantity = 2, Variant = new OrderVariantDto { Size = "M", Color = "Red" } },
                new CreateOrderItemRequest { ProductId = product2.Id, Quantity = 1, Variant = new OrderVariantDto { Size = "L", Color = "Blue" } }
            ]
        };

        ResponseData<OrderDto> result = await CreateService().CreateAsync(customer.Email, customer.FirstName, customer.LastName, null, request, Guid.NewGuid(), "127.0.0.1", "ua");

        result.StatusCode.Should().Be(201);
        result.IsSuccess.Should().BeTrue();
        // Subtotal = 2*20 + 1*50 = 90; Tax = round(90*0.10, 2) = 9.00; Total = 99.00
        result.Data!.Subtotal.Should().Be(90m);
        result.Data.Tax.Should().Be(9.00m);
        result.Data.ShippingCost.Should().Be(0m);
        result.Data.Total.Should().Be(99.00m);
        result.Data.OrderId.Should().Be("ORD-2026-000001");

        _orders.Verify(r => r.AddAsync(It.IsAny<Order>()), Times.Once);
        variant1.StockQuantity.Should().Be(8);
        variant2.StockQuantity.Should().Be(4);
    }

    [Fact]
    public async Task CreateAsync_UnknownProduct_Returns400()
    {
        var productId = Guid.NewGuid();
        _products.Setup(r => r.GetByIdAsync(productId)).ReturnsAsync((Product?)null);
        _orders.Setup(r => r.CountForYearAsync(_tenantId, It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(0);
        SetupCustomer(Customer());

        CreateOrderRequest request = ValidRequestFor(productId, quantity: 1);
        ResponseData<OrderDto> result = await CreateService().CreateAsync("customer@example.com", "Jane", "Doe", null, request, Guid.NewGuid(), "127.0.0.1", "ua");

        result.StatusCode.Should().Be(400);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_InactiveProduct_Returns400()
    {
        var product = new Product { Id = Guid.NewGuid(), TenantId = _tenantId, Name = "Tee", BasePrice = 20m, Status = ProductStatus.Draft };
        _products.Setup(r => r.GetByIdAsync(product.Id)).ReturnsAsync(product);
        _orders.Setup(r => r.CountForYearAsync(_tenantId, It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(0);
        SetupCustomer(Customer());

        CreateOrderRequest request = ValidRequestFor(product.Id, quantity: 1);
        ResponseData<OrderDto> result = await CreateService().CreateAsync("customer@example.com", "Jane", "Doe", null, request, Guid.NewGuid(), "127.0.0.1", "ua");

        result.StatusCode.Should().Be(400);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_VariantRequestedButNotFound_Returns400()
    {
        var product = new Product { Id = Guid.NewGuid(), TenantId = _tenantId, Name = "Tee", BasePrice = 20m, Status = ProductStatus.Active };
        _products.Setup(r => r.GetByIdAsync(product.Id)).ReturnsAsync(product);
        _variants.Setup(r => r.GetByProductAsync(product.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductVariant>());
        _orders.Setup(r => r.CountForYearAsync(_tenantId, It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(0);
        SetupCustomer(Customer());

        CreateOrderRequest request = ValidRequestFor(product.Id, quantity: 1, size: "M", color: "Red");
        ResponseData<OrderDto> result = await CreateService().CreateAsync("customer@example.com", "Jane", "Doe", null, request, Guid.NewGuid(), "127.0.0.1", "ua");

        result.StatusCode.Should().Be(400);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_InsufficientStock_Returns400_AndDoesNotSave()
    {
        var product = new Product { Id = Guid.NewGuid(), TenantId = _tenantId, Name = "Tee", BasePrice = 20m, Status = ProductStatus.Active };
        var variant = new ProductVariant { Id = Guid.NewGuid(), ProductId = product.Id, Size = "M", Color = "Red", StockQuantity = 1 };
        _products.Setup(r => r.GetByIdAsync(product.Id)).ReturnsAsync(product);
        _variants.Setup(r => r.GetByProductAsync(product.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductVariant> { variant });
        _orders.Setup(r => r.CountForYearAsync(_tenantId, It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(0);
        SetupCustomer(Customer());

        CreateOrderRequest request = ValidRequestFor(product.Id, quantity: 5, size: "M", color: "Red");
        ResponseData<OrderDto> result = await CreateService().CreateAsync("customer@example.com", "Jane", "Doe", null, request, Guid.NewGuid(), "127.0.0.1", "ua");

        result.StatusCode.Should().Be(400);
        result.Message.Should().Contain("stock");
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_VariantPriceOverride_UsedOverBasePrice()
    {
        var product = new Product { Id = Guid.NewGuid(), TenantId = _tenantId, Name = "Tee", BasePrice = 20m, Status = ProductStatus.Active };
        var variant = new ProductVariant { Id = Guid.NewGuid(), ProductId = product.Id, Size = "M", Color = "Red", StockQuantity = 10, PriceOverride = 15m };
        _products.Setup(r => r.GetByIdAsync(product.Id)).ReturnsAsync(product);
        _variants.Setup(r => r.GetByProductAsync(product.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductVariant> { variant });
        _orders.Setup(r => r.CountForYearAsync(_tenantId, It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(0);
        SetupCustomer(Customer());

        CreateOrderRequest request = ValidRequestFor(product.Id, quantity: 2, size: "M", color: "Red");
        ResponseData<OrderDto> result = await CreateService().CreateAsync("customer@example.com", "Jane", "Doe", null, request, Guid.NewGuid(), "127.0.0.1", "ua");

        result.StatusCode.Should().Be(201);
        // 2 * 15 = 30 (override), not 2 * 20 = 40 (base)
        result.Data!.Subtotal.Should().Be(30m);
    }

    [Fact]
    public async Task CreateAsync_ClientCannotTamperPrices()
    {
        // CreateOrderItemRequest has no price field at all — totals must derive solely from repo-provided BasePrice.
        var product = new Product { Id = Guid.NewGuid(), TenantId = _tenantId, Name = "Tee", BasePrice = 20m, Status = ProductStatus.Active };
        _products.Setup(r => r.GetByIdAsync(product.Id)).ReturnsAsync(product);
        _variants.Setup(r => r.GetByProductAsync(product.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductVariant>());
        _orders.Setup(r => r.CountForYearAsync(_tenantId, It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(0);
        SetupCustomer(Customer());

        CreateOrderRequest request = ValidRequestFor(product.Id, quantity: 3);
        ResponseData<OrderDto> result = await CreateService().CreateAsync("customer@example.com", "Jane", "Doe", null, request, Guid.NewGuid(), "127.0.0.1", "ua");

        result.StatusCode.Should().Be(201);
        result.Data!.Subtotal.Should().Be(60m); // 3 * 20 BasePrice, server-computed
    }

    [Fact]
    public async Task CreateAsync_TenantUnresolved_Returns400()
    {
        _tenant.SetupGet(t => t.TenantId).Returns((Guid?)null);
        CreateOrderRequest request = ValidRequestFor(Guid.NewGuid(), quantity: 1);

        ResponseData<OrderDto> result = await CreateService().CreateAsync("customer@example.com", "Jane", "Doe", null, request, Guid.NewGuid(), "127.0.0.1", "ua");

        result.StatusCode.Should().Be(400);
    }

    // ── Confirm / Ship / Deliver ────────────────────────────────────────────────

    private Order OrderWithStatus(OrderStatus status, string shippingEmail = "customer@example.com") => new()
    {
        Id = Guid.NewGuid(),
        TenantId = _tenantId,
        OrderNumber = "ORD-2026-000001",
        Status = status,
        ShippingEmail = shippingEmail,
        Items = new List<OrderItem>()
    };

    [Fact]
    public async Task ConfirmAsync_FromPending_Succeeds()
    {
        Order order = OrderWithStatus(OrderStatus.Pending);
        _orders.Setup(r => r.GetByIdWithItemsAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);

        ResponseData<OrderDto> result = await CreateService().ConfirmAsync(order.Id, Guid.NewGuid(), "127.0.0.1", "ua");

        result.StatusCode.Should().Be(200);
        order.Status.Should().Be(OrderStatus.Confirmed);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConfirmAsync_FromShipped_Returns400()
    {
        Order order = OrderWithStatus(OrderStatus.Shipped);
        _orders.Setup(r => r.GetByIdWithItemsAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);

        ResponseData<OrderDto> result = await CreateService().ConfirmAsync(order.Id, Guid.NewGuid(), "127.0.0.1", "ua");

        result.StatusCode.Should().Be(400);
        result.Message.Should().Contain("Cannot confirm an order in status Shipped");
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ConfirmAsync_NotFound_Returns404()
    {
        var id = Guid.NewGuid();
        _orders.Setup(r => r.GetByIdWithItemsAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync((Order?)null);

        ResponseData<OrderDto> result = await CreateService().ConfirmAsync(id, Guid.NewGuid(), "127.0.0.1", "ua");

        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task ShipAsync_SetsTrackingNumber()
    {
        Order order = OrderWithStatus(OrderStatus.Confirmed);
        _orders.Setup(r => r.GetByIdWithItemsAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);

        ResponseData<OrderDto> result = await CreateService().ShipAsync(order.Id, "TRACK123", Guid.NewGuid(), "127.0.0.1", "ua");

        result.StatusCode.Should().Be(200);
        order.Status.Should().Be(OrderStatus.Shipped);
        order.TrackingNumber.Should().Be("TRACK123");
    }

    [Fact]
    public async Task ShipAsync_FromPending_Returns400()
    {
        Order order = OrderWithStatus(OrderStatus.Pending);
        _orders.Setup(r => r.GetByIdWithItemsAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);

        ResponseData<OrderDto> result = await CreateService().ShipAsync(order.Id, "TRACK123", Guid.NewGuid(), "127.0.0.1", "ua");

        result.StatusCode.Should().Be(400);
        result.Message.Should().Contain("Cannot ship an order in status Pending");
    }

    [Fact]
    public async Task DeliverAsync_FromShipped_Succeeds()
    {
        Order order = OrderWithStatus(OrderStatus.Shipped);
        _orders.Setup(r => r.GetByIdWithItemsAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);

        ResponseData<OrderDto> result = await CreateService().DeliverAsync(order.Id, Guid.NewGuid(), "127.0.0.1", "ua");

        result.StatusCode.Should().Be(200);
        order.Status.Should().Be(OrderStatus.Delivered);
    }

    [Fact]
    public async Task DeliverAsync_FromPending_Returns400()
    {
        Order order = OrderWithStatus(OrderStatus.Pending);
        _orders.Setup(r => r.GetByIdWithItemsAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);

        ResponseData<OrderDto> result = await CreateService().DeliverAsync(order.Id, Guid.NewGuid(), "127.0.0.1", "ua");

        result.StatusCode.Should().Be(400);
        result.Message.Should().Contain("Cannot deliver an order in status Pending");
    }

    // ── Cancel ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CancelAsync_FromPending_RestoresStock()
    {
        var variant = new ProductVariant { Id = Guid.NewGuid(), StockQuantity = 3 };
        Order order = OrderWithStatus(OrderStatus.Pending);
        order.Items.Add(new OrderItem { OrderId = order.Id, ProductId = Guid.NewGuid(), ProductVariantId = variant.Id, Quantity = 2, UnitPrice = 20m, ProductName = "Tee" });
        _orders.Setup(r => r.GetByIdWithItemsAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);
        _variants.Setup(r => r.GetByIdAsync(variant.Id)).ReturnsAsync(variant);

        ResponseData<OrderDto> result = await CreateService().CancelAsync(order.Id, "Customer changed mind", false, null, Guid.NewGuid(), "127.0.0.1", "ua");

        result.StatusCode.Should().Be(200);
        order.Status.Should().Be(OrderStatus.Cancelled);
        order.CancelReason.Should().Be("Customer changed mind");
        variant.StockQuantity.Should().Be(5); // 3 + 2 restored
        _stockAdjustments.Verify(r => r.AddAsync(It.IsAny<StockAdjustment>()), Times.Once);
    }

    [Fact]
    public async Task CancelAsync_FromShipped_Returns400()
    {
        Order order = OrderWithStatus(OrderStatus.Shipped);
        _orders.Setup(r => r.GetByIdWithItemsAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);

        ResponseData<OrderDto> result = await CreateService().CancelAsync(order.Id, "reason", false, null, Guid.NewGuid(), "127.0.0.1", "ua");

        result.StatusCode.Should().Be(400);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CancelAsync_AsCustomer_WrongEmail_Returns404()
    {
        Order order = OrderWithStatus(OrderStatus.Pending, shippingEmail: "owner@example.com");
        _orders.Setup(r => r.GetByIdWithItemsAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);

        ResponseData<OrderDto> result = await CreateService().CancelAsync(order.Id, "reason", true, "someone-else@example.com", Guid.NewGuid(), "127.0.0.1", "ua");

        result.StatusCode.Should().Be(404);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CancelAsync_AsCustomer_CorrectEmail_Succeeds()
    {
        Order order = OrderWithStatus(OrderStatus.Pending, shippingEmail: "owner@example.com");
        _orders.Setup(r => r.GetByIdWithItemsAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);

        ResponseData<OrderDto> result = await CreateService().CancelAsync(order.Id, "reason", true, "OWNER@example.com", Guid.NewGuid(), "127.0.0.1", "ua");

        result.StatusCode.Should().Be(200);
        order.Status.Should().Be(OrderStatus.Cancelled);
    }

    // ── GetById / GetAll / Customer-scoped ──────────────────────────────────────

    [Fact]
    public async Task GetByIdAsync_Found_ReturnsOrder()
    {
        Order order = OrderWithStatus(OrderStatus.Pending);
        _orders.Setup(r => r.GetByIdWithItemsAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);

        ResponseData<OrderDto> result = await CreateService().GetByIdAsync(order.Id);

        result.StatusCode.Should().Be(200);
        result.Data!.Id.Should().Be(order.Id);
    }

    [Fact]
    public async Task GetByIdAsync_NotFound_Returns404()
    {
        var id = Guid.NewGuid();
        _orders.Setup(r => r.GetByIdWithItemsAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync((Order?)null);

        ResponseData<OrderDto> result = await CreateService().GetByIdAsync(id);

        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task GetByIdForCustomerAsync_NotOwner_Returns404()
    {
        Order order = OrderWithStatus(OrderStatus.Pending, shippingEmail: "owner@example.com");
        _orders.Setup(r => r.GetByIdWithItemsAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);

        ResponseData<OrderDto> result = await CreateService().GetByIdForCustomerAsync(order.Id, "stranger@example.com");

        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task GetByIdForCustomerAsync_Owner_ReturnsOrder()
    {
        Order order = OrderWithStatus(OrderStatus.Pending, shippingEmail: "owner@example.com");
        _orders.Setup(r => r.GetByIdWithItemsAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);

        ResponseData<OrderDto> result = await CreateService().GetByIdForCustomerAsync(order.Id, "owner@example.com");

        result.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsPagedOrders()
    {
        var filter = new OrderFilter { Page = 1, PageSize = 20 };
        Order order = OrderWithStatus(OrderStatus.Pending);
        _orders.Setup(r => r.GetPagedAsync(It.Is<OrderFilter>(f => f.TenantId == _tenantId), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<Order> { order }.AsReadOnly() as IReadOnlyList<Order>, 1));

        ResponseData<PagedResult<OrderDto>> result = await CreateService().GetAllAsync(filter);

        result.IsSuccess.Should().BeTrue();
        result.Data!.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task GetAllAsync_OverwritesClientSuppliedForeignTenantId_PreventingCrossTenantLeak()
    {
        var foreignTenantId = Guid.NewGuid();
        var filter = new OrderFilter { TenantId = foreignTenantId, Page = 1, PageSize = 20 };
        Order order = OrderWithStatus(OrderStatus.Pending);
        _orders.Setup(r => r.GetPagedAsync(It.Is<OrderFilter>(f => f.TenantId == _tenantId), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<Order> { order }.AsReadOnly() as IReadOnlyList<Order>, 1));

        ResponseData<PagedResult<OrderDto>> result = await CreateService().GetAllAsync(filter);

        result.IsSuccess.Should().BeTrue();
        filter.TenantId.Should().Be(_tenantId);
        filter.TenantId.Should().NotBe(foreignTenantId);
    }

    [Fact]
    public async Task GetForCustomerAsync_FiltersByEmail()
    {
        Order order = OrderWithStatus(OrderStatus.Pending, shippingEmail: "customer@example.com");
        _orders.Setup(r => r.GetPagedAsync(It.IsAny<OrderFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<Order> { order }.AsReadOnly() as IReadOnlyList<Order>, 1));

        ResponseData<PagedResult<OrderDto>> result = await CreateService().GetForCustomerAsync("customer@example.com", 1, 20);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Items.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetForCustomerAsync_ForwardsEmailFilter_AndUsesRepositoryTotalCount()
    {
        // Repository is the source of truth for filtering/pagination now — the service must
        // forward CustomerEmail in the filter and trust the repo's TotalCount verbatim,
        // rather than re-filtering/re-counting in memory.
        Order order = OrderWithStatus(OrderStatus.Pending, shippingEmail: "customer@example.com");
        _orders.Setup(r => r.GetPagedAsync(
                It.Is<OrderFilter>(f => f.TenantId == _tenantId && f.CustomerEmail == "customer@example.com" && f.Page == 2 && f.PageSize == 20),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<Order> { order }.AsReadOnly() as IReadOnlyList<Order>, 25));

        ResponseData<PagedResult<OrderDto>> result = await CreateService().GetForCustomerAsync("customer@example.com", 2, 20);

        result.IsSuccess.Should().BeTrue();
        result.Data!.TotalCount.Should().Be(25);
        result.Data.Items.Should().HaveCount(1);
        _orders.Verify(r => r.GetPagedAsync(
            It.Is<OrderFilter>(f => f.CustomerEmail == "customer@example.com"), It.IsAny<CancellationToken>()), Times.Once);
    }
}
