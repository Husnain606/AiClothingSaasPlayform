using System.Collections.ObjectModel;
using FashionSaaS.Application.Common;
using FashionSaaS.Application.Configuration;
using FashionSaaS.Application.Interfaces;
using FashionSaaS.Application.Mfa;
using FashionSaaS.Application.Mfa.DTOs;
using FashionSaaS.Domain.Entities;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;

namespace FashionSaaS.Application.Tests.Mfa;

public class MfaServiceTests
{
    private readonly Mock<IUserRepository> _userRepo = new();
    private readonly Mock<ITotpService> _totpService = new();
    private readonly Mock<IPasswordHasher> _passwordHasher = new();
    private readonly Mock<IFieldEncryptionService> _fieldEncryption = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly IOptions<JwtSettings> _jwtOptions;

    public MfaServiceTests()
    {
        _jwtOptions = Options.Create(new JwtSettings { Issuer = "FashionSaaS" });
    }

    private MfaService CreateService() => new(
        _userRepo.Object, _totpService.Object, _passwordHasher.Object,
        _fieldEncryption.Object, _uow.Object, _jwtOptions);

    // ------------------------------------------------------------------
    // SetupAsync
    // ------------------------------------------------------------------

    [Fact]
    public async Task SetupAsync_UserNotFound_ReturnsFailure()
    {
        _userRepo.Setup(r => r.GetByIdWithRolesAsync(It.IsAny<Guid>())).ReturnsAsync((User?)null);

        MfaService service = CreateService();
        ResponseData<MfaSetupResponse> result = await service.SetupAsync(Guid.NewGuid());

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task SetupAsync_NewMfaSettings_EncryptsSecretBeforeStoring()
    {
        var userId = Guid.NewGuid();
        var user = new User { Id = userId, Email = "sa@system.com", PasswordHash = "h", IsActive = true, MfaSettings = null };
        _userRepo.Setup(r => r.GetByIdWithRolesAsync(userId)).ReturnsAsync(user);
        _totpService.Setup(t => t.GenerateSetup("sa@system.com", "FashionSaaS"))
            .Returns(("RAWSECRET", "otpauth://..."));
        _fieldEncryption.Setup(e => e.Encrypt("RAWSECRET")).Returns("ENCRYPTEDSECRET");
        _userRepo.Setup(r => r.UpdateAsync(user)).Returns(Task.CompletedTask);
        _uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);

        MfaService service = CreateService();
        ResponseData<MfaSetupResponse> result = await service.SetupAsync(userId);

        result.IsSuccess.Should().BeTrue();
        result.Data!.SecretBase32.Should().Be("RAWSECRET");
        result.Data.QrCodeUrl.Should().Be("otpauth://...");
        // Verify encryption was applied before storing
        user.MfaSettings!.TotpSecretEncrypted.Should().Be("ENCRYPTEDSECRET");
        _fieldEncryption.Verify(e => e.Encrypt("RAWSECRET"), Times.Once);
    }

