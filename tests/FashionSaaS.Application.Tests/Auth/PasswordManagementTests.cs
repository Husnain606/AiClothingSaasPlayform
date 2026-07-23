using FashionSaaS.Application.Auth;
using FashionSaaS.Application.Auth.DTOs;
using FashionSaaS.Application.Common;
using FashionSaaS.Application.Interfaces;
using FashionSaaS.Domain.Entities;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FashionSaaS.Application.Tests.Auth;

public class PasswordManagementTests
{
    private readonly Mock<IUserRepository> _userRepo = new();
    private readonly Mock<IRefreshTokenRepository> _refreshRepo = new();
    private readonly Mock<ILoginAttemptRepository> _loginAttemptRepo = new();
    private readonly Mock<IPasswordHasher> _passwordHasher = new();
    private readonly Mock<IJwtService> _jwtService = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IAuditLogService> _auditLog = new();
    private readonly Mock<IEmailService> _emailService = new();
    private readonly Mock<IPasswordResetTokenRepository> _resetTokenRepo = new();
    private readonly Mock<IPasswordHistoryRepository> _historyRepo = new();
    private readonly Mock<IFieldEncryptionService> _fieldEncryption = new();
    private readonly Mock<ISuperAdminIpGuardService> _ipGuardService = new();

    private readonly Mock<ISubscriptionRepository> _subscriptionRepo = new();

    private AuthService CreateService() => new(
        _userRepo.Object, _refreshRepo.Object, _loginAttemptRepo.Object,
        _passwordHasher.Object, _jwtService.Object, _uow.Object,
        _auditLog.Object, _emailService.Object, _fieldEncryption.Object,
        _ipGuardService.Object, _subscriptionRepo.Object,
        NullLogger<AuthService>.Instance);

    // ------------------------------------------------------------------
    // ForgotPassword
    // ------------------------------------------------------------------

