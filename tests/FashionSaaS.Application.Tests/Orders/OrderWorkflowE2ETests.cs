using System.Text;
using FashionSaaS.Application.Common;
using FashionSaaS.Application.Interfaces;
using FashionSaaS.Application.Mapping;
using FashionSaaS.Application.Orders;
using FashionSaaS.Application.Orders.DTOs;
using FashionSaaS.Application.Reports;
using FashionSaaS.Application.Reports.DTOs;
using FashionSaaS.Domain.Entities;
using FashionSaaS.Domain.Enums;
using FashionSaaS.Infrastructure.Persistence;
using FashionSaaS.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace FashionSaaS.Application.Tests.Orders;

/// <summary>
/// End-to-end workflow tests exercising the real repositories, real UnitOfWork, and real
/// OrderService/ReportService over ONE shared in-memory ApplicationDbContext. Only
/// IAuditLogService and ICurrentTenantService are mocked, mirroring ReportServiceTests -
/// the value under test is the full create -> transition -> stock -> report pipeline, not
/// any single layer in isolation.
/// </summary>
public class OrderWorkflowE2ETests
{
    static OrderWorkflowE2ETests()
    {
        // Order.Adapt<OrderDto>() (invoked internally by OrderService) relies on the
        // OrderMappings IRegister profile being scanned into Mapster's global config —
        // normally done once at API startup. Must be forced here too so this class's
        // lowercase-status assertions pass when run in isolation, not just when some
        // other test class (e.g. OrderServiceTests) happens to run first and prime it.
        MappingConfiguration.GetMappingConfig();
    }

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _actingUserId = Guid.NewGuid();
    private const string IpAddress = "127.0.0.1";
    private const string UserAgent = "xUnit-Test-Agent";

    private sealed class Harness : IAsyncDisposable
    {
        public required ApplicationDbContext Ctx { get; init; }
        public required OrderService OrderService { get; init; }
        public required ReportService ReportService { get; init; }
        public required Guid ProductId { get; init; }
        public required Guid VariantId { get; init; }

        public ValueTask DisposeAsync() => Ctx.DisposeAsync();
    }

