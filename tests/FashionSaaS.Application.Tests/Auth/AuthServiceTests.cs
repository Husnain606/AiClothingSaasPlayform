using FashionSaaS.Application.Auth;
using FashionSaaS.Application.Auth.DTOs;
using FashionSaaS.Application.Interfaces;
using FashionSaaS.Domain.Entities;
using FashionSaaS.Domain.Enums;
using FashionSaaS.Domain.Events;
using FluentAssertions;
using Moq;

namespace FashionSaaS.Application.Tests.Auth;

public class AuthServiceTests
{
    private readonly Mock<IUserRepository> _userRepo = new();
    private readonly Mock<IRefreshTokenRepository> _refreshRepo = new();
    private readonly Mock<ILoginAttemptRepository> _loginAttemptRepo = new();
    private readonly Mock<IPasswordHasher> _passwordHasher = new();
    private readonly Mock<IJwtService> _jwtService = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IAuditLogService> _auditLog = new();
    private readonly Mock<IEmailService> _emailService = new();
    private readonly Mock<IFieldEncryptionService> _fieldEncryption = new();
    private readonly Mock<ITotpService> _totpService = new();
    private readonly Mock<ISuperAdminIpGuardService> _ipGuardService = new();

    private AuthService CreateService() => new(
        _userRepo.Object, _refreshRepo.Object, _loginAttemptRepo.Object,
        _passwordHasher.Object, _jwtService.Object, _uow.Object,
        _auditLog.Object, _emailService.Object, _fieldEncryption.Object,
        _ipGuardService.Object);

    [Fact]
    public async Task LoginAsync_ValidCredentials_NonSuperAdmin_ReturnsTokens()
    {
        var tenantId = Guid.NewGuid();
        var tenant = new Tenant { Id = tenantId, Slug = "brand-slug" };
        var user = new User
        {
            Id = Guid.NewGuid(), Email = "owner@brand.com",
            PasswordHash = "hash", IsActive = true, TenantId = tenantId,
            Tenant = tenant,
            UserRoles = new List<UserRole>
            {
                new() { Role = new Role { Name = RoleType.AdminOwner, Scope = RoleScope.Tenant } }
            }
        };

        _userRepo.Setup(r => r.GetByEmailAsync("owner@brand.com")).ReturnsAsync(user);
        _userRepo.Setup(r => r.GetByIdWithRolesAsync(user.Id)).ReturnsAsync(user);
        _passwordHasher.Setup(h => h.Verify("Password@1", "hash")).Returns(true);
        _loginAttemptRepo.Setup(r => r.GetRecentFailureCountAsync("owner@brand.com", 15)).ReturnsAsync(0);

        // Correct 4-arg signature: (user, roles, tenantSlug, mfaVerified)
        _jwtService.Setup(j => j.GenerateAccessToken(
            user,
            It.IsAny<IList<string>>(),
            It.IsAny<string?>(),
            false)).Returns("access_token");
        _jwtService.Setup(j => j.GenerateRefreshToken()).Returns("raw_refresh");
        _passwordHasher.Setup(h => h.Hash("raw_refresh")).Returns("hashed_refresh");
        _uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);

        var service = CreateService();
        var result = await service.LoginAsync(
            new LoginRequest { Email = "owner@brand.com", Password = "Password@1" },
            "127.0.0.1", "Mozilla");