    [Fact]
    public async Task ForgotPasswordAsync_UnknownEmail_ReturnsSuccessWithAmbiguousMessage()
    {
        _userRepo.Setup(r => r.GetByEmailAsync("nobody@test.com")).ReturnsAsync((User?)null);

        AuthService service = CreateService();
        ResponseData<bool> result = await service.ForgotPasswordAsync("nobody@test.com", "https://app.test", _resetTokenRepo.Object);

        result.IsSuccess.Should().BeTrue();
        // Both branches return the same message to prevent user enumeration
        result.Message.Should().Be("If this email is registered, a reset link has been sent.");
        _emailService.Verify(e => e.SendPasswordResetAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ForgotPasswordAsync_KnownEmail_InvalidatesOldTokens_SendsEmail()
    {
        var user = new User { Id = Guid.NewGuid(), Email = "user@test.com", PasswordHash = "hash", IsActive = true };
        _userRepo.Setup(r => r.GetByEmailAsync("user@test.com")).ReturnsAsync(user);
        _resetTokenRepo.Setup(r => r.InvalidateAllByUserIdAsync(user.Id)).Returns(Task.CompletedTask);
        _resetTokenRepo.Setup(r => r.AddAsync(It.IsAny<PasswordResetToken>())).Returns(Task.CompletedTask);
        _uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);
        _emailService.Setup(e => e.SendPasswordResetAsync(user.Email, It.IsAny<string>())).Returns(Task.CompletedTask);

        AuthService service = CreateService();
        ResponseData<bool> result = await service.ForgotPasswordAsync("user@test.com", "https://app.test", _resetTokenRepo.Object);

        result.IsSuccess.Should().BeTrue();
        // Known-email path returns the SAME message as unknown-email — prevents user enumeration
        result.Message.Should().Be("If this email is registered, a reset link has been sent.");
        _resetTokenRepo.Verify(r => r.InvalidateAllByUserIdAsync(user.Id), Times.Once);
        _resetTokenRepo.Verify(r => r.AddAsync(It.IsAny<PasswordResetToken>()), Times.Once);
        _emailService.Verify(e => e.SendPasswordResetAsync(user.Email, It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task ForgotPasswordAsync_EmailSendThrows_StillReturnsSuccessAndPersistsToken()
    {
        var user = new User { Id = Guid.NewGuid(), Email = "user@test.com", PasswordHash = "hash", IsActive = true };
        _userRepo.Setup(r => r.GetByEmailAsync("user@test.com")).ReturnsAsync(user);
        _resetTokenRepo.Setup(r => r.InvalidateAllByUserIdAsync(user.Id)).Returns(Task.CompletedTask);
        _resetTokenRepo.Setup(r => r.AddAsync(It.IsAny<PasswordResetToken>())).Returns(Task.CompletedTask);
        _uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);
        _emailService.Setup(e => e.SendPasswordResetAsync(user.Email, It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("SMTP unavailable"));

        AuthService service = CreateService();
        ResponseData<bool> result = await service.ForgotPasswordAsync("user@test.com", "https://app.test", _resetTokenRepo.Object);

        // The reset token must still be persisted and the response must still report success —
        // a notification-email failure must never turn an already-committed write into a 500.
        result.IsSuccess.Should().BeTrue();
        result.Message.Should().Be("If this email is registered, a reset link has been sent.");
        _resetTokenRepo.Verify(r => r.AddAsync(It.IsAny<PasswordResetToken>()), Times.Once);
    }

    [Fact]
    public async Task ForgotPasswordAsync_StoredTokenHash_IsNotRawToken()
    {
        // The raw token must NOT be stored in DB — only the SHA-256 hash
        var user = new User { Id = Guid.NewGuid(), Email = "user@test.com", PasswordHash = "hash", IsActive = true };
        _userRepo.Setup(r => r.GetByEmailAsync("user@test.com")).ReturnsAsync(user);
        _resetTokenRepo.Setup(r => r.InvalidateAllByUserIdAsync(user.Id)).Returns(Task.CompletedTask);
        _uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);
        _emailService.Setup(e => e.SendPasswordResetAsync(user.Email, It.IsAny<string>())).Returns(Task.CompletedTask);

        PasswordResetToken? capturedToken = null;
        _resetTokenRepo.Setup(r => r.AddAsync(It.IsAny<PasswordResetToken>()))
            .Callback<PasswordResetToken>(t => capturedToken = t)
            .Returns(Task.CompletedTask);

        AuthService service = CreateService();
        await service.ForgotPasswordAsync("user@test.com", "https://app.test", _resetTokenRepo.Object);

        capturedToken.Should().NotBeNull();
        // The stored hash must be a 64-char hex string (SHA-256 produces 32 bytes = 64 hex chars)
        capturedToken!.TokenHash.Length.Should().Be(64);
        // And it must be a valid hex string (no Base64 chars like +, /, =)
        capturedToken.TokenHash.Should().MatchRegex("^[0-9A-F]+$");
    }

    // ------------------------------------------------------------------
    // ResetPassword
    // ------------------------------------------------------------------

    [Fact]
    public async Task ResetPasswordAsync_WeakPassword_ReturnsFailure()
    {
        AuthService service = CreateService();
        ResponseData<bool> result = await service.ResetPasswordAsync(
            new ResetPasswordRequest { Token = "tok", NewPassword = "weak" },
            _resetTokenRepo.Object, _historyRepo.Object);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.Message.Should().Contain("complexity");
    }

    [Fact]
    public async Task ResetPasswordAsync_InvalidToken_ReturnsFailure()
    {
        _resetTokenRepo.Setup(r => r.GetValidByHashAsync(It.IsAny<string>()))
            .ReturnsAsync((PasswordResetToken?)null);

        AuthService service = CreateService();
        ResponseData<bool> result = await service.ResetPasswordAsync(
            new ResetPasswordRequest { Token = "badtoken", NewPassword = "Password@1" },
            _resetTokenRepo.Object, _historyRepo.Object);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.Message.Should().Contain("Invalid or expired");
    }

    [Fact]
    public async Task ResetPasswordAsync_PasswordInHistory_ReturnsFailure()
    {
        var userId = Guid.NewGuid();
        var user = new User { Id = userId, Email = "user@test.com", PasswordHash = "oldhash", IsActive = true };
        var resetToken = new PasswordResetToken { UserId = userId, TokenHash = "hash", ExpiresAt = DateTime.UtcNow.AddHours(1) };

        _resetTokenRepo.Setup(r => r.GetValidByHashAsync(It.IsAny<string>())).ReturnsAsync(resetToken);
        _userRepo.Setup(r => r.GetByIdAsync(userId)).ReturnsAsync(user);

        var history = new List<PasswordHistory>
        {
            new() { UserId = userId, PasswordHash = "previoushash" }
        };
        _historyRepo.Setup(r => r.GetLastNAsync(userId, 5)).ReturnsAsync(history);
        _passwordHasher.Setup(h => h.Verify("Password@1", "previoushash")).Returns(true);

        AuthService service = CreateService();
        ResponseData<bool> result = await service.ResetPasswordAsync(
            new ResetPasswordRequest { Token = "validtoken", NewPassword = "Password@1" },
            _resetTokenRepo.Object, _historyRepo.Object);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.Message.Should().Contain("last 5 passwords");
    }

    [Fact]
    public async Task ResetPasswordAsync_Valid_MarksTokenUsed_RevokesRefreshTokens_RecordsHistory()
    {
        var userId = Guid.NewGuid();
        var user = new User { Id = userId, Email = "user@test.com", PasswordHash = "oldhash", IsActive = true };
        var resetToken = new PasswordResetToken { UserId = userId, TokenHash = "hash", ExpiresAt = DateTime.UtcNow.AddHours(1) };

        _resetTokenRepo.Setup(r => r.GetValidByHashAsync(It.IsAny<string>())).ReturnsAsync(resetToken);
        _userRepo.Setup(r => r.GetByIdAsync(userId)).ReturnsAsync(user);
        _historyRepo.Setup(r => r.GetLastNAsync(userId, 5)).ReturnsAsync(new List<PasswordHistory>());
        _passwordHasher.Setup(h => h.Hash("Password@1")).Returns("newhash");
        _userRepo.Setup(r => r.UpdateAsync(user)).Returns(Task.CompletedTask);
        _resetTokenRepo.Setup(r => r.UpdateAsync(resetToken)).Returns(Task.CompletedTask);
        _historyRepo.Setup(r => r.AddAsync(It.IsAny<PasswordHistory>())).Returns(Task.CompletedTask);
        _refreshRepo.Setup(r => r.RevokeAllByUserIdAsync(userId)).Returns(Task.CompletedTask);
        _uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);

        AuthService service = CreateService();
        ResponseData<bool> result = await service.ResetPasswordAsync(
            new ResetPasswordRequest { Token = "validtoken", NewPassword = "Password@1" },
            _resetTokenRepo.Object, _historyRepo.Object);

        result.IsSuccess.Should().BeTrue();
        resetToken.IsUsed.Should().BeTrue();
        user.PasswordHash.Should().Be("newhash");
        _refreshRepo.Verify(r => r.RevokeAllByUserIdAsync(userId), Times.Once);
        _historyRepo.Verify(r => r.AddAsync(It.Is<PasswordHistory>(h => h.UserId == userId && h.PasswordHash == "newhash")), Times.Once);
    }

    // ------------------------------------------------------------------
    // ChangePassword
    // ------------------------------------------------------------------

    [Fact]
    public async Task ChangePasswordAsync_WeakPassword_ReturnsFailure()
    {
        AuthService service = CreateService();
        ResponseData<bool> result = await service.ChangePasswordAsync(Guid.NewGuid(),
            new ChangePasswordRequest { CurrentPassword = "Current@1", NewPassword = "weak" },
            _historyRepo.Object);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task ChangePasswordAsync_WrongCurrentPassword_Returns401()
    {
        var userId = Guid.NewGuid();
        var user = new User { Id = userId, Email = "user@test.com", PasswordHash = "hash", IsActive = true };
        _userRepo.Setup(r => r.GetByIdAsync(userId)).ReturnsAsync(user);
        _passwordHasher.Setup(h => h.Verify("WrongCurrent@1", "hash")).Returns(false);

        AuthService service = CreateService();
        ResponseData<bool> result = await service.ChangePasswordAsync(userId,
            new ChangePasswordRequest { CurrentPassword = "WrongCurrent@1", NewPassword = "NewPass@1" },
            _historyRepo.Object);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task ChangePasswordAsync_PasswordInHistory_ReturnsFailure()
    {
        var userId = Guid.NewGuid();
        var user = new User { Id = userId, Email = "user@test.com", PasswordHash = "hash", IsActive = true };
        _userRepo.Setup(r => r.GetByIdAsync(userId)).ReturnsAsync(user);
        _passwordHasher.Setup(h => h.Verify("Current@1", "hash")).Returns(true);

        var history = new List<PasswordHistory> { new() { UserId = userId, PasswordHash = "oldhash" } };
        _historyRepo.Setup(r => r.GetLastNAsync(userId, 5)).ReturnsAsync(history);
        _passwordHasher.Setup(h => h.Verify("NewPass@1", "oldhash")).Returns(true);

        AuthService service = CreateService();
        ResponseData<bool> result = await service.ChangePasswordAsync(userId,
            new ChangePasswordRequest { CurrentPassword = "Current@1", NewPassword = "NewPass@1" },
            _historyRepo.Object);

        result.IsSuccess.Should().BeFalse();
        result.Message.Should().Contain("last 5 passwords");
    }

    [Fact]
    public async Task ChangePasswordAsync_Valid_UpdatesHash_RevokesTokens_RecordsHistory()
    {
        var userId = Guid.NewGuid();
        var user = new User { Id = userId, Email = "user@test.com", PasswordHash = "oldhash", IsActive = true };
        _userRepo.Setup(r => r.GetByIdAsync(userId)).ReturnsAsync(user);
        _passwordHasher.Setup(h => h.Verify("Current@1", "oldhash")).Returns(true);
        _historyRepo.Setup(r => r.GetLastNAsync(userId, 5)).ReturnsAsync(new List<PasswordHistory>());
        _passwordHasher.Setup(h => h.Hash("NewPass@1")).Returns("newhash");
        _userRepo.Setup(r => r.UpdateAsync(user)).Returns(Task.CompletedTask);
        _historyRepo.Setup(r => r.AddAsync(It.IsAny<PasswordHistory>())).Returns(Task.CompletedTask);
        _refreshRepo.Setup(r => r.RevokeAllByUserIdAsync(userId)).Returns(Task.CompletedTask);
        _uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);

        AuthService service = CreateService();
        ResponseData<bool> result = await service.ChangePasswordAsync(userId,
            new ChangePasswordRequest { CurrentPassword = "Current@1", NewPassword = "NewPass@1" },
            _historyRepo.Object);

        result.IsSuccess.Should().BeTrue();
        user.PasswordHash.Should().Be("newhash");
        _refreshRepo.Verify(r => r.RevokeAllByUserIdAsync(userId), Times.Once);
        _historyRepo.Verify(r => r.AddAsync(It.Is<PasswordHistory>(h => h.UserId == userId && h.PasswordHash == "newhash")), Times.Once);
    }
}
