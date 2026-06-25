using FashionSaaS.Application.Interfaces;
using FashionSaaS.Application.LoginAttempts;
using FashionSaaS.Application.LoginAttempts.DTOs;
using FashionSaaS.Domain.Entities;
using FluentAssertions;
using Moq;

namespace FashionSaaS.Application.Tests.LoginAttempts;

public class LoginAttemptServiceTests
{
    private readonly Mock<ILoginAttemptRepository> _repo = new();

    private LoginAttemptService CreateService() => new(_repo.Object);

    private static UserLoginAttempt MakeAttempt(string email = "user@test.com",
        string ip = "127.0.0.1", bool success = true, string? reason = null) => new()
    {
        Id = Guid.NewGuid(),
        Email = email,
        IpAddress = ip,
        UserAgent = "xunit",
        IsSuccess = success,
        FailureReason = reason
    };

    // ── Validation ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetByEmailAsync_NullEmail_Returns400()
    {
        var filter = new LoginAttemptFilterRequest { Email = null };
        var result = await CreateService().GetByEmailAsync(filter);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.Message.Should().Be("Email is required.");
    }

    [Fact]
    public async Task GetByEmailAsync_EmptyEmail_Returns400()
    {
        var filter = new LoginAttemptFilterRequest { Email = string.Empty };
        var result = await CreateService().GetByEmailAsync(filter);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
    }

    // ── Happy path ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetByEmailAsync_ValidEmail_ReturnsPaged()
    {
        var attempts = new List<UserLoginAttempt>
        {
            MakeAttempt(success: true),
            MakeAttempt(success: false, reason: "Bad password")
        };
        _repo.Setup(r => r.GetByEmailAsync("user@test.com", 200)).ReturnsAsync(attempts);

        var filter = new LoginAttemptFilterRequest { Email = "user@test.com", Page = 1, PageSize = 50 };
        var result = await CreateService().GetByEmailAsync(filter);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(200);
        result.Data!.TotalCount.Should().Be(2);
        result.Data.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetByEmailAsync_MapsAllFields()
    {
        var attempt = MakeAttempt(email: "x@y.com", ip: "10.0.0.1", success: false, reason: "Locked");
        _repo.Setup(r => r.GetByEmailAsync("x@y.com", 200)).ReturnsAsync(new List<UserLoginAttempt> { attempt });

        var result = await CreateService().GetByEmailAsync(
            new LoginAttemptFilterRequest { Email = "x@y.com" });

        var item = result.Data!.Items.Single();
        item.Id.Should().Be(attempt.Id);
        item.Email.Should().Be("x@y.com");
        item.IpAddress.Should().Be("10.0.0.1");
        item.IsSuccess.Should().BeFalse();
        item.FailureReason.Should().Be("Locked");
        item.CreatedAt.Should().Be(attempt.CreatedAt);
    }

    // ── Filtering ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetByEmailAsync_FilterByIsSuccess_FiltersInMemory()
    {
        var attempts = new List<UserLoginAttempt>
        {
            MakeAttempt(success: true),
            MakeAttempt(success: false, reason: "Bad password"),
            MakeAttempt(success: false, reason: "Locked")
        };
        _repo.Setup(r => r.GetByEmailAsync("user@test.com", 200)).ReturnsAsync(attempts);

        var filter = new LoginAttemptFilterRequest
        {
            Email = "user@test.com", IsSuccess = false, Page = 1, PageSize = 50
        };
        var result = await CreateService().GetByEmailAsync(filter);

        result.IsSuccess.Should().BeTrue();
        result.Data!.TotalCount.Should().Be(2);
        result.Data.Items.Should().AllSatisfy(i => i.IsSuccess.Should().BeFalse());
    }

    [Fact]
    public async Task GetByEmailAsync_FilterByIpAddress_FiltersInMemory()
    {
        var attempts = new List<UserLoginAttempt>
        {
            MakeAttempt(ip: "1.2.3.4"),
            MakeAttempt(ip: "5.6.7.8"),
            MakeAttempt(ip: "1.2.3.4")
        };
        _repo.Setup(r => r.GetByEmailAsync("user@test.com", 200)).ReturnsAsync(attempts);

        var filter = new LoginAttemptFilterRequest
        {
            Email = "user@test.com", IpAddress = "1.2.3.4", Page = 1, PageSize = 50
        };
        var result = await CreateService().GetByEmailAsync(filter);

        result.IsSuccess.Should().BeTrue();
        result.Data!.TotalCount.Should().Be(2);
        result.Data.Items.Should().AllSatisfy(i => i.IpAddress.Should().Be("1.2.3.4"));
    }

    [Fact]
    public async Task GetByEmailAsync_BothFilters_CombinesCorrectly()
    {
        var attempts = new List<UserLoginAttempt>
        {
            MakeAttempt(ip: "1.2.3.4", success: true),
            MakeAttempt(ip: "1.2.3.4", success: false),
            MakeAttempt(ip: "9.9.9.9", success: false)
        };
        _repo.Setup(r => r.GetByEmailAsync("user@test.com", 200)).ReturnsAsync(attempts);

        var filter = new LoginAttemptFilterRequest
        {
            Email = "user@test.com", IpAddress = "1.2.3.4", IsSuccess = false,
            Page = 1, PageSize = 50
        };
        var result = await CreateService().GetByEmailAsync(filter);

        result.IsSuccess.Should().BeTrue();
        result.Data!.TotalCount.Should().Be(1);
        result.Data.Items.Single().IpAddress.Should().Be("1.2.3.4");
        result.Data.Items.Single().IsSuccess.Should().BeFalse();
    }

    // ── Pagination ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetByEmailAsync_Pagination_SlicesCorrectly()
    {
        var attempts = Enumerable.Range(1, 10)
            .Select(_ => MakeAttempt())
            .ToList();
        _repo.Setup(r => r.GetByEmailAsync("user@test.com", 200)).ReturnsAsync(attempts);

        var filter = new LoginAttemptFilterRequest
        {
            Email = "user@test.com", Page = 2, PageSize = 3
        };
        var result = await CreateService().GetByEmailAsync(filter);

        result.IsSuccess.Should().BeTrue();
        result.Data!.TotalCount.Should().Be(10);
        result.Data.Items.Should().HaveCount(3);
        result.Data.Page.Should().Be(2);
        result.Data.PageSize.Should().Be(3);
    }

    [Fact]
    public async Task GetByEmailAsync_EmptyList_ReturnsEmptyPaged()
    {
        _repo.Setup(r => r.GetByEmailAsync("user@test.com", 200))
            .ReturnsAsync(new List<UserLoginAttempt>());

        var result = await CreateService().GetByEmailAsync(
            new LoginAttemptFilterRequest { Email = "user@test.com" });

        result.IsSuccess.Should().BeTrue();
        result.Data!.Items.Should().BeEmpty();
        result.Data.TotalCount.Should().Be(0);
    }

    // ── Read-only: no write methods called ────────────────────────────────────

    [Fact]
    public async Task GetByEmailAsync_NeverCallsAddOrUpdateOrReset()
    {
        _repo.Setup(r => r.GetByEmailAsync("user@test.com", 200))
            .ReturnsAsync(new List<UserLoginAttempt>());

        await CreateService().GetByEmailAsync(
            new LoginAttemptFilterRequest { Email = "user@test.com" });

        _repo.Verify(r => r.AddAsync(It.IsAny<UserLoginAttempt>()), Times.Never);
        _repo.Verify(r => r.UpdateAsync(It.IsAny<UserLoginAttempt>()), Times.Never);
        _repo.Verify(r => r.ResetRecentFailedAttemptsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
