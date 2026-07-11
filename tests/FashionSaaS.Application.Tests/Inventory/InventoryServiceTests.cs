using FashionSaaS.Application.Common;
using FashionSaaS.Application.Interfaces;
using FashionSaaS.Application.Inventory;
using FashionSaaS.Application.Inventory.DTOs;
using FashionSaaS.Domain.Entities;
using FashionSaaS.Domain.Enums;
using FashionSaaS.Domain.Events;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FashionSaaS.Application.Tests.Inventory;

public class InventoryServiceTests
{
    private readonly Mock<IProductVariantRepository> _variants = new();
    private readonly Mock<IStockAdjustmentRepository> _adjustments = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IAuditLogService> _audit = new();
    private readonly Mock<ICurrentTenantService> _tenant = new();
    private readonly Guid _tenantId = Guid.NewGuid();

    public InventoryServiceTests()
    {
        _tenant.SetupGet(t => t.TenantId).Returns(_tenantId);
    }

    private InventoryService CreateService() => new(
        _variants.Object, _adjustments.Object, _uow.Object, _audit.Object,
        _tenant.Object, NullLogger<InventoryService>.Instance);

    private ProductVariant Variant(int stock) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = _tenantId,
        ProductId = Guid.NewGuid(),
        Size = "M",
        Color = "Red",
        Sku = "SKU-1",
        StockQuantity = stock
    };

    // ── AdjustStock ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task AdjustStockAsync_PositiveDelta_IncreasesStock_AndWritesAudit()
    {
        ProductVariant variant = Variant(50);
        _variants.Setup(r => r.GetByIdAsync(variant.Id)).ReturnsAsync(variant);
        StockAdjustment? captured = null;
        _adjustments.Setup(r => r.AddAsync(It.IsAny<StockAdjustment>()))
            .Callback<StockAdjustment>(a => captured = a).Returns(Task.CompletedTask);

        ResponseData<StockAdjustmentResponse> result = await CreateService().AdjustStockAsync(
            new AdjustStockRequest { VariantId = variant.Id, Delta = 10, Reason = StockAdjustmentReason.Restock },
            Guid.NewGuid(), "127.0.0.1", "ua");

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(200);
        variant.StockQuantity.Should().Be(60);
        captured.Should().NotBeNull();
        captured!.ResultingQuantity.Should().Be(60);
        captured.Delta.Should().Be(10);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        // Stock well above threshold → no low-stock event.
        variant.DomainEvents.Should().NotContain(e => e is LowStockEvent);
    }

    [Fact]
    public async Task AdjustStockAsync_NegativeDeltaWithinStock_DecreasesStock()
    {
        ProductVariant variant = Variant(50);
        _variants.Setup(r => r.GetByIdAsync(variant.Id)).ReturnsAsync(variant);

        ResponseData<StockAdjustmentResponse> result = await CreateService().AdjustStockAsync(
            new AdjustStockRequest { VariantId = variant.Id, Delta = -20, Reason = StockAdjustmentReason.Sale },
            Guid.NewGuid(), "127.0.0.1", "ua");

        result.IsSuccess.Should().BeTrue();
        variant.StockQuantity.Should().Be(30);
    }

    [Fact]
    public async Task AdjustStockAsync_WouldGoNegative_Returns400_NoMutation()
    {
        ProductVariant variant = Variant(5);
        _variants.Setup(r => r.GetByIdAsync(variant.Id)).ReturnsAsync(variant);

        ResponseData<StockAdjustmentResponse> result = await CreateService().AdjustStockAsync(
            new AdjustStockRequest { VariantId = variant.Id, Delta = -10, Reason = StockAdjustmentReason.Sale },
            Guid.NewGuid(), "127.0.0.1", "ua");

        result.StatusCode.Should().Be(400);
        variant.StockQuantity.Should().Be(5); // unchanged
        _adjustments.Verify(r => r.AddAsync(It.IsAny<StockAdjustment>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AdjustStockAsync_AtOrBelowThreshold_RaisesLowStockEvent()
    {
        ProductVariant variant = Variant(8);
        _variants.Setup(r => r.GetByIdAsync(variant.Id)).ReturnsAsync(variant);
        var userId = Guid.NewGuid();

        ResponseData<StockAdjustmentResponse> result = await CreateService().AdjustStockAsync(
            new AdjustStockRequest { VariantId = variant.Id, Delta = -3, Reason = StockAdjustmentReason.Sale },
            userId, "127.0.0.1", "ua");

        result.IsSuccess.Should().BeTrue();
        variant.StockQuantity.Should().Be(5); // == LowStockThreshold
        variant.DomainEvents.Should().ContainSingle(e => e is LowStockEvent)
            .Which.Should().BeOfType<LowStockEvent>()
            .Which.Remaining.Should().Be(5);
    }

    [Fact]
    public async Task AdjustStockAsync_RecordsActingUser()
    {
        ProductVariant variant = Variant(50);
        _variants.Setup(r => r.GetByIdAsync(variant.Id)).ReturnsAsync(variant);
        StockAdjustment? captured = null;
        _adjustments.Setup(r => r.AddAsync(It.IsAny<StockAdjustment>()))
            .Callback<StockAdjustment>(a => captured = a).Returns(Task.CompletedTask);
        var userId = Guid.NewGuid();

        await CreateService().AdjustStockAsync(
            new AdjustStockRequest { VariantId = variant.Id, Delta = 1, Reason = StockAdjustmentReason.Correction },
            userId, "127.0.0.1", "ua");

        captured!.AdjustedByUserId.Should().Be(userId);
    }

    [Fact]
    public async Task AdjustStockAsync_VariantFromAnotherTenant_Returns404()
    {
        var variant = new ProductVariant { Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), StockQuantity = 10 };
        _variants.Setup(r => r.GetByIdAsync(variant.Id)).ReturnsAsync(variant);

        ResponseData<StockAdjustmentResponse> result = await CreateService().AdjustStockAsync(
            new AdjustStockRequest { VariantId = variant.Id, Delta = 1, Reason = StockAdjustmentReason.Restock },
            Guid.NewGuid(), "127.0.0.1", "ua");

        result.StatusCode.Should().Be(404);
    }

    // ── GetLowStock / GetStockHistory ─────────────────────────────────────────────

    [Fact]
    public async Task GetLowStockAsync_ReturnsMappedItems()
    {
        ProductVariant v = Variant(2);
        _variants.Setup(r => r.GetLowStockAsync(_tenantId, 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductVariant> { v });

        ResponseData<IReadOnlyList<LowStockItemResponse>> result = await CreateService().GetLowStockAsync(5);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Single().VariantId.Should().Be(v.Id);
        result.Data!.Single().StockQuantity.Should().Be(2);
    }

    [Fact]
    public async Task GetStockHistoryAsync_ReturnsAdjustments()
    {
        ProductVariant v = Variant(10);
        _variants.Setup(r => r.GetByIdAsync(v.Id)).ReturnsAsync(v);
        _adjustments.Setup(r => r.GetByVariantAsync(v.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StockAdjustment>
            {
                new() { Id = Guid.NewGuid(), TenantId = _tenantId, ProductVariantId = v.Id, Delta = 5, ResultingQuantity = 10 }
            });

        ResponseData<IReadOnlyList<StockAdjustmentResponse>> result = await CreateService().GetStockHistoryAsync(v.Id);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Single().ResultingQuantity.Should().Be(10);
    }
}
