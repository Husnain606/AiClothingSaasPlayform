using FashionSaaS.Application.Common;
using FashionSaaS.Application.Interfaces;
using FashionSaaS.Application.Subscriptions;
using FashionSaaS.Application.Subscriptions.DTOs;
using FashionSaaS.Domain.Entities;
using FashionSaaS.Domain.Enums;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FashionSaaS.Application.Tests.Subscriptions;

public class SubscriptionServiceTests
{
    private readonly Mock<ISubscriptionRepository> _subRepo = new();
    private readonly Mock<IPaymentRepository> _payRepo = new();
    private readonly Mock<ISubscriptionPlanRepository> _planRepo = new();
    private readonly Mock<ITenantRepository> _tenantRepo = new();
    private readonly Mock<IBankAccountRepository> _bankRepo = new();
    private readonly Mock<IEmailService> _email = new();
    private readonly Mock<IAuditLogService> _audit = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IFieldEncryptionService> _encrypt = new();

    private static readonly Guid AdminId = Guid.NewGuid();
    private const string Ip = "127.0.0.1";
    private const string Ua = "xunit";

    private SubscriptionService CreateService() => new(
        _subRepo.Object, _payRepo.Object, _planRepo.Object, _tenantRepo.Object,
        _bankRepo.Object, _email.Object, _audit.Object, _uow.Object, _encrypt.Object,
        NullLogger<SubscriptionService>.Instance);

    private static Tenant MakeTenant(Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        Name = "ACME Corp",
        Slug = "acme",
        Email = "admin@acme.com",
        IsActive = true
    };

    private static SubscriptionPlan MakePaidPlan(Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        Name = "Pro Monthly",
        PlanType = SubscriptionPlanType.Monthly,
        Price = 99m,
        DurationDays = 30,
        TrialDays = 0,
        IsActive = true
    };

    private static SubscriptionPlan MakeTrialPlan(Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        Name = "Free Trial",
        PlanType = SubscriptionPlanType.FreeTrial,
        Price = 0m,
        DurationDays = 0,
        TrialDays = 14,
        IsActive = true
    };

