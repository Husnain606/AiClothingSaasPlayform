using FashionSaaS.Application.AuditLogs;
using FashionSaaS.Application.AuditLogs.DTOs;
using FashionSaaS.Application.Interfaces;
using FashionSaaS.Domain.Entities;
using FluentAssertions;
using Moq;

namespace FashionSaaS.Application.Tests.AuditLogs;

public class AuditLogQueryServiceTests
{
    private readonly Mock<IAuditLogRepository> _repo = new();

    private AuditLogQueryService CreateService() => new(_repo.Object);

    private static AuditLog MakeLog(Guid? userId = null, string action = "Update",
        string entity = "User") => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        TenantId = null,
        Action = action,
        EntityName = entity,
        EntityId = Guid.NewGuid(),
        OldValues = "{\"name\":\"old\"}",
        NewValues = "{\"name\":\"new\"}",
        IpAddress = "127.0.0.1"
    };

    // ── GetPagedAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetPagedAsync_ReturnsMappedPagedResult()
    {
        var log1 = MakeLog(action: "Create");
        var log2 = MakeLog(action: "Update");
        var logs = new List<AuditLog> { log1, log2 };

        _repo.Setup(r => r.GetPagedAsync(null, null, null, null, null, 1, 50))
            .ReturnsAsync(logs);
        _repo.Setup(r => r.GetTotalCountAsync(null, null, null, null, null))
            .ReturnsAsync(2);

        var filter = new AuditLogFilterRequest { Page = 1, PageSize = 50 };
        var result = await CreateService().GetPagedAsync(filter);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(200);
        result.Data!.TotalCount.Should().Be(2);
        result.Data.Items.Should().HaveCount(2);
        result.Data.Page.Should().Be(1);
        result.Data.PageSize.Should().Be(50);
    }

    [Fact]
    public async Task GetPagedAsync_MapsAllFields()
    {
        var userId = Guid.NewGuid();
        var log = MakeLog(userId: userId);
        _repo.Setup(r => r.GetPagedAsync(It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<Guid?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
                It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(new List<AuditLog> { log });
        _repo.Setup(r => r.GetTotalCountAsync(It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<Guid?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>()))
            .ReturnsAsync(1);

        var result = await CreateService().GetPagedAsync(new AuditLogFilterRequest());

        var item = result.Data!.Items.Single();
        item.Id.Should().Be(log.Id);
        item.UserId.Should().Be(userId);
        item.Action.Should().Be(log.Action);
        item.EntityName.Should().Be(log.EntityName);
        item.EntityId.Should().Be(log.EntityId);
        item.OldValues.Should().Be(log.OldValues);
        item.NewValues.Should().Be(log.NewValues);
        item.IpAddress.Should().Be(log.IpAddress);
        item.CreatedAt.Should().Be(log.CreatedAt);
    }

    [Fact]
    public async Task GetPagedAsync_PassesFilterToRepository()
    {
        var userId = Guid.NewGuid();
        var from = DateTime.UtcNow.AddDays(-7);
        var to = DateTime.UtcNow;

        _repo.Setup(r => r.GetPagedAsync("Create", "User", userId, from, to, 2, 10))
            .ReturnsAsync(new List<AuditLog>());
        _repo.Setup(r => r.GetTotalCountAsync("Create", "User", userId, from, to))
            .ReturnsAsync(0);

        var filter = new AuditLogFilterRequest
        {
            Action = "Create", EntityName = "User", UserId = userId,
            From = from, To = to, Page = 2, PageSize = 10
        };

        var result = await CreateService().GetPagedAsync(filter);

        result.IsSuccess.Should().BeTrue();
        result.Data!.TotalCount.Should().Be(0);
        result.Data.Items.Should().BeEmpty();

        _repo.Verify(r => r.GetPagedAsync("Create", "User", userId, from, to, 2, 10), Times.Once);
        _repo.Verify(r => r.GetTotalCountAsync("Create", "User", userId, from, to), Times.Once);
    }

    [Fact]
    public async Task GetPagedAsync_EmptyResult_ReturnsPaged()
    {
        _repo.Setup(r => r.GetPagedAsync(It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<Guid?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
                It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(new List<AuditLog>());
        _repo.Setup(r => r.GetTotalCountAsync(It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<Guid?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>()))
            .ReturnsAsync(0);

        var result = await CreateService().GetPagedAsync(new AuditLogFilterRequest());

        result.IsSuccess.Should().BeTrue();
        result.Data!.Items.Should().BeEmpty();
        result.Data.TotalCount.Should().Be(0);
    }

    // ── GetByIdAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetByIdAsync_ExistingLog_ReturnsMapped()
    {
        var log = MakeLog();
        _repo.Setup(r => r.GetByIdAsync(log.Id)).ReturnsAsync(log);

        var result = await CreateService().GetByIdAsync(log.Id);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(200);
        result.Data!.Id.Should().Be(log.Id);
        result.Data.Action.Should().Be(log.Action);
    }

    [Fact]
    public async Task GetByIdAsync_NotFound_Returns404()
    {
        var id = Guid.NewGuid();
        _repo.Setup(r => r.GetByIdAsync(id)).ReturnsAsync((AuditLog?)null);

        var result = await CreateService().GetByIdAsync(id);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
        result.Message.Should().Be("Audit log not found.");
    }

    // ── Read-only: no write methods called ───────────────────────────────────

    [Fact]
    public async Task GetPagedAsync_NeverCallsAddOrUpdate()
    {
        _repo.Setup(r => r.GetPagedAsync(It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<Guid?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
                It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(new List<AuditLog>());
        _repo.Setup(r => r.GetTotalCountAsync(It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<Guid?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>()))
            .ReturnsAsync(0);

        await CreateService().GetPagedAsync(new AuditLogFilterRequest());

        _repo.Verify(r => r.AddAsync(It.IsAny<AuditLog>()), Times.Never);
        _repo.Verify(r => r.UpdateAsync(It.IsAny<AuditLog>()), Times.Never);
        _repo.Verify(r => r.DeleteAsync(It.IsAny<AuditLog>()), Times.Never);
    }
}
