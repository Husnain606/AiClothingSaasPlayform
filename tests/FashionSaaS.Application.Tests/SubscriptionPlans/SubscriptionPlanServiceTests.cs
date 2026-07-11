using FashionSaaS.Application.Common;
using FashionSaaS.Application.Interfaces;
using FashionSaaS.Application.SubscriptionPlans;
using FashionSaaS.Application.SubscriptionPlans.DTOs;
using FashionSaaS.Domain.Entities;
using FashionSaaS.Domain.Enums;
using FluentAssertions;
using Moq;

namespace FashionSaaS.Application.Tests.SubscriptionPlans;

public class SubscriptionPlanServiceTests
{
    private readonly Mock<ISubscriptionPlanRepository> _planRepo = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IAuditLogService> _auditLog = new();

    private static readonly Guid AdminId = Guid.NewGuid();
    private const string Ip = "127.0.0.1";
    private const string Ua = "xunit";

    private SubscriptionPlanService CreateService() =>
        new(_planRepo.Object, _uow.Object, _auditLog.Object);

    // ── CreateAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_ValidRequest_Returns201WithMappedResponse()
    {
        // Arrange
        SubscriptionPlan? captured = null;
        _planRepo.Setup(r => r.AddAsync(It.IsAny<SubscriptionPlan>()))
            .Callback<SubscriptionPlan>(p => captured = p)
            .Returns(Task.CompletedTask);
        _uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);
        _auditLog.Setup(a => a.LogAsync(It.IsAny<Guid?>(), null, "SubscriptionPlanCreated",
            "SubscriptionPlan", It.IsAny<Guid>(), null, It.IsAny<object>(), Ip, Ua))
            .Returns(Task.CompletedTask);

        var request = new CreateSubscriptionPlanRequest
        {
            PlanType = SubscriptionPlanType.Monthly,
            Name = "Pro Plan",
            Price = 49.99m,
            DurationDays = 30,
            TrialDays = 7,
            ProductLimit = 100,
            UserLimit = 10,
            AiUsageLimit = 500,
            StorageLimitMb = 10240
        };

