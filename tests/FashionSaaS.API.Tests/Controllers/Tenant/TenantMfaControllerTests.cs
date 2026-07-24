using System.Security.Claims;
using FashionSaaS.API.Controllers.Tenant;
using FashionSaaS.Application.Common;
using FashionSaaS.Application.Configuration;
using FashionSaaS.Application.Interfaces;
using FashionSaaS.Application.Mfa;
using FashionSaaS.Application.Mfa.DTOs;
using FashionSaaS.Domain.Entities;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Moq;

namespace FashionSaaS.API.Tests.Controllers.Tenant;

/// <summary>
/// Verifies tenant staff (AdminOwner/StoreManager) can reach MFA enrollment through
/// this controller's routes, wired to the same MfaService the SuperAdmin-only
/// Admin/MfaController uses. Before this controller existed, tenant staff had no route
/// to satisfy TenantBankAccountController.GetFull's MFA-enrollment requirement.
/// </summary>
public class TenantMfaControllerTests
{
    private readonly Mock<IUserRepository> _userRepo = new();
    private readonly Mock<ITotpService> _totpService = new();
    private readonly Mock<IPasswordHasher> _passwordHasher = new();
    private readonly Mock<IFieldEncryptionService> _fieldEncryption = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly IOptions<JwtSettings> _jwtOptions = Options.Create(new JwtSettings { Issuer = "FashionSaaS" });

    private MfaService CreateService() => new(
        _userRepo.Object, _totpService.Object, _passwordHasher.Object,
        _fieldEncryption.Object, _uow.Object, _jwtOptions);

    private static TenantMfaController CreateController(MfaService service, Guid userId)
    {
        var controller = new TenantMfaController(service);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId.ToString())]));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = principal }
        };
        return controller;
    }

    [Fact]
    public async Task Setup_TenantOwnerNotYetEnrolled_ReturnsSetupResponse()
    {
        var userId = Guid.NewGuid();
        var user = new User { Id = userId, Email = "owner@chicboutique.dev", PasswordHash = "h", IsActive = true, MfaSettings = null };
        _userRepo.Setup(r => r.GetByIdWithRolesAsync(userId)).ReturnsAsync(user);
        _totpService.Setup(t => t.GenerateSetup("owner@chicboutique.dev", "FashionSaaS"))
            .Returns(("RAWSECRET", "otpauth://totp/FashionSaaS:owner@chicboutique.dev?secret=RAWSECRET"));
        _fieldEncryption.Setup(e => e.Encrypt("RAWSECRET")).Returns("ENCRYPTEDSECRET");

        TenantMfaController controller = CreateController(CreateService(), userId);
        var result = await controller.Setup() as ObjectResult;

        result.Should().NotBeNull();
        result!.StatusCode.Should().Be(200);
        var body = result.Value as ResponseData<MfaSetupResponse>;
        body!.IsSuccess.Should().BeTrue();
        body.Data!.SecretBase32.Should().Be("RAWSECRET");
    }

    [Fact]
    public async Task VerifySetup_ValidCode_EnrollsAndReturnsBackupCodes()
    {
        var userId = Guid.NewGuid();
        var user = new User
        {
            Id = userId,
            Email = "owner@chicboutique.dev",
            PasswordHash = "h",
            IsActive = true,
            MfaSettings = new UserMfaSettings
            {
                UserId = userId,
                IsEnabled = false,
                IsEnrolled = false,
                TotpSecretEncrypted = "ENCRYPTEDSECRET"
            }
        };
        _userRepo.Setup(r => r.GetByIdWithRolesAsync(userId)).ReturnsAsync(user);
        _fieldEncryption.Setup(e => e.Decrypt("ENCRYPTEDSECRET")).Returns("RAWSECRET");
        _totpService.Setup(t => t.Verify("RAWSECRET", "123456")).Returns(true);
        _totpService.Setup(t => t.GenerateBackupCodes()).Returns(["backup-1", "backup-2"]);
        _fieldEncryption.Setup(e => e.Encrypt(It.IsAny<string>())).Returns<string>(s => $"ENC:{s}");

        TenantMfaController controller = CreateController(CreateService(), userId);
        var result = await controller.VerifySetup(new TenantMfaController.VerifySetupRequest("123456")) as ObjectResult;

        result.Should().NotBeNull();
        result!.StatusCode.Should().Be(200);
        var body = result.Value as ResponseData<IReadOnlyList<string>>;
        body!.IsSuccess.Should().BeTrue();
        body.Data.Should().NotBeEmpty();
        user.MfaSettings!.IsEnrolled.Should().BeTrue();
    }

    [Fact]
    public async Task VerifySetup_InvalidCode_ReturnsFailure()
    {
        var userId = Guid.NewGuid();
        var user = new User
        {
            Id = userId,
            Email = "owner@chicboutique.dev",
            PasswordHash = "h",
            IsActive = true,
            MfaSettings = new UserMfaSettings
            {
                UserId = userId,
                IsEnabled = false,
                IsEnrolled = false,
                TotpSecretEncrypted = "ENCRYPTEDSECRET"
            }
        };
        _userRepo.Setup(r => r.GetByIdWithRolesAsync(userId)).ReturnsAsync(user);
        _fieldEncryption.Setup(e => e.Decrypt("ENCRYPTEDSECRET")).Returns("RAWSECRET");
        _totpService.Setup(t => t.Verify("RAWSECRET", "000000")).Returns(false);

        TenantMfaController controller = CreateController(CreateService(), userId);
        var result = await controller.VerifySetup(new TenantMfaController.VerifySetupRequest("000000")) as ObjectResult;

        result.Should().NotBeNull();
        result!.StatusCode.Should().Be(400);
        var body = result.Value as ResponseData<IReadOnlyList<string>>;
        body!.IsSuccess.Should().BeFalse();
        user.MfaSettings!.IsEnrolled.Should().BeFalse();
    }
}