        result.IsSuccess.Should().BeTrue();
        result.Data!.AccessToken.Should().Be("access_token");
        result.Data.MfaRequired.Should().BeFalse();
    }

    [Fact]
    public async Task LoginAsync_InvalidPassword_ReturnsFailure()
    {
        var user = new User
        {
            Id = Guid.NewGuid(), Email = "test@test.com",
            PasswordHash = "hash", IsActive = true
        };
        _userRepo.Setup(r => r.GetByEmailAsync("test@test.com")).ReturnsAsync(user);
        _userRepo.Setup(r => r.GetByIdWithRolesAsync(It.IsAny<Guid>())).ReturnsAsync(user);
        _passwordHasher.Setup(h => h.Verify("wrong", "hash")).Returns(false);
        _loginAttemptRepo.Setup(r => r.GetRecentFailureCountAsync("test@test.com", 15)).ReturnsAsync(0);
        _uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);

        var service = CreateService();
        var result = await service.LoginAsync(
            new LoginRequest { Email = "test@test.com", Password = "wrong" },
            "127.0.0.1", "Mozilla");

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task LoginAsync_UnknownEmail_ReturnsFailure()
    {
        _userRepo.Setup(r => r.GetByEmailAsync("nobody@test.com")).ReturnsAsync((User?)null);
        _uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);

        var service = CreateService();
        var result = await service.LoginAsync(
            new LoginRequest { Email = "nobody@test.com", Password = "pass" },
            "127.0.0.1", "Mozilla");

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task LoginAsync_SuperAdmin_ReturnsMfaRequired_NoJwtIssued()
    {
        var user = new User
        {
            Id = Guid.NewGuid(), Email = "superadmin@system.com",
            PasswordHash = "hash", IsActive = true, TenantId = null,
            UserRoles = new List<UserRole>
            {
                new() { Role = new Role { Name = RoleType.SuperAdmin, Scope = RoleScope.Platform } }
            }
        };

        _userRepo.Setup(r => r.GetByEmailAsync("superadmin@system.com")).ReturnsAsync(user);
        _userRepo.Setup(r => r.GetByIdWithRolesAsync(user.Id)).ReturnsAsync(user);
        _passwordHasher.Setup(h => h.Verify("Password@1", "hash")).Returns(true);
        _loginAttemptRepo.Setup(r => r.GetRecentFailureCountAsync("superadmin@system.com", 15)).ReturnsAsync(0);
        _uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);

        var service = CreateService();
        var result = await service.LoginAsync(
            new LoginRequest { Email = "superadmin@system.com", Password = "Password@1" },
            "127.0.0.1", "Mozilla");

        result.IsSuccess.Should().BeTrue();
        result.Data!.MfaRequired.Should().BeTrue();
        result.Data.MfaUserId.Should().Be(user.Id);
        // No JWT should be issued at this step
        result.Data.AccessToken.Should().BeNull();
        _jwtService.Verify(j => j.GenerateAccessToken(
            It.IsAny<User>(), It.IsAny<IList<string>>(), It.IsAny<string?>(), It.IsAny<bool>()),
            Times.Never);
    }

    [Fact]
    public async Task LoginAsync_AccountLocked_Returns423()
    {
        var user = new User
        {
            Id = Guid.NewGuid(), Email = "locked@test.com",
            PasswordHash = "hash", IsActive = true
        };
        _userRepo.Setup(r => r.GetByEmailAsync("locked@test.com")).ReturnsAsync(user);
        // Lockout is checked BEFORE password verification; Verify must never be called.
        _loginAttemptRepo.Setup(r => r.GetRecentFailureCountAsync("locked@test.com", 15)).ReturnsAsync(5);
        _emailService.Setup(e => e.SendAccountLockedAsync(user.Email)).Returns(Task.CompletedTask);
        _uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);

        var service = CreateService();
        var result = await service.LoginAsync(
            new LoginRequest { Email = "locked@test.com", Password = "Password@1" },
            "127.0.0.1", "Mozilla");

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(423);
        // Prove the lockout short-circuits before any password verification
        _passwordHasher.Verify(h => h.Verify(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task LogoutAsync_RevokesTokens_ReturnsSuccess()
    {
        var userId = Guid.NewGuid();
        _refreshRepo.Setup(r => r.RevokeAllByUserIdAsync(userId)).Returns(Task.CompletedTask);
        _uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);

        var service = CreateService();
        var result = await service.LogoutAsync(userId);

        result.IsSuccess.Should().BeTrue();
        result.Data.Should().BeTrue();
        _refreshRepo.Verify(r => r.RevokeAllByUserIdAsync(userId), Times.Once);
    }

    [Fact]
    public async Task LoginMfaAsync_DecryptsSecretBeforeVerify_RoundtripSucceeds()
    {
        // Arrange: mock fieldEncryption so Encrypt="enc:"+input, Decrypt strips "enc:"
        // This proves the decrypted secret (not ciphertext) reaches totpService.Verify.
        var userId = Guid.NewGuid();
        const string rawSecret = "JBSWY3DPEHPK3PXP";
        const string encryptedSecret = "enc:JBSWY3DPEHPK3PXP";

        var mfaSettings = new UserMfaSettings
        {
            TotpSecretEncrypted = encryptedSecret,
            IsEnabled = true,
            IsEnrolled = true
        };
        var user = new User
        {
            Id = userId, Email = "superadmin@system.com",
            PasswordHash = "hash", IsActive = true, TenantId = null,
            MfaSettings = mfaSettings,
            UserRoles = new List<UserRole>
            {
                new() { Role = new Role { Name = RoleType.SuperAdmin, Scope = RoleScope.Platform } }
            }
        };

        _userRepo.Setup(r => r.GetByIdWithRolesAsync(userId)).ReturnsAsync(user);
        // IP guard: known IP so no new-IP event is raised (keeps test focused)
        _ipGuardService.Setup(g => g.IsNewIpAsync("superadmin@system.com", "127.0.0.1"))
            .ReturnsAsync(false);
        // fieldEncryption.Decrypt must be called with the ciphertext and return the raw secret
        _fieldEncryption.Setup(e => e.Decrypt(encryptedSecret)).Returns(rawSecret);
        // totpService.Verify must be called with the RAW (decrypted) secret, not the ciphertext
        _totpService.Setup(t => t.Verify(rawSecret, "123456")).Returns(true);
        _jwtService.Setup(j => j.GenerateAccessToken(
            user, It.IsAny<IList<string>>(), It.IsAny<string?>(), true)).Returns("access_token");
        _jwtService.Setup(j => j.GenerateRefreshToken()).Returns("raw_refresh");
        _passwordHasher.Setup(h => h.Hash("raw_refresh")).Returns("hashed_refresh");
        _auditLog.Setup(a => a.LogAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<object?>(), It.IsAny<object?>(),
            It.IsAny<string>(), It.IsAny<string>())).Returns(Task.CompletedTask);
        _uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);

        var totpServiceMock = new Mock<ITotpService>();
        totpServiceMock.Setup(t => t.Verify(rawSecret, "123456")).Returns(true);

        var service = CreateService();
        var result = await service.LoginMfaAsync(
            new LoginMfaRequest { UserId = userId, Code = "123456" },
            totpServiceMock.Object, "127.0.0.1", "Mozilla");

        // Assert: login succeeds proving the decrypted secret reached totpService.Verify
        result.IsSuccess.Should().BeTrue();
        result.Data!.AccessToken.Should().NotBeNull();
        // Prove decrypt was called with the ciphertext
        _fieldEncryption.Verify(e => e.Decrypt(encryptedSecret), Times.Once);
        // Prove totpService.Verify was called with the DECRYPTED secret (not ciphertext)
        totpServiceMock.Verify(t => t.Verify(rawSecret, "123456"), Times.Once);
    }

    [Fact]
    public async Task IsPasswordCompliant_WeakPassword_ReturnsFalse()
    {
        AuthService.IsPasswordCompliant("weak").Should().BeFalse();
    }

    [Fact]
    public async Task IsPasswordCompliant_StrongPassword_AtSymbol_ReturnsTrue()
    {
        AuthService.IsPasswordCompliant("Password@1").Should().BeTrue();
    }

    [Fact]
    public async Task IsPasswordCompliant_StrongPassword_HyphenSpecial_ReturnsTrue()
    {
        // Hyphen was rejected by the old narrow regex — must be accepted now
        AuthService.IsPasswordCompliant("MyPass1-").Should().BeTrue();
    }

    [Fact]
    public async Task IsPasswordCompliant_StrongPassword_UnderscoreSpecial_ReturnsTrue()
    {
        AuthService.IsPasswordCompliant("MyPass1_").Should().BeTrue();
    }

    // -------------------------------------------------------------------------
    // Fix 1 — new-IP event gated on SuperAdmin role
    // -------------------------------------------------------------------------

    [Fact]
    public async Task LoginMfaAsync_SuperAdmin_NewIp_RaisesSuperAdminLoginFromNewIpEvent()
    {
        // Arrange: SuperAdmin user with valid TOTP + a new (unknown) IP
        var userId = Guid.NewGuid();
        const string rawSecret = "JBSWY3DPEHPK3PXP";
        const string encryptedSecret = "enc:JBSWY3DPEHPK3PXP";

        var mfaSettings = new UserMfaSettings
        {
            TotpSecretEncrypted = encryptedSecret,
            IsEnabled = true,
            IsEnrolled = true
        };
        var user = new User
        {
            Id = userId, Email = "superadmin@system.com",
            PasswordHash = "hash", IsActive = true, TenantId = null,
            MfaSettings = mfaSettings,
            UserRoles = new List<UserRole>
            {
                new() { Role = new Role { Name = RoleType.SuperAdmin, Scope = RoleScope.Platform } }
            }
        };

        var mockTotp = new Mock<ITotpService>();
        mockTotp.Setup(t => t.Verify(rawSecret, "123456")).Returns(true);
        _userRepo.Setup(r => r.GetByIdWithRolesAsync(userId)).ReturnsAsync(user);
        _fieldEncryption.Setup(e => e.Decrypt(encryptedSecret)).Returns(rawSecret);
        // IP guard reports a new IP
        _ipGuardService.Setup(g => g.IsNewIpAsync("superadmin@system.com", "192.168.1.99"))
            .ReturnsAsync(true);
        _jwtService.Setup(j => j.GenerateAccessToken(user, It.IsAny<IList<string>>(), It.IsAny<string?>(), true))
            .Returns("access_token");
        _jwtService.Setup(j => j.GenerateRefreshToken()).Returns("raw_refresh");
        _passwordHasher.Setup(h => h.Hash("raw_refresh")).Returns("hashed_refresh");
        _auditLog.Setup(a => a.LogAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<object?>(), It.IsAny<object?>(),
            It.IsAny<string>(), It.IsAny<string>())).Returns(Task.CompletedTask);
        _uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);

        var service = CreateService();

        // Act
        var result = await service.LoginMfaAsync(
            new LoginMfaRequest { UserId = userId, Code = "123456" },
            mockTotp.Object, "192.168.1.99", "Mozilla");

        // Assert: login succeeds AND the user entity carries the domain event
        result.IsSuccess.Should().BeTrue();
        user.DomainEvents.Should().ContainSingle(e => e is SuperAdminLoginFromNewIpEvent)
            .Which.As<SuperAdminLoginFromNewIpEvent>()
            .NewIpAddress.Should().Be("192.168.1.99");
    }

    [Fact]
    public async Task LoginMfaAsync_NonSuperAdmin_NewIp_DoesNotRaiseSuperAdminLoginFromNewIpEvent()
    {
        // Arrange: non-SuperAdmin (AdminOwner) with valid TOTP + a new (unknown) IP — must NOT get event
        var userId = Guid.NewGuid();
        const string rawSecret = "JBSWY3DPEHPK3PXP";
        const string encryptedSecret = "enc:JBSWY3DPEHPK3PXP";
        var tenantId = Guid.NewGuid();
        var tenant = new Tenant { Id = tenantId, Slug = "brand-slug" };

        var mfaSettings = new UserMfaSettings
        {
            TotpSecretEncrypted = encryptedSecret,
            IsEnabled = true,
            IsEnrolled = true
        };
        var user = new User
        {
            Id = userId, Email = "owner@brand.com",
            PasswordHash = "hash", IsActive = true, TenantId = tenantId,
            Tenant = tenant,
            MfaSettings = mfaSettings,
            UserRoles = new List<UserRole>
            {
                new() { Role = new Role { Name = RoleType.AdminOwner, Scope = RoleScope.Tenant } }
            }
        };

        var mockTotp = new Mock<ITotpService>();
        mockTotp.Setup(t => t.Verify(rawSecret, "654321")).Returns(true);
        _userRepo.Setup(r => r.GetByIdWithRolesAsync(userId)).ReturnsAsync(user);
        _fieldEncryption.Setup(e => e.Decrypt(encryptedSecret)).Returns(rawSecret);
        // IP guard would report a new IP if called — but it should NOT be called for non-SuperAdmin
        _ipGuardService.Setup(g => g.IsNewIpAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true);
        _jwtService.Setup(j => j.GenerateAccessToken(user, It.IsAny<IList<string>>(), It.IsAny<string?>(), true))
            .Returns("access_token");
        _jwtService.Setup(j => j.GenerateRefreshToken()).Returns("raw_refresh");
        _passwordHasher.Setup(h => h.Hash("raw_refresh")).Returns("hashed_refresh");
        _auditLog.Setup(a => a.LogAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<object?>(), It.IsAny<object?>(),
            It.IsAny<string>(), It.IsAny<string>())).Returns(Task.CompletedTask);
        _uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);

        var service = CreateService();

        // Act
        var result = await service.LoginMfaAsync(
            new LoginMfaRequest { UserId = userId, Code = "654321" },
            mockTotp.Object, "192.168.1.99", "Mozilla");

        // Assert: login succeeds but NO SuperAdminLoginFromNewIpEvent is present
        result.IsSuccess.Should().BeTrue();
        user.DomainEvents.Should().NotContain(e => e is SuperAdminLoginFromNewIpEvent);
        // Also verify the IP guard was never queried for a non-SuperAdmin
        _ipGuardService.Verify(g => g.IsNewIpAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }
}