        // Act
        ResponseData<SubscriptionPlanResponse> result = await CreateService().CreateAsync(request, AdminId, Ip, Ua);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(201);
        result.Data!.Name.Should().Be("Pro Plan");
        result.Data.Price.Should().Be(49.99m);
        result.Data.IsActive.Should().BeTrue();
        captured.Should().NotBeNull();
        _uow.Verify(u => u.SaveChangesAsync(default), Times.Once);
        _auditLog.Verify(a => a.LogAsync(AdminId, null, "SubscriptionPlanCreated",
            "SubscriptionPlan", It.IsAny<Guid>(), null, It.IsAny<object>(), Ip, Ua), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_EmptyName_Returns400()
    {
        var request = new CreateSubscriptionPlanRequest { Name = "  ", Price = 10m, DurationDays = 30 };

        ResponseData<SubscriptionPlanResponse> result = await CreateService().CreateAsync(request, AdminId, Ip, Ua);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        _planRepo.Verify(r => r.AddAsync(It.IsAny<SubscriptionPlan>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_NegativePrice_Returns400()
    {
        var request = new CreateSubscriptionPlanRequest { Name = "Bad Plan", Price = -1m, DurationDays = 30 };

        ResponseData<SubscriptionPlanResponse> result = await CreateService().CreateAsync(request, AdminId, Ip, Ua);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task CreateAsync_ZeroDuration_Returns400()
    {
        var request = new CreateSubscriptionPlanRequest { Name = "Bad Plan", Price = 10m, DurationDays = 0 };

        ResponseData<SubscriptionPlanResponse> result = await CreateService().CreateAsync(request, AdminId, Ip, Ua);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
    }

    // ── UpdateAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_PlanNotFound_Returns404()
    {
        _planRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((SubscriptionPlan?)null);

        ResponseData<SubscriptionPlanResponse> result = await CreateService().UpdateAsync(Guid.NewGuid(),
            new UpdateSubscriptionPlanRequest { Name = "X", Price = 0m }, AdminId, Ip, Ua);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task UpdateAsync_ValidRequest_UpdatesAndAudits()
    {
        var plan = new SubscriptionPlan
        {
            Id = Guid.NewGuid(),
            Name = "Old",
            Price = 10m,
            DurationDays = 30,
            PlanType = SubscriptionPlanType.Monthly
        };
        _planRepo.Setup(r => r.GetByIdAsync(plan.Id)).ReturnsAsync(plan);
        _planRepo.Setup(r => r.UpdateAsync(plan)).Returns(Task.CompletedTask);
        _uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);
        _auditLog.Setup(a => a.LogAsync(It.IsAny<Guid?>(), null, "SubscriptionPlanUpdated",
            "SubscriptionPlan", plan.Id, It.IsAny<object>(), It.IsAny<object>(), Ip, Ua))
            .Returns(Task.CompletedTask);

        var request = new UpdateSubscriptionPlanRequest
        {
            Name = "New Name",
            Price = 99.99m,
            DurationDays = 365,
            IsActive = true,
            UserLimit = 50
        };

        ResponseData<SubscriptionPlanResponse> result = await CreateService().UpdateAsync(plan.Id, request, AdminId, Ip, Ua);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Name.Should().Be("New Name");
        result.Data.Price.Should().Be(99.99m);
        _auditLog.Verify(a => a.LogAsync(AdminId, null, "SubscriptionPlanUpdated",
            "SubscriptionPlan", plan.Id, It.IsAny<object>(), It.IsAny<object>(), Ip, Ua), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_EmptyName_Returns400()
    {
        var plan = new SubscriptionPlan { Id = Guid.NewGuid(), Name = "Old", Price = 10m };
        _planRepo.Setup(r => r.GetByIdAsync(plan.Id)).ReturnsAsync(plan);

        ResponseData<SubscriptionPlanResponse> result = await CreateService().UpdateAsync(plan.Id,
            new UpdateSubscriptionPlanRequest { Name = "", Price = 10m }, AdminId, Ip, Ua);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
    }

    // ── DeleteAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_PlanNotFound_Returns404()
    {
        _planRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((SubscriptionPlan?)null);

        ResponseData<bool> result = await CreateService().DeleteAsync(Guid.NewGuid(), AdminId, Ip, Ua);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task DeleteAsync_ExistingPlan_DeletesAndAudits()
    {
        var plan = new SubscriptionPlan { Id = Guid.NewGuid(), Name = "Doomed Plan", Price = 0m };
        _planRepo.Setup(r => r.GetByIdAsync(plan.Id)).ReturnsAsync(plan);
        _planRepo.Setup(r => r.DeleteAsync(plan)).Returns(Task.CompletedTask);
        _uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);
        _auditLog.Setup(a => a.LogAsync(It.IsAny<Guid?>(), null, "SubscriptionPlanDeleted",
            "SubscriptionPlan", plan.Id, It.IsAny<object>(), null, Ip, Ua))
            .Returns(Task.CompletedTask);

        ResponseData<bool> result = await CreateService().DeleteAsync(plan.Id, AdminId, Ip, Ua);

        result.IsSuccess.Should().BeTrue();
        result.Data.Should().BeTrue();
        _planRepo.Verify(r => r.DeleteAsync(plan), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(default), Times.Once);
        _auditLog.Verify(a => a.LogAsync(AdminId, null, "SubscriptionPlanDeleted",
            "SubscriptionPlan", plan.Id, It.IsAny<object>(), null, Ip, Ua), Times.Once);
    }

    // ── GetAllAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAllAsync_ReturnsAllMappedPlans()
    {
        var plans = new List<SubscriptionPlan>
        {
            new() { Id = Guid.NewGuid(), Name = "Free", Price = 0m, IsActive = true, DurationDays = 30 },
            new() { Id = Guid.NewGuid(), Name = "Pro",  Price = 49m, IsActive = false, DurationDays = 30 }
        };
        _planRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(plans);

        ResponseData<IReadOnlyList<SubscriptionPlanResponse>> result = await CreateService().GetAllAsync();

        result.IsSuccess.Should().BeTrue();
        result.Data!.Count.Should().Be(2);
        result.Data.Should().Contain(p => p.Name == "Free");
        result.Data.Should().Contain(p => p.Name == "Pro");
    }

    // ── GetActiveAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetActiveAsync_ReturnsOnlyActivePlans()
    {
        var active = new List<SubscriptionPlan>
        {
            new() { Id = Guid.NewGuid(), Name = "Active Plan", Price = 29m, IsActive = true, DurationDays = 30 }
        };
        _planRepo.Setup(r => r.GetActiveAsync()).ReturnsAsync(active);

        ResponseData<IReadOnlyList<SubscriptionPlanResponse>> result = await CreateService().GetActiveAsync();

        result.IsSuccess.Should().BeTrue();
        result.Data!.Count.Should().Be(1);
        result.Data[0].Name.Should().Be("Active Plan");
    }

    // ── GetByIdAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetByIdAsync_ExistingPlan_ReturnsMapped()
    {
        var plan = new SubscriptionPlan
        {
            Id = Guid.NewGuid(),
            Name = "Yearly",
            Price = 299m,
            PlanType = SubscriptionPlanType.Yearly,
            IsActive = true,
            DurationDays = 365
        };
        _planRepo.Setup(r => r.GetByIdAsync(plan.Id)).ReturnsAsync(plan);

        ResponseData<SubscriptionPlanResponse> result = await CreateService().GetByIdAsync(plan.Id);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Id.Should().Be(plan.Id);
        result.Data.PlanType.Should().Be(SubscriptionPlanType.Yearly);
    }

    [Fact]
    public async Task GetByIdAsync_NotFound_Returns404()
    {
        _planRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((SubscriptionPlan?)null);

        ResponseData<SubscriptionPlanResponse> result = await CreateService().GetByIdAsync(Guid.NewGuid());

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }
}
