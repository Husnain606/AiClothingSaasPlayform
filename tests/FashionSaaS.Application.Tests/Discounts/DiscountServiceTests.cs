using FashionSaaS.Application.Discounts;
using FashionSaaS.Application.Discounts.DTOs;
using FashionSaaS.Application.Interfaces;
using FashionSaaS.Domain.Entities;
using FashionSaaS.Domain.Enums;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FashionSaaS.Application.Tests.Discounts;

public class DiscountServiceTests
{
    private readonly Mock<IDiscountRepository> _discounts = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IAuditLogService> _audit = new();
    private readonly Mock<ICurrentTenantService> _tenant = new();
    private readonly Guid _tenantId = Guid.NewGuid();

    public DiscountServiceTests()
    {
        _tenant.SetupGet(t => t.TenantId).Returns(_tenantId);
    }

    private DiscountService CreateService() => new(
        _discounts.Object, _uow.Object, _audit.Object, _tenant.Object,
        NullLogger<DiscountService>.Instance);

    private Discount Discount(string code = "SAVE10") => new()
    {
        Id = Guid.NewGuid(), TenantId = _tenantId, Code = code, Type = DiscountType.Percentage,
        Value = 10, StartsAt = DateTime.UtcNow, EndsAt = DateTime.UtcNow.AddDays(7), IsActive = true
    };

    private static CreateDiscountRequest ValidCreate() => new()
    {
        Code = "SAVE10", Type = DiscountType.Percentage, Value = 10,
        StartsAt = DateTime.UtcNow, EndsAt = DateTime.UtcNow.AddDays(7)
    };

    // ── Create ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_NewCode_Succeeds()
    {
        _discounts.Setup(r => r.CodeExistsAsync(_tenantId, "SAVE10", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await CreateService().CreateAsync(ValidCreate(), Guid.NewGuid(), "127.0.0.1", "ua");

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(201);
        _discounts.Verify(r => r.AddAsync(It.IsAny<Discount>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_DuplicateCode_Returns409()
    {
        _discounts.Setup(r => r.CodeExistsAsync(_tenantId, "SAVE10", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await CreateService().CreateAsync(ValidCreate(), Guid.NewGuid(), "127.0.0.1", "ua");

        result.StatusCode.Should().Be(409);
        _discounts.Verify(r => r.AddAsync(It.IsAny<Discount>()), Times.Never);
    }

    // ── Update ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_DuplicateCodeExcludingSelf_Returns409()
    {
        var discount = Discount();
        _discounts.Setup(r => r.GetByIdAsync(discount.Id)).ReturnsAsync(discount);
        _discounts.Setup(r => r.CodeExistsAsync(_tenantId, "TAKEN", discount.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await CreateService().UpdateAsync(discount.Id,
            new UpdateDiscountRequest { Code = "TAKEN", Type = DiscountType.FixedAmount, Value = 5,
                StartsAt = DateTime.UtcNow, EndsAt = DateTime.UtcNow.AddDays(1) },
            Guid.NewGuid(), "127.0.0.1", "ua");

        result.StatusCode.Should().Be(409);
    }

    // ── Deactivate / Delete ─────────────────────────────────────────────────────────

    [Fact]
    public async Task DeactivateAsync_SetsInactive()
    {
        var discount = Discount();
        _discounts.Setup(r => r.GetByIdAsync(discount.Id)).ReturnsAsync(discount);

        var result = await CreateService().DeactivateAsync(discount.Id, Guid.NewGuid(), "127.0.0.1", "ua");

        result.IsSuccess.Should().BeTrue();
        discount.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_RemovesDiscount()
    {
        var discount = Discount();
        _discounts.Setup(r => r.GetByIdAsync(discount.Id)).ReturnsAsync(discount);

        var result = await CreateService().DeleteAsync(discount.Id, Guid.NewGuid(), "127.0.0.1", "ua");

        result.IsSuccess.Should().BeTrue();
        _discounts.Verify(r => r.DeleteAsync(discount), Times.Once);
    }

    // ── GetByCode / GetAll ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetByCodeAsync_Found_ReturnsDiscount()
    {
        var discount = Discount("FOUND");
        _discounts.Setup(r => r.GetByCodeAsync(_tenantId, "FOUND", It.IsAny<CancellationToken>()))
            .ReturnsAsync(discount);

        var result = await CreateService().GetByCodeAsync("FOUND");

        result.IsSuccess.Should().BeTrue();
        result.Data!.Code.Should().Be("FOUND");
    }

    [Fact]
    public async Task GetByCodeAsync_NotFound_Returns404()
    {
        _discounts.Setup(r => r.GetByCodeAsync(_tenantId, "MISSING", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Discount?)null);

        var result = await CreateService().GetByCodeAsync("MISSING");

        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsTenantDiscounts()
    {
        _discounts.Setup(r => r.GetByTenantAsync(_tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Discount> { Discount(), Discount("X") });

        var result = await CreateService().GetAllAsync();

        result.IsSuccess.Should().BeTrue();
        result.Data!.Should().HaveCount(2);
    }
}