    private static async Task<Harness> CreateHarnessAsync(Guid tenantId, string dbName)
    {
        var currentTenant = new Mock<ICurrentTenantService>();
        currentTenant.Setup(c => c.TenantId).Returns(tenantId);

        DbContextOptions<ApplicationDbContext> options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        var ctx = new ApplicationDbContext(options, currentTenant.Object);

        // Seed product (BasePrice 40) + variant (M/Red, stock 10)
        var categoryId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var variantId = Guid.NewGuid();

        ctx.Categories.Add(new Category { Id = categoryId, TenantId = tenantId, Name = "Shirts", Slug = "shirts" });
        ctx.Products.Add(new Product
        {
            Id = productId,
            TenantId = tenantId,
            CategoryId = categoryId,
            Name = "Test Shirt",
            Slug = "test-shirt",
            BasePrice = 40m,
            Status = ProductStatus.Active
        });
        ctx.ProductVariants.Add(new ProductVariant
        {
            Id = variantId,
            TenantId = tenantId,
            ProductId = productId,
            Sku = "TS-M-RED",
            Size = "M",
            Color = "Red",
            StockQuantity = 10,
            IsActive = true
        });
        await ctx.SaveChangesAsync();

        var orderRepository = new OrderRepository(ctx);
        var customerRepository = new CustomerRepository(ctx);
        var productRepository = new ProductRepository(ctx);
        var variantRepository = new ProductVariantRepository(ctx);
        var stockAdjustmentRepository = new StockAdjustmentRepository(ctx);
        var discountRepository = new DiscountRepository(ctx);

        var publisher = new Mock<IPublisher>();
        // CA2000 suppressed: UnitOfWork.Dispose() only disposes the shared `ctx`, which the
        // Harness already disposes via DisposeAsync() — a separate Dispose() call on this
        // wrapper would be a redundant no-op, not a real resource leak.
#pragma warning disable CA2000
        var unitOfWork = new UnitOfWork(ctx, publisher.Object);
#pragma warning restore CA2000

        var auditLogService = new Mock<IAuditLogService>();
        auditLogService
            .Setup(a => a.LogAsync(
                It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(),
                It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        // Real repository, sharing this test's DbContext, so EF's navigation fixup links the
        // saved proof row to the order automatically. Storage stays mocked since no assertion
        // here cares about the saved bytes.
        var proofRepository = new OrderPaymentProofRepository(ctx);
        var proofStorage = new Mock<IPaymentProofStorageService>();

        var orderServiceLogger = new Mock<ILogger<OrderService>>();
        var orderService = new OrderService(
            orderRepository, customerRepository, productRepository, variantRepository,
            stockAdjustmentRepository, discountRepository, proofRepository, proofStorage.Object,
            unitOfWork, auditLogService.Object, currentTenant.Object,
            orderServiceLogger.Object);

        var reportRepository = new ReportRepository(ctx);
        var reportServiceLogger = new Mock<ILogger<ReportService>>();
        var reportService = new ReportService(reportRepository, currentTenant.Object, reportServiceLogger.Object);

        return new Harness
        {
            Ctx = ctx,
            OrderService = orderService,
            ReportService = reportService,
            ProductId = productId,
            VariantId = variantId
        };
    }

    private static CreateOrderRequest BuildRequest(Guid productId, int quantity, string size = "M", string color = "Red") => new()
    {
        ShippingAddress = new ShippingAddressDto
        {
            FirstName = "Jane",
            LastName = "Doe",
            Email = "jane.doe@example.com",
            Phone = "555-0100",
            Street = "123 Main St",
            City = "Springfield",
            State = "IL",
            ZipCode = "62701",
            Country = "US"
        },
        Items =
        [
            new CreateOrderItemRequest
            {
                ProductId = productId,
                Quantity = quantity,
                Variant = new OrderVariantDto { Size = size, Color = color }
            }
        ]
    };

    private static MemoryStream ValidProofStream() => new(Encoding.ASCII.GetBytes("%PDF-1.7 body"));

    private const string ValidProofFileName = "receipt.pdf";
    private const string ValidProofContentType = "application/pdf";
    private const long ValidProofSizeBytes = 13L;

    [Fact]
    public async Task FullLifecycle_CreateConfirmShipDeliver_TransitionsAndStock()
    {
        var dbName = Guid.NewGuid().ToString();
        await using Harness harness = await CreateHarnessAsync(_tenantId, dbName);

        // --- Create order for 2 ---
        ResponseData<OrderDto> createResult = await harness.OrderService.CreateAsync(
            "jane.doe@example.com", "Jane", "Doe", "555-0100",
            BuildRequest(harness.ProductId, 2), _actingUserId, IpAddress, UserAgent,
            ValidProofStream(), ValidProofFileName, ValidProofContentType, ValidProofSizeBytes);

        createResult.IsSuccess.Should().BeTrue();
        OrderDto order = createResult.Data!;
        order.Status.Should().Be("pending");
        order.Subtotal.Should().Be(80m);
        order.Tax.Should().Be(8.00m);
        order.Total.Should().Be(88.00m);
        order.OrderId.Should().Be($"ORD-{DateTime.UtcNow.Year}-000001");

        ProductVariant variantAfterCreate = await harness.Ctx.ProductVariants.AsNoTracking()
            .SingleAsync(v => v.Id == harness.VariantId);
        variantAfterCreate.StockQuantity.Should().Be(8);

        // --- Confirm ---
        ResponseData<OrderDto> confirmResult = await harness.OrderService.ConfirmAsync(order.Id, _actingUserId, IpAddress, UserAgent);
        confirmResult.IsSuccess.Should().BeTrue();
        confirmResult.Data!.Status.Should().Be("confirmed");

        // --- Ship ---
        ResponseData<OrderDto> shipResult = await harness.OrderService.ShipAsync(order.Id, "TRK1", _actingUserId, IpAddress, UserAgent);
        shipResult.IsSuccess.Should().BeTrue();
        shipResult.Data!.Status.Should().Be("shipped");
        shipResult.Data.TrackingNumber.Should().Be("TRK1");

        // --- Deliver ---
        ResponseData<OrderDto> deliverResult = await harness.OrderService.DeliverAsync(order.Id, _actingUserId, IpAddress, UserAgent);
        deliverResult.IsSuccess.Should().BeTrue();
        deliverResult.Data!.Status.Should().Be("delivered");

        // --- Second order gets the next sequence number ---
        ResponseData<OrderDto> secondCreateResult = await harness.OrderService.CreateAsync(
            "jane.doe@example.com", "Jane", "Doe", "555-0100",
            BuildRequest(harness.ProductId, 1), _actingUserId, IpAddress, UserAgent,
            ValidProofStream(), ValidProofFileName, ValidProofContentType, ValidProofSizeBytes);

        secondCreateResult.IsSuccess.Should().BeTrue();
        secondCreateResult.Data!.OrderId.Should().Be($"ORD-{DateTime.UtcNow.Year}-000002");
    }

    [Fact]
    public async Task CancelPath_RestoresStock()
    {
        var dbName = Guid.NewGuid().ToString();
        await using Harness harness = await CreateHarnessAsync(_tenantId, dbName);

        ResponseData<OrderDto> createResult = await harness.OrderService.CreateAsync(
            "jane.doe@example.com", "Jane", "Doe", "555-0100",
            BuildRequest(harness.ProductId, 3), _actingUserId, IpAddress, UserAgent,
            ValidProofStream(), ValidProofFileName, ValidProofContentType, ValidProofSizeBytes);

        createResult.IsSuccess.Should().BeTrue();
        OrderDto order = createResult.Data!;

        ProductVariant variantAfterCreate = await harness.Ctx.ProductVariants.AsNoTracking()
            .SingleAsync(v => v.Id == harness.VariantId);
        variantAfterCreate.StockQuantity.Should().Be(7); // 10 - 3

        ResponseData<OrderDto> cancelResult = await harness.OrderService.CancelAsync(
            order.Id, "Customer changed their mind", asCustomer: false, customerEmail: null,
            _actingUserId, IpAddress, UserAgent);

        cancelResult.IsSuccess.Should().BeTrue();
        cancelResult.Data!.Status.Should().Be("cancelled");

        ProductVariant variantAfterCancel = await harness.Ctx.ProductVariants.AsNoTracking()
            .SingleAsync(v => v.Id == harness.VariantId);
        variantAfterCancel.StockQuantity.Should().Be(10); // restored

        List<StockAdjustment> restorationAdjustment = await harness.Ctx.StockAdjustments.AsNoTracking()
            .Where(s => s.ProductVariantId == harness.VariantId && s.Reason == StockAdjustmentReason.OrderCancelled)
            .ToListAsync();
        restorationAdjustment.Should().ContainSingle();
        restorationAdjustment[0].Delta.Should().Be(3);
        restorationAdjustment[0].ResultingQuantity.Should().Be(10);
    }

    [Fact]
    public async Task Reports_ReflectOrders()
    {
        var dbName = Guid.NewGuid().ToString();
        await using Harness harness = await CreateHarnessAsync(_tenantId, dbName);

        // Order 1: create -> confirm -> ship -> deliver
        ResponseData<OrderDto> deliveredCreate = await harness.OrderService.CreateAsync(
            "jane.doe@example.com", "Jane", "Doe", "555-0100",
            BuildRequest(harness.ProductId, 2), _actingUserId, IpAddress, UserAgent,
            ValidProofStream(), ValidProofFileName, ValidProofContentType, ValidProofSizeBytes);
        deliveredCreate.IsSuccess.Should().BeTrue();
        OrderDto deliveredOrder = deliveredCreate.Data!;

        await harness.OrderService.ConfirmAsync(deliveredOrder.Id, _actingUserId, IpAddress, UserAgent);
        await harness.OrderService.ShipAsync(deliveredOrder.Id, "TRK-D1", _actingUserId, IpAddress, UserAgent);
        ResponseData<OrderDto> delivered = await harness.OrderService.DeliverAsync(deliveredOrder.Id, _actingUserId, IpAddress, UserAgent);
        delivered.IsSuccess.Should().BeTrue();

        // Order 2: create -> cancel
        ResponseData<OrderDto> cancelledCreate = await harness.OrderService.CreateAsync(
            "jane.doe@example.com", "Jane", "Doe", "555-0100",
            BuildRequest(harness.ProductId, 1), _actingUserId, IpAddress, UserAgent,
            ValidProofStream(), ValidProofFileName, ValidProofContentType, ValidProofSizeBytes);
        cancelledCreate.IsSuccess.Should().BeTrue();
        OrderDto cancelledOrder = cancelledCreate.Data!;

        ResponseData<OrderDto> cancelled = await harness.OrderService.CancelAsync(
            cancelledOrder.Id, "Out of stock elsewhere", asCustomer: false, customerEmail: null,
            _actingUserId, IpAddress, UserAgent);
        cancelled.IsSuccess.Should().BeTrue();

        var from = new DateTime(DateTime.UtcNow.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(DateTime.UtcNow.Year, 12, 31, 23, 59, 59, DateTimeKind.Utc);

        ResponseData<SummaryReportDto> summaryResult = await harness.ReportService.GetSummaryAsync(from, to);
        summaryResult.IsSuccess.Should().BeTrue();
        // Revenue must equal the non-cancelled (delivered) order's total only.
        summaryResult.Data!.Revenue.Should().Be(deliveredOrder.Total);
        summaryResult.Data.OrderCount.Should().Be(1);

        ResponseData<List<StatusBreakdownDto>> breakdownResult = await harness.ReportService.GetStatusBreakdownAsync(from, to);
        breakdownResult.IsSuccess.Should().BeTrue();
        List<StatusBreakdownDto> rows = breakdownResult.Data!;
        rows.Single(r => string.Equals(r.Status, "Delivered", StringComparison.Ordinal)).Count.Should().Be(1);
        rows.Single(r => string.Equals(r.Status, "Cancelled", StringComparison.Ordinal)).Count.Should().Be(1);
    }
}
