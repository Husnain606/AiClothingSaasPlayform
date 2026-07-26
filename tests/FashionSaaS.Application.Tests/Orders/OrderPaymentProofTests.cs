using System.Text;
using FashionSaaS.Application.Common;
using FashionSaaS.Application.Configuration;
using FashionSaaS.Application.Interfaces;
using FashionSaaS.Application.Orders;
using FashionSaaS.Application.Orders.DTOs;
using FashionSaaS.Domain.Entities;
using FashionSaaS.Domain.Enums;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace FashionSaaS.Application.Tests.Orders;

/// <summary>
/// Phase 9a: the order cannot exist without its payment proof, and cannot be confirmed without one.
/// </summary>
public class OrderPaymentProofTests
{
    private readonly Mock<IOrderRepository> _orders = new();
    private readonly Mock<IOrderPaymentProofRepository> _proofs = new();
    private readonly Mock<IPaymentProofStorageService> _storage = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Guid _tenantId = Guid.NewGuid();

    private static MemoryStream Pdf() => new(Encoding.ASCII.GetBytes("%PDF-1.7 body"));

    [Fact]
    public void HeaderMatches_GuardsTheServiceBoundary()
    {
        // The service must reject a declared type the bytes do not support.
        PaymentProofContentTypes.HeaderMatches(Encoding.ASCII.GetBytes("%PDF-1.7"), "application/pdf")
            .Should().BeTrue();
        PaymentProofContentTypes.HeaderMatches(Encoding.ASCII.GetBytes("%PDF-1.7"), "image/png")
            .Should().BeFalse();
    }

    [Fact]
    public async Task ConfirmAsync_OrderWithoutProof_Returns400_AndDoesNotChangeStatus()
    {
        var order = new Order
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            OrderNumber = "ORD-2026-000001",
            Status = OrderStatus.Pending,
            PaymentProof = null
        };
        _orders.Setup(r => r.GetByIdWithItemsAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);

        ResponseData<OrderDto> result = await CreateService().ConfirmAsync(
            order.Id, Guid.NewGuid(), "127.0.0.1", "ua");

        result.StatusCode.Should().Be(400);
        result.Message.Should().Contain("Payment proof");
        order.Status.Should().Be(OrderStatus.Pending);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ConfirmAsync_OrderWithProof_Succeeds()
    {
        var order = new Order
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            OrderNumber = "ORD-2026-000002",
            Status = OrderStatus.Pending
        };
        order.PaymentProof = new OrderPaymentProof
        {
            OrderId = order.Id,
            TenantId = _tenantId,
            StorageKey = "k",
            ContentType = "application/pdf",
            OriginalFileName = "receipt.pdf",
            SizeBytes = 10
        };
        _orders.Setup(r => r.GetByIdWithItemsAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);

        ResponseData<OrderDto> result = await CreateService().ConfirmAsync(
            order.Id, Guid.NewGuid(), "127.0.0.1", "ua");

        result.IsSuccess.Should().BeTrue();
        order.Status.Should().Be(OrderStatus.Confirmed);
    }

    [Fact]
    public async Task GetProofForCustomerAsync_OtherCustomersOrder_Returns404_NotForbidden()
    {
        var order = new Order
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            ShippingEmail = "owner@example.com"
        };
        _orders.Setup(r => r.GetByIdWithItemsAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);

        ResponseData<PaymentProofFileDto> result = await CreateService()
            .GetProofForCustomerAsync(order.Id, "someone.else@example.com");

        // 404, never 403 — a 403 would confirm the order exists.
        result.StatusCode.Should().Be(404);
        _storage.Verify(s => s.OpenReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetProofForCustomerAsync_OwnOrder_StreamsTheFile()
    {
        var order = new Order
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            ShippingEmail = "owner@example.com"
        };
        _orders.Setup(r => r.GetByIdWithItemsAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);
        _proofs.Setup(r => r.GetByOrderIdAsync(order.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrderPaymentProof
            {
                OrderId = order.Id,
                TenantId = _tenantId,
                StorageKey = "key-1",
                ContentType = "application/pdf",
                OriginalFileName = "receipt.pdf"
            });
        _storage.Setup(s => s.OpenReadAsync("key-1", It.IsAny<CancellationToken>())).ReturnsAsync(Pdf());

        ResponseData<PaymentProofFileDto> result = await CreateService()
            .GetProofForCustomerAsync(order.Id, "OWNER@example.com");

        result.IsSuccess.Should().BeTrue();
        result.Data!.ContentType.Should().Be("application/pdf");
        result.Data.FileName.Should().Be("receipt.pdf");
    }

    [Fact]
    public async Task GetProofForCustomerAsync_OrderHasNoProof_Returns404()
    {
        var order = new Order { Id = Guid.NewGuid(), TenantId = _tenantId, ShippingEmail = "owner@example.com" };
        _orders.Setup(r => r.GetByIdWithItemsAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);
        _proofs.Setup(r => r.GetByOrderIdAsync(order.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrderPaymentProof?)null);

        ResponseData<PaymentProofFileDto> result = await CreateService()
            .GetProofForCustomerAsync(order.Id, "owner@example.com");

        result.StatusCode.Should().Be(404);
    }

    private OrderService CreateService()
    {
        // Only the collaborators these tests exercise are configured; the rest are loose mocks.
        // Constructor order established by this task: orderRepository, customerRepository,
        // productRepository, variantRepository, stockAdjustmentRepository, discountRepository,
        // paymentProofRepository, proofStorage, proofStorageSettings, unitOfWork, auditLogService,
        // currentTenant, logger.
        return new OrderService(
            _orders.Object,
            Mock.Of<ICustomerRepository>(),
            Mock.Of<IProductRepository>(),
            Mock.Of<IProductVariantRepository>(),
            Mock.Of<IStockAdjustmentRepository>(),
            Mock.Of<IDiscountRepository>(),
            _proofs.Object,
            _storage.Object,
            Options.Create(new PaymentProofStorageSettings { RootPath = "." }),
            _uow.Object,
            Mock.Of<IAuditLogService>(),
            Mock.Of<ICurrentTenantService>(),
            NullLogger<OrderService>.Instance);
    }
}