    private void SetupUow() =>
        _uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);

    private void SetupAudit() =>
        _audit.Setup(a => a.LogAsync(
            It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<Guid>(), It.IsAny<object>(), It.IsAny<object>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

    // ── AssignAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task AssignAsync_TenantNotFound_Returns404()
    {
        _tenantRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((Tenant?)null);

        ResponseData<SubscriptionResponse> result = await CreateService().AssignAsync(
            new AssignSubscriptionRequest { TenantId = Guid.NewGuid(), PlanId = Guid.NewGuid(), StartDate = DateTime.UtcNow },
            AdminId, Ip, Ua);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
        _subRepo.Verify(r => r.AddAsync(It.IsAny<TenantSubscription>()), Times.Never);
    }

    [Fact]
    public async Task AssignAsync_PlanNotFound_Returns404()
    {
        Tenant tenant = MakeTenant();
        _tenantRepo.Setup(r => r.GetByIdAsync(tenant.Id)).ReturnsAsync(tenant);
        _planRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((SubscriptionPlan?)null);

        ResponseData<SubscriptionResponse> result = await CreateService().AssignAsync(
            new AssignSubscriptionRequest { TenantId = tenant.Id, PlanId = Guid.NewGuid(), StartDate = DateTime.UtcNow },
            AdminId, Ip, Ua);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task AssignAsync_PaidPlan_CreatesSubscriptionAndPayment_Returns201()
    {
        Tenant tenant = MakeTenant();
        SubscriptionPlan plan = MakePaidPlan();
        var startDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        _tenantRepo.Setup(r => r.GetByIdAsync(tenant.Id)).ReturnsAsync(tenant);
        _planRepo.Setup(r => r.GetByIdAsync(plan.Id)).ReturnsAsync(plan);
        _subRepo.Setup(r => r.AddAsync(It.IsAny<TenantSubscription>())).Returns(Task.CompletedTask);
        _payRepo.Setup(r => r.AddAsync(It.IsAny<SubscriptionPayment>())).Returns(Task.CompletedTask);
        _bankRepo.Setup(r => r.GetPlatformAccountAsync()).ReturnsAsync((BankAccount?)null);
        _email.Setup(e => e.SendSubscriptionAssignedAsync(It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<DateTime>(), It.IsAny<string>())).Returns(Task.CompletedTask);
        SetupUow();
        SetupAudit();

        TenantSubscription? capturedSub = null;
        _subRepo.Setup(r => r.AddAsync(It.IsAny<TenantSubscription>()))
            .Callback<TenantSubscription>(s => capturedSub = s)
            .Returns(Task.CompletedTask);

        ResponseData<SubscriptionResponse> result = await CreateService().AssignAsync(
            new AssignSubscriptionRequest { TenantId = tenant.Id, PlanId = plan.Id, StartDate = startDate },
            AdminId, Ip, Ua);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(201);
        result.Data!.TenantId.Should().Be(tenant.Id);
        result.Data.PlanName.Should().Be(plan.Name);
        result.Data.Status.Should().Be(SubscriptionStatus.Active);
        result.Data.EndDate.Should().Be(startDate.AddDays(30));
        result.Data.Price.Should().Be(99m);

        capturedSub.Should().NotBeNull();
        capturedSub!.DomainEvents.Should().ContainSingle(e => e.GetType().Name == "SubscriptionAssignedEvent");

        _payRepo.Verify(r => r.AddAsync(It.IsAny<SubscriptionPayment>()), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(default), Times.Once);
        _audit.Verify(a => a.LogAsync(AdminId, tenant.Id, "SubscriptionAssigned",
            "TenantSubscription", It.IsAny<Guid>(), null, It.IsAny<object>(), Ip, Ua), Times.Once);
    }

    [Fact]
    public async Task AssignAsync_PaidPlan_EmailSendThrows_StillReturnsSuccessAndPersistsSubscription()
    {
        Tenant tenant = MakeTenant();
        SubscriptionPlan plan = MakePaidPlan();
        var startDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        _tenantRepo.Setup(r => r.GetByIdAsync(tenant.Id)).ReturnsAsync(tenant);
        _planRepo.Setup(r => r.GetByIdAsync(plan.Id)).ReturnsAsync(plan);
        _subRepo.Setup(r => r.AddAsync(It.IsAny<TenantSubscription>())).Returns(Task.CompletedTask);
        _payRepo.Setup(r => r.AddAsync(It.IsAny<SubscriptionPayment>())).Returns(Task.CompletedTask);
        _bankRepo.Setup(r => r.GetPlatformAccountAsync()).ReturnsAsync((BankAccount?)null);
        _email.Setup(e => e.SendSubscriptionAssignedAsync(It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<DateTime>(), It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("SMTP unavailable"));
        SetupUow();
        SetupAudit();

        ResponseData<SubscriptionResponse> result = await CreateService().AssignAsync(
            new AssignSubscriptionRequest { TenantId = tenant.Id, PlanId = plan.Id, StartDate = startDate },
            AdminId, Ip, Ua);

        // The subscription + pending payment must still be committed and the response must still
        // report success — a notification-email failure must never turn an already-staged write
        // (and the SaveChangesAsync call that follows it) into a 500.
        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(201);
        _subRepo.Verify(r => r.AddAsync(It.IsAny<TenantSubscription>()), Times.Once);
        _payRepo.Verify(r => r.AddAsync(It.IsAny<SubscriptionPayment>()), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task AssignAsync_FreeTrial_UsesTrialDays_NoPaymentCreated()
    {
        Tenant tenant = MakeTenant();
        SubscriptionPlan plan = MakeTrialPlan();
        var startDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        _tenantRepo.Setup(r => r.GetByIdAsync(tenant.Id)).ReturnsAsync(tenant);
        _planRepo.Setup(r => r.GetByIdAsync(plan.Id)).ReturnsAsync(plan);
        _subRepo.Setup(r => r.AddAsync(It.IsAny<TenantSubscription>())).Returns(Task.CompletedTask);
        SetupUow();
        SetupAudit();

        ResponseData<SubscriptionResponse> result = await CreateService().AssignAsync(
            new AssignSubscriptionRequest { TenantId = tenant.Id, PlanId = plan.Id, StartDate = startDate },
            AdminId, Ip, Ua);

        result.IsSuccess.Should().BeTrue();
        result.Data!.EndDate.Should().Be(startDate.AddDays(14)); // TrialDays=14
        _payRepo.Verify(r => r.AddAsync(It.IsAny<SubscriptionPayment>()), Times.Never);
        _email.Verify(e => e.SendSubscriptionAssignedAsync(It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<DateTime>(), It.IsAny<string>()), Times.Never);
    }

    // ── ConfirmPaymentAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task ConfirmPaymentAsync_PaymentNotFound_Returns404()
    {
        _payRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((SubscriptionPayment?)null);

        ResponseData<PaymentResponse> result = await CreateService().ConfirmPaymentAsync(Guid.NewGuid(), AdminId, Ip, Ua);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task ConfirmPaymentAsync_AlreadyConfirmed_Returns400()
    {
        var payment = new SubscriptionPayment
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            SubscriptionId = Guid.NewGuid(),
            Amount = 99m,
            DueDate = DateTime.UtcNow,
            Status = PaymentStatus.Confirmed,
            PaidAt = DateTime.UtcNow.AddDays(-1),
            ConfirmedByAdminId = AdminId
        };
        _payRepo.Setup(r => r.GetByIdAsync(payment.Id)).ReturnsAsync(payment);

        ResponseData<PaymentResponse> result = await CreateService().ConfirmPaymentAsync(payment.Id, AdminId, Ip, Ua);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        _payRepo.Verify(r => r.UpdateAsync(It.IsAny<SubscriptionPayment>()), Times.Never);
    }

    [Fact]
    public async Task ConfirmPaymentAsync_PendingPayment_ConfirmsAndSendsEmail()
    {
        var tenantId = Guid.NewGuid();
        Tenant tenant = MakeTenant(tenantId);
        var payment = new SubscriptionPayment
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SubscriptionId = Guid.NewGuid(),
            Amount = 99m,
            DueDate = DateTime.UtcNow.AddDays(3),
            Status = PaymentStatus.Pending
        };

        _payRepo.Setup(r => r.GetByIdAsync(payment.Id)).ReturnsAsync(payment);
        _payRepo.Setup(r => r.UpdateAsync(payment)).Returns(Task.CompletedTask);
        _tenantRepo.Setup(r => r.GetByIdAsync(tenantId)).ReturnsAsync(tenant);
        _email.Setup(e => e.SendPaymentConfirmedAsync(tenant.Email, payment.Amount)).Returns(Task.CompletedTask);
        SetupUow();
        SetupAudit();

        ResponseData<PaymentResponse> result = await CreateService().ConfirmPaymentAsync(payment.Id, AdminId, Ip, Ua);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Status.Should().Be(PaymentStatus.Confirmed);
        result.Data.PaidAt.Should().NotBeNull();

        payment.Status.Should().Be(PaymentStatus.Confirmed);
        payment.PaidAt.Should().NotBeNull();
        payment.ConfirmedByAdminId.Should().Be(AdminId);
        payment.DomainEvents.Should().ContainSingle(e => e.GetType().Name == "PaymentConfirmedEvent");

        _email.Verify(e => e.SendPaymentConfirmedAsync(tenant.Email, payment.Amount), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(default), Times.Once);
        _audit.Verify(a => a.LogAsync(AdminId, tenantId, "PaymentConfirmed",
            "SubscriptionPayment", payment.Id, It.IsAny<object>(), It.IsAny<object>(), Ip, Ua), Times.Once);
    }

    [Fact]
    public async Task ConfirmPaymentAsync_EmailSendThrows_StillReturnsSuccessAndPersistsConfirmation()
    {
        var tenantId = Guid.NewGuid();
        Tenant tenant = MakeTenant(tenantId);
        var payment = new SubscriptionPayment
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SubscriptionId = Guid.NewGuid(),
            Amount = 99m,
            DueDate = DateTime.UtcNow.AddDays(3),
            Status = PaymentStatus.Pending
        };

        _payRepo.Setup(r => r.GetByIdAsync(payment.Id)).ReturnsAsync(payment);
        _payRepo.Setup(r => r.UpdateAsync(payment)).Returns(Task.CompletedTask);
        _tenantRepo.Setup(r => r.GetByIdAsync(tenantId)).ReturnsAsync(tenant);
        _email.Setup(e => e.SendPaymentConfirmedAsync(tenant.Email, payment.Amount))
            .ThrowsAsync(new InvalidOperationException("SMTP unavailable"));
        SetupUow();
        SetupAudit();

        ResponseData<PaymentResponse> result = await CreateService().ConfirmPaymentAsync(payment.Id, AdminId, Ip, Ua);

        // The payment row already committed (SaveChangesAsync) before the email is sent — a
        // notification-email failure must never turn an already-confirmed payment into a 500.
        result.IsSuccess.Should().BeTrue();
        result.Data!.Status.Should().Be(PaymentStatus.Confirmed);
        payment.Status.Should().Be(PaymentStatus.Confirmed);
        _uow.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    // ── SuspendAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task SuspendAsync_SubscriptionNotFound_Returns404()
    {
        _subRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((TenantSubscription?)null);

        ResponseData<SubscriptionResponse> result = await CreateService().SuspendAsync(Guid.NewGuid(), AdminId, Ip, Ua);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task SuspendAsync_AlreadySuspended_Returns400()
    {
        var sub = new TenantSubscription
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            PlanId = Guid.NewGuid(),
            StartDate = DateTime.UtcNow,
            EndDate = DateTime.UtcNow.AddDays(30),
            Status = SubscriptionStatus.Suspended
        };
        _subRepo.Setup(r => r.GetByIdAsync(sub.Id)).ReturnsAsync(sub);

        ResponseData<SubscriptionResponse> result = await CreateService().SuspendAsync(sub.Id, AdminId, Ip, Ua);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        _subRepo.Verify(r => r.UpdateAsync(It.IsAny<TenantSubscription>()), Times.Never);
    }

    [Fact]
    public async Task SuspendAsync_ActiveSubscription_SuspendsAndAudits()
    {
        SubscriptionPlan plan = MakePaidPlan();
        var sub = new TenantSubscription
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            PlanId = plan.Id,
            StartDate = DateTime.UtcNow,
            EndDate = DateTime.UtcNow.AddDays(30),
            Status = SubscriptionStatus.Active,
            Plan = plan
        };
        _subRepo.Setup(r => r.GetByIdAsync(sub.Id)).ReturnsAsync(sub);
        _subRepo.Setup(r => r.UpdateAsync(sub)).Returns(Task.CompletedTask);
        _planRepo.Setup(r => r.GetByIdAsync(plan.Id)).ReturnsAsync(plan);
        SetupUow();
        SetupAudit();

        ResponseData<SubscriptionResponse> result = await CreateService().SuspendAsync(sub.Id, AdminId, Ip, Ua);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Status.Should().Be(SubscriptionStatus.Suspended);
        sub.Status.Should().Be(SubscriptionStatus.Suspended);
        _audit.Verify(a => a.LogAsync(AdminId, sub.TenantId, "SubscriptionSuspended",
            "TenantSubscription", sub.Id, It.IsAny<object>(), It.IsAny<object>(), Ip, Ua), Times.Once);
    }

    // ── ReactivateAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task ReactivateAsync_AlreadyActive_Returns400()
    {
        var sub = new TenantSubscription
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            PlanId = Guid.NewGuid(),
            StartDate = DateTime.UtcNow,
            EndDate = DateTime.UtcNow.AddDays(30),
            Status = SubscriptionStatus.Active
        };
        _subRepo.Setup(r => r.GetByIdAsync(sub.Id)).ReturnsAsync(sub);

        ResponseData<SubscriptionResponse> result = await CreateService().ReactivateAsync(sub.Id, AdminId, Ip, Ua);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task ReactivateAsync_SuspendedSubscription_ReactivatesAndAudits()
    {
        SubscriptionPlan plan = MakePaidPlan();
        var sub = new TenantSubscription
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            PlanId = plan.Id,
            StartDate = DateTime.UtcNow,
            EndDate = DateTime.UtcNow.AddDays(30),
            Status = SubscriptionStatus.Suspended,
            Plan = plan
        };
        _subRepo.Setup(r => r.GetByIdAsync(sub.Id)).ReturnsAsync(sub);
        _subRepo.Setup(r => r.UpdateAsync(sub)).Returns(Task.CompletedTask);
        _planRepo.Setup(r => r.GetByIdAsync(plan.Id)).ReturnsAsync(plan);
        SetupUow();
        SetupAudit();

        ResponseData<SubscriptionResponse> result = await CreateService().ReactivateAsync(sub.Id, AdminId, Ip, Ua);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Status.Should().Be(SubscriptionStatus.Active);
        sub.Status.Should().Be(SubscriptionStatus.Active);
        _audit.Verify(a => a.LogAsync(AdminId, sub.TenantId, "SubscriptionReactivated",
            "TenantSubscription", sub.Id, It.IsAny<object>(), It.IsAny<object>(), Ip, Ua), Times.Once);
    }

    // ── ChangePlanAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task ChangePlanAsync_SubscriptionNotFound_Returns404()
    {
        _subRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((TenantSubscription?)null);

        ResponseData<SubscriptionResponse> result = await CreateService().ChangePlanAsync(Guid.NewGuid(), Guid.NewGuid(), AdminId, Ip, Ua);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task ChangePlanAsync_NewPlanNotFound_Returns404()
    {
        var sub = new TenantSubscription
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            PlanId = Guid.NewGuid(),
            StartDate = DateTime.UtcNow,
            EndDate = DateTime.UtcNow.AddDays(30),
            Status = SubscriptionStatus.Active
        };
        _subRepo.Setup(r => r.GetByIdAsync(sub.Id)).ReturnsAsync(sub);
        _planRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((SubscriptionPlan?)null);

        ResponseData<SubscriptionResponse> result = await CreateService().ChangePlanAsync(sub.Id, Guid.NewGuid(), AdminId, Ip, Ua);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task ChangePlanAsync_ValidNewPlan_UpdatesDatesAndCreatesPayment()
    {
        var startDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        SubscriptionPlan newPlan = MakePaidPlan();
        newPlan.DurationDays = 365; // Yearly
        var sub = new TenantSubscription
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            PlanId = Guid.NewGuid(),
            StartDate = startDate,
            EndDate = startDate.AddDays(30),
            Status = SubscriptionStatus.Active
        };
        _subRepo.Setup(r => r.GetByIdAsync(sub.Id)).ReturnsAsync(sub);
        _subRepo.Setup(r => r.UpdateAsync(sub)).Returns(Task.CompletedTask);
        _planRepo.Setup(r => r.GetByIdAsync(newPlan.Id)).ReturnsAsync(newPlan);
        _payRepo.Setup(r => r.AddAsync(It.IsAny<SubscriptionPayment>())).Returns(Task.CompletedTask);
        SetupUow();
        SetupAudit();

        ResponseData<SubscriptionResponse> result = await CreateService().ChangePlanAsync(sub.Id, newPlan.Id, AdminId, Ip, Ua);

        result.IsSuccess.Should().BeTrue();
        result.Data!.EndDate.Should().Be(startDate.AddDays(365));
        result.Data.PlanName.Should().Be(newPlan.Name);
        sub.PlanId.Should().Be(newPlan.Id);
        _payRepo.Verify(r => r.AddAsync(It.IsAny<SubscriptionPayment>()), Times.Once);
        _audit.Verify(a => a.LogAsync(AdminId, sub.TenantId, "SubscriptionPlanChanged",
            "TenantSubscription", sub.Id, It.IsAny<object>(), It.IsAny<object>(), Ip, Ua), Times.Once);
    }

    // ── GetByTenantAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetByTenantAsync_NoActiveSub_Returns404()
    {
        _subRepo.Setup(r => r.GetActiveByTenantIdAsync(It.IsAny<Guid>()))
            .ReturnsAsync((TenantSubscription?)null);

        ResponseData<SubscriptionResponse> result = await CreateService().GetByTenantAsync(Guid.NewGuid());

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task GetByTenantAsync_ExistingSub_ReturnsMapped()
    {
        SubscriptionPlan plan = MakePaidPlan();
        var sub = new TenantSubscription
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            PlanId = plan.Id,
            StartDate = DateTime.UtcNow,
            EndDate = DateTime.UtcNow.AddDays(30),
            Status = SubscriptionStatus.Active,
            Plan = plan
        };
        _subRepo.Setup(r => r.GetActiveByTenantIdAsync(sub.TenantId)).ReturnsAsync(sub);

        ResponseData<SubscriptionResponse> result = await CreateService().GetByTenantAsync(sub.TenantId);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Id.Should().Be(sub.Id);
        result.Data.PlanName.Should().Be(plan.Name);
        result.Data.Status.Should().Be(SubscriptionStatus.Active);
    }

    // ── GetAllAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAllAsync_ReturnsMappedSubscriptions()
    {
        SubscriptionPlan plan = MakePaidPlan();
        var subs = new List<TenantSubscription>
        {
            new() { Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), PlanId = plan.Id,
                    StartDate = DateTime.UtcNow, EndDate = DateTime.UtcNow.AddDays(30),
                    Status = SubscriptionStatus.Active, Plan = plan },
            new() { Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), PlanId = plan.Id,
                    StartDate = DateTime.UtcNow, EndDate = DateTime.UtcNow.AddDays(10),
                    Status = SubscriptionStatus.Suspended, Plan = plan }
        };
        _subRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(subs);

        ResponseData<IReadOnlyList<SubscriptionResponse>> result = await CreateService().GetAllAsync();

        result.IsSuccess.Should().BeTrue();
        result.Data!.Count.Should().Be(2);
    }

    // ── GetPaymentsBySubscriptionAsync ───────────────────────────────────────

    [Fact]
    public async Task GetPaymentsBySubscriptionAsync_ReturnsMappedPayments()
    {
        var subId = Guid.NewGuid();
        var payments = new List<SubscriptionPayment>
        {
            new() { Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), SubscriptionId = subId,
                    Amount = 99m, DueDate = DateTime.UtcNow.AddDays(7), Status = PaymentStatus.Pending },
            new() { Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), SubscriptionId = subId,
                    Amount = 99m, DueDate = DateTime.UtcNow.AddDays(-30), Status = PaymentStatus.Confirmed,
                    PaidAt = DateTime.UtcNow.AddDays(-25) }
        };
        _payRepo.Setup(r => r.GetBySubscriptionAsync(subId)).ReturnsAsync(payments);

        ResponseData<IReadOnlyList<PaymentResponse>> result = await CreateService().GetPaymentsBySubscriptionAsync(subId);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Count.Should().Be(2);
        result.Data.Should().Contain(p => p.Status == PaymentStatus.Confirmed);
    }

    // ── GetAllPaymentsAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task GetAllPaymentsAsync_ReturnsMappedPayments()
    {
        var payments = new List<SubscriptionPayment>
        {
            new() { Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), SubscriptionId = Guid.NewGuid(),
                    Amount = 49m, DueDate = DateTime.UtcNow, Status = PaymentStatus.Pending }
        };
        _payRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(payments);

        ResponseData<IReadOnlyList<PaymentResponse>> result = await CreateService().GetAllPaymentsAsync();

        result.IsSuccess.Should().BeTrue();
        result.Data!.Count.Should().Be(1);
        result.Data[0].Amount.Should().Be(49m);
    }
}