    [Fact]
    public async Task SetupAsync_ExistingMfaSettings_ResetsEnrolledFlag()
    {
        var userId = Guid.NewGuid();
        var existingSettings = new UserMfaSettings { UserId = userId, IsEnabled = true, IsEnrolled = true, TotpSecretEncrypted = "oldenc" };
        var user = new User { Id = userId, Email = "sa@system.com", PasswordHash = "h", IsActive = true, MfaSettings = existingSettings };

        _userRepo.Setup(r => r.GetByIdWithRolesAsync(userId)).ReturnsAsync(user);
        _totpService.Setup(t => t.GenerateSetup(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(("NEWSECRET", "otpauth://new"));
        _fieldEncryption.Setup(e => e.Encrypt("NEWSECRET")).Returns("NEWENC");
        _userRepo.Setup(r => r.UpdateAsync(user)).Returns(Task.CompletedTask);
        _uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);

        MfaService service = CreateService();
        ResponseData<MfaSetupResponse> result = await service.SetupAsync(userId);

        result.IsSuccess.Should().BeTrue();
        // IsEnrolled must be reset so user has to re-verify
        user.MfaSettings!.IsEnrolled.Should().BeFalse();
        user.MfaSettings.TotpSecretEncrypted.Should().Be("NEWENC");
    }

    // ------------------------------------------------------------------
    // VerifySetupAsync
    // ------------------------------------------------------------------

    [Fact]
    public async Task VerifySetupAsync_NoMfaSettings_ReturnsFailure()
    {
        var user = new User { Id = Guid.NewGuid(), Email = "u@test.com", PasswordHash = "h", IsActive = true, MfaSettings = null };
        _userRepo.Setup(r => r.GetByIdWithRolesAsync(It.IsAny<Guid>())).ReturnsAsync(user);

        MfaService service = CreateService();
        ResponseData<IReadOnlyList<string>> result = await service.VerifySetupAsync(user.Id, "123456");

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task VerifySetupAsync_InvalidCode_ReturnsFailure()
    {
        var mfaSettings = new UserMfaSettings { TotpSecretEncrypted = "enc", IsEnrolled = false };
        var user = new User { Id = Guid.NewGuid(), Email = "u@test.com", PasswordHash = "h", IsActive = true, MfaSettings = mfaSettings };
        _userRepo.Setup(r => r.GetByIdWithRolesAsync(user.Id)).ReturnsAsync(user);
        _fieldEncryption.Setup(e => e.Decrypt("enc")).Returns("RAWSECRET");
        _totpService.Setup(t => t.Verify("RAWSECRET", "000000")).Returns(false);

        MfaService service = CreateService();
        ResponseData<IReadOnlyList<string>> result = await service.VerifySetupAsync(user.Id, "000000");

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task VerifySetupAsync_ValidCode_EnablesMfa_Returns8BackupCodes()
    {
        var settingsId = Guid.NewGuid();
        var mfaSettings = new UserMfaSettings { Id = settingsId, TotpSecretEncrypted = "enc", IsEnrolled = false };
        var user = new User { Id = Guid.NewGuid(), Email = "u@test.com", PasswordHash = "h", IsActive = true, MfaSettings = mfaSettings };

        _userRepo.Setup(r => r.GetByIdWithRolesAsync(user.Id)).ReturnsAsync(user);
        _fieldEncryption.Setup(e => e.Decrypt("enc")).Returns("RAWSECRET");
        _totpService.Setup(t => t.Verify("RAWSECRET", "123456")).Returns(true);

        ReadOnlyCollection<string> rawCodes = Enumerable.Range(1, 8).Select(i => $"code{i:D8}").ToList().AsReadOnly();
        _totpService.Setup(t => t.GenerateBackupCodes()).Returns(rawCodes);
        _passwordHasher.Setup(h => h.Hash(It.IsAny<string>())).Returns<string>(c => $"hashed_{c}");
        _userRepo.Setup(r => r.UpdateAsync(user)).Returns(Task.CompletedTask);
        _uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);

        MfaService service = CreateService();
        ResponseData<IReadOnlyList<string>> result = await service.VerifySetupAsync(user.Id, "123456");

        result.IsSuccess.Should().BeTrue();
        result.Data.Should().HaveCount(8);
        user.MfaSettings!.IsEnabled.Should().BeTrue();
        user.MfaSettings.IsEnrolled.Should().BeTrue();
        // Backup codes should be stored hashed, not raw
        user.MfaSettings.BackupCodes.Should().HaveCount(8);
        user.MfaSettings.BackupCodes.All(c => c.CodeHash.StartsWith("hashed_", StringComparison.Ordinal)).Should().BeTrue();
    }

    // ------------------------------------------------------------------
    // RegenerateBackupCodesAsync
    // ------------------------------------------------------------------

    [Fact]
    public async Task RegenerateBackupCodesAsync_NotEnrolled_ReturnsFailure()
    {
        var mfaSettings = new UserMfaSettings { IsEnrolled = false };
        var user = new User { Id = Guid.NewGuid(), Email = "u@test.com", PasswordHash = "h", IsActive = true, MfaSettings = mfaSettings };
        _userRepo.Setup(r => r.GetByIdWithRolesAsync(user.Id)).ReturnsAsync(user);

        MfaService service = CreateService();
        ResponseData<IReadOnlyList<string>> result = await service.RegenerateBackupCodesAsync(user.Id);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task RegenerateBackupCodesAsync_Enrolled_ReplacesCodesAndReturnsRaw()
    {
        var settingsId = Guid.NewGuid();
        var existingCode = new MfaBackupCode { UserMfaSettingsId = settingsId, CodeHash = "oldhash" };
        var mfaSettings = new UserMfaSettings { Id = settingsId, IsEnrolled = true, IsEnabled = true };
        mfaSettings.BackupCodes.Add(existingCode);
        var user = new User { Id = Guid.NewGuid(), Email = "u@test.com", PasswordHash = "h", IsActive = true, MfaSettings = mfaSettings };

        _userRepo.Setup(r => r.GetByIdWithRolesAsync(user.Id)).ReturnsAsync(user);
        ReadOnlyCollection<string> rawCodes = Enumerable.Range(1, 8).Select(i => $"code{i:D8}").ToList().AsReadOnly();
        _totpService.Setup(t => t.GenerateBackupCodes()).Returns(rawCodes);
        _passwordHasher.Setup(h => h.Hash(It.IsAny<string>())).Returns<string>(c => $"hashed_{c}");
        _userRepo.Setup(r => r.UpdateAsync(user)).Returns(Task.CompletedTask);
        _uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);

        MfaService service = CreateService();
        ResponseData<IReadOnlyList<string>> result = await service.RegenerateBackupCodesAsync(user.Id);

        result.IsSuccess.Should().BeTrue();
        result.Data.Should().HaveCount(8);
        // Old code should be cleared; new 8 hashed codes should be present
        user.MfaSettings!.BackupCodes.Should().HaveCount(8);
        user.MfaSettings.BackupCodes.Should().NotContain(c => c.CodeHash == "oldhash");
    }
}
