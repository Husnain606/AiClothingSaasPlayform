using FashionSaaS.Application.BankAccounts;
using FashionSaaS.Application.BankAccounts.DTOs;
using FashionSaaS.Application.Common;
using FashionSaaS.Application.Interfaces;
using FashionSaaS.Domain.Entities;
using FluentAssertions;
using Moq;

namespace FashionSaaS.Application.Tests.BankAccounts;

public class BankAccountServiceTests
{
    private readonly Mock<IBankAccountRepository> _bankRepo = new();
    private readonly Mock<IUserRepository> _userRepo = new();
    private readonly Mock<IFieldEncryptionService> _encryption = new();
    private readonly Mock<IPasswordHasher> _hasher = new();
    private readonly Mock<IAuditLogService> _audit = new();
    private readonly Mock<IEmailService> _email = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<ITotpService> _totp = new();

    private const string Ip = "127.0.0.1";
    private const string Ua = "xunit";

    private BankAccountService CreateService() => new(_bankRepo.Object, _userRepo.Object,
        _encryption.Object, _hasher.Object, _audit.Object, _email.Object, _uow.Object, _totp.Object);

    private void SetupUow() =>
        _uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);

    private void SetupAudit() =>
        _audit.Setup(a => a.LogAsync(
            It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<Guid>(), It.IsAny<object>(), It.IsAny<object>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

    /// <summary>
    /// Sets up a user with MFA enrolled and returns (userId, totpCode).
    /// _totp.Verify(secret, totpCode) returns true; _encryption.Decrypt("ENC(secret)") returns "secret".
    /// </summary>
    private (User user, Guid userId, string totpCode) SetupMfaUser()
    {
        var userId = Guid.NewGuid();
        var totpCode = "123456";
        var mfaSettings = new UserMfaSettings
        {
            IsEnrolled = true,
            TotpSecretEncrypted = "ENC(secret)"
        };
        var user = new User { Id = userId, PasswordHash = "hash", Email = "admin@x.com" };
        user.MfaSettings = mfaSettings;

        _userRepo.Setup(r => r.GetByIdWithRolesAsync(userId)).ReturnsAsync(user);
        _encryption.Setup(e => e.Decrypt("ENC(secret)")).Returns("secret");
        _totp.Setup(t => t.Verify("secret", totpCode)).Returns(true);

        return (user, userId, totpCode);
    }

    // ── CreateAsync: password gate ───────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_WrongPassword_ReturnsUnauthorized()
    {
        var user = new User { Id = Guid.NewGuid(), PasswordHash = "hash", Email = "a@b.com" };
        _userRepo.Setup(r => r.GetByIdAsync(user.Id)).ReturnsAsync(user);
        _hasher.Setup(h => h.Verify("wrong", "hash")).Returns(false);

        ResponseData<BankAccountResponse> result = await CreateService().CreateAsync(
            new CreateBankAccountRequest { CurrentPassword = "wrong" },
            user.Id, null, Ip, Ua);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task CreateAsync_UserNotFound_ReturnsUnauthorized()
    {
        _userRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((User?)null);

        ResponseData<BankAccountResponse> result = await CreateService().CreateAsync(
            new CreateBankAccountRequest { CurrentPassword = "pwd" },
            Guid.NewGuid(), null, Ip, Ua);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(401);
    }

    // ── CreateAsync: conflict guard ──────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_AccountAlreadyExists_Returns409()
    {
        var user = new User { Id = Guid.NewGuid(), PasswordHash = "hash", Email = "a@b.com" };
        _userRepo.Setup(r => r.GetByIdAsync(user.Id)).ReturnsAsync(user);
        _hasher.Setup(h => h.Verify("good", "hash")).Returns(true);
        _bankRepo.Setup(r => r.GetPlatformAccountAsync()).ReturnsAsync(new BankAccount());

        ResponseData<BankAccountResponse> result = await CreateService().CreateAsync(
            new CreateBankAccountRequest { CurrentPassword = "good" },
            user.Id, null, Ip, Ua);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
    }

    // ── CRITICAL: encryption enforced on write ───────────────────────────────

    [Fact]
    public async Task CreateAsync_ValidRequest_EncryptsAllFiveFieldsBeforePersist()
    {
        var user = new User { Id = Guid.NewGuid(), PasswordHash = "hash", Email = "admin@x.com" };
        _userRepo.Setup(r => r.GetByIdAsync(user.Id)).ReturnsAsync(user);
        _hasher.Setup(h => h.Verify("pwd", "hash")).Returns(true);
        _bankRepo.Setup(r => r.GetPlatformAccountAsync()).ReturnsAsync((BankAccount?)null);
        _bankRepo.Setup(r => r.AddAsync(It.IsAny<BankAccount>())).Returns(Task.CompletedTask);
        _encryption.Setup(e => e.Encrypt(It.IsAny<string>())).Returns<string>(s => $"ENC({s})");
        _encryption.Setup(e => e.Decrypt(It.IsAny<string>())).Returns<string>(s => s);
        _encryption.Setup(e => e.MaskAccountNumber(It.IsAny<string>())).Returns("****5678");
        _email.Setup(e => e.SendBankAccountChangedAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        SetupUow();
        SetupAudit();

        var request = new CreateBankAccountRequest
        {
            AccountTitle = "ACME Corp",
            AccountNumber = "12345678",
            BankName = "HBL",
            BranchCode = "0012",
            Iban = "PK36SCBL0000001123456702",
            CurrentPassword = "pwd"
        };

        BankAccount? captured = null;
        _bankRepo.Setup(r => r.AddAsync(It.IsAny<BankAccount>()))
            .Callback<BankAccount>(a => captured = a)
            .Returns(Task.CompletedTask);

        ResponseData<BankAccountResponse> result = await CreateService().CreateAsync(request, user.Id, null, Ip, Ua);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(201);

        // All five fields must be stored encrypted — never plaintext
        captured.Should().NotBeNull();
        captured!.AccountTitleEncrypted.Should().Be("ENC(ACME Corp)");
        captured.AccountNumberEncrypted.Should().Be("ENC(12345678)");
        captured.BankNameEncrypted.Should().Be("ENC(HBL)");
        captured.BranchCodeEncrypted.Should().Be("ENC(0012)");
        captured.IbanEncrypted.Should().Be("ENC(PK36SCBL0000001123456702)");

        // Verify Encrypt called exactly 5 times (once per field)
        _encryption.Verify(e => e.Encrypt(It.IsAny<string>()), Times.Exactly(5));
    }

    // ── CRITICAL: AccountNumber masked as ****{last4} in all responses ───────

    [Fact]
    public async Task CreateAsync_Response_AccountNumberIsMasked()
    {
        var user = new User { Id = Guid.NewGuid(), PasswordHash = "hash", Email = "admin@x.com" };
        _userRepo.Setup(r => r.GetByIdAsync(user.Id)).ReturnsAsync(user);
        _hasher.Setup(h => h.Verify("pwd", "hash")).Returns(true);
        _bankRepo.Setup(r => r.GetPlatformAccountAsync()).ReturnsAsync((BankAccount?)null);
        _bankRepo.Setup(r => r.AddAsync(It.IsAny<BankAccount>())).Returns(Task.CompletedTask);
        _encryption.Setup(e => e.Encrypt(It.IsAny<string>())).Returns<string>(s => $"ENC({s})");
        _encryption.Setup(e => e.Decrypt(It.IsAny<string>())).Returns<string>(s =>
            s.StartsWith("ENC(", StringComparison.Ordinal) ? s[4..^1] : s);
        _encryption.Setup(e => e.MaskAccountNumber("12345678")).Returns("****5678");
        _email.Setup(e => e.SendBankAccountChangedAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        SetupUow();
        SetupAudit();

        ResponseData<BankAccountResponse> result = await CreateService().CreateAsync(
            new CreateBankAccountRequest
            {
                AccountTitle = "ACME Corp",
                AccountNumber = "12345678",
                BankName = "HBL",
                BranchCode = "0012",
                Iban = "PK36SCBL0000001123456702",
                CurrentPassword = "pwd"
            },
            user.Id, null, Ip, Ua);

        result.IsSuccess.Should().BeTrue();
        result.Data!.AccountNumber.Should().Be("****5678");
        result.Data.AccountNumber.Should().NotBe("12345678");
    }

    [Fact]
    public async Task GetAsync_Response_AccountNumberIsMasked()
    {
        var account = new BankAccount
        {
            Id = Guid.NewGuid(),
            TenantId = null,
            AccountTitleEncrypted = "ENC(ACME)",
            AccountNumberEncrypted = "ENC(12345678)",
            BankNameEncrypted = "ENC(HBL)",
            BranchCodeEncrypted = "ENC(0012)",
            IbanEncrypted = "ENC(PK36SCBL)",
            IsActive = true
        };
        _bankRepo.Setup(r => r.GetPlatformAccountAsync()).ReturnsAsync(account);
        _encryption.Setup(e => e.Decrypt(It.IsAny<string>())).Returns<string>(s =>
            s.StartsWith("ENC(", StringComparison.Ordinal) ? s[4..^1] : s);
        _encryption.Setup(e => e.MaskAccountNumber("12345678")).Returns("****5678");

        ResponseData<BankAccountResponse> result = await CreateService().GetAsync(null);

        result.IsSuccess.Should().BeTrue();
        result.Data!.AccountNumber.Should().Be("****5678");
        result.Data.AccountNumber.Should().NotBe("12345678");

        // Decrypt was called for all 5 fields
        _encryption.Verify(e => e.Decrypt(It.IsAny<string>()), Times.Exactly(5));
        // MaskAccountNumber called exactly once (only for AccountNumber)
        _encryption.Verify(e => e.MaskAccountNumber(It.IsAny<string>()), Times.Once);
    }

    // ── GetAsync: not found ──────────────────────────────────────────────────

    [Fact]
    public async Task GetAsync_NoAccount_ReturnsNotFound()
    {
        _bankRepo.Setup(r => r.GetPlatformAccountAsync()).ReturnsAsync((BankAccount?)null);
        ResponseData<BankAccountResponse> result = await CreateService().GetAsync(null);
        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task GetAsync_TenantAccount_NotFound_ReturnsNotFound()
    {
        var tenantId = Guid.NewGuid();
        _bankRepo.Setup(r => r.GetByTenantIdAsync(tenantId)).ReturnsAsync((BankAccount?)null);
        ResponseData<BankAccountResponse> result = await CreateService().GetAsync(tenantId);
        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    // ── UpdateAsync: encryption on update ───────────────────────────────────

    [Fact]
    public async Task UpdateAsync_ValidRequest_EncryptsAllFiveFieldsOnUpdate()
    {
        var user = new User { Id = Guid.NewGuid(), PasswordHash = "hash", Email = "admin@x.com" };
        var account = new BankAccount
        {
            Id = Guid.NewGuid(),
            TenantId = null,
            AccountTitleEncrypted = "ENC(OldTitle)",
            AccountNumberEncrypted = "ENC(11111111)",
            BankNameEncrypted = "ENC(OldBank)",
            BranchCodeEncrypted = "ENC(0000)",
            IbanEncrypted = "ENC(OLDIBAN)",
            IsActive = true
        };

        _userRepo.Setup(r => r.GetByIdAsync(user.Id)).ReturnsAsync(user);
        _hasher.Setup(h => h.Verify("pwd", "hash")).Returns(true);
        _bankRepo.Setup(r => r.GetPlatformAccountAsync()).ReturnsAsync(account);
        _bankRepo.Setup(r => r.UpdateAsync(account)).Returns(Task.CompletedTask);
        _encryption.Setup(e => e.Encrypt(It.IsAny<string>())).Returns<string>(s => $"ENC({s})");
        _encryption.Setup(e => e.Decrypt(It.IsAny<string>())).Returns<string>(s =>
            s.StartsWith("ENC(", StringComparison.Ordinal) ? s[4..^1] : s);
        _encryption.Setup(e => e.MaskAccountNumber(It.IsAny<string>())).Returns<string>(s =>
            $"****{s[^4..]}");
        _email.Setup(e => e.SendBankAccountChangedAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        SetupUow();
        SetupAudit();

        var request = new UpdateBankAccountRequest
        {
            AccountTitle = "NewTitle",
            AccountNumber = "99998888",
            BankName = "MCB",
            BranchCode = "0099",
            Iban = "PK36SCBL0000009999888877",
            CurrentPassword = "pwd"
        };

        ResponseData<BankAccountResponse> result = await CreateService().UpdateAsync(request, user.Id, null, Ip, Ua);

        result.IsSuccess.Should().BeTrue();

        account.AccountTitleEncrypted.Should().Be("ENC(NewTitle)");
        account.AccountNumberEncrypted.Should().Be("ENC(99998888)");
        account.BankNameEncrypted.Should().Be("ENC(MCB)");
        account.BranchCodeEncrypted.Should().Be("ENC(0099)");
        account.IbanEncrypted.Should().Be("ENC(PK36SCBL0000009999888877)");

        // 5 encrypts for update + 1 decrypt for old masked value in audit
        _encryption.Verify(e => e.Encrypt(It.IsAny<string>()), Times.Exactly(5));
    }

    // ── UpdateAsync: audit must NOT log plaintext AccountNumber / IBAN ───────

    [Fact]
    public async Task UpdateAsync_AuditLog_ContainsMaskedAccountNumberNotPlaintext()
    {
        var user = new User { Id = Guid.NewGuid(), PasswordHash = "hash", Email = "admin@x.com" };
        var account = new BankAccount
        {
            Id = Guid.NewGuid(),
            TenantId = null,
            AccountTitleEncrypted = "ENC(T)",
            AccountNumberEncrypted = "ENC(12345678)",
            BankNameEncrypted = "ENC(B)",
            BranchCodeEncrypted = "ENC(C)",
            IbanEncrypted = "ENC(IBAN)",
            IsActive = true
        };

        _userRepo.Setup(r => r.GetByIdAsync(user.Id)).ReturnsAsync(user);
        _hasher.Setup(h => h.Verify("pwd", "hash")).Returns(true);
        _bankRepo.Setup(r => r.GetPlatformAccountAsync()).ReturnsAsync(account);
        _bankRepo.Setup(r => r.UpdateAsync(account)).Returns(Task.CompletedTask);
        _encryption.Setup(e => e.Encrypt(It.IsAny<string>())).Returns<string>(s => $"ENC({s})");
        _encryption.Setup(e => e.Decrypt("ENC(12345678)")).Returns("12345678");
        _encryption.Setup(e => e.Decrypt(It.Is<string>(s => s != "ENC(12345678)"))).Returns("decrypted");
        _encryption.Setup(e => e.MaskAccountNumber("12345678")).Returns("****5678");
        _encryption.Setup(e => e.MaskAccountNumber("87654321")).Returns("****4321");
        _email.Setup(e => e.SendBankAccountChangedAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        SetupUow();

        object? capturedNewValues = null;
        object? capturedOldValues = null;
        _audit.Setup(a => a.LogAsync(
                It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<Guid>(), It.IsAny<object>(), It.IsAny<object>(), It.IsAny<string>(), It.IsAny<string>()))
            .Callback<Guid?, Guid?, string, string, Guid, object?, object?, string, string>(
                (_, _, _, _, _, old, newV, _, _) => { capturedOldValues = old; capturedNewValues = newV; })
            .Returns(Task.CompletedTask);

        var request = new UpdateBankAccountRequest
        {
            AccountTitle = "T",
            AccountNumber = "87654321",
            BankName = "B",
            BranchCode = "C",
            Iban = "IBAN",
            CurrentPassword = "pwd"
        };

        await CreateService().UpdateAsync(request, user.Id, null, Ip, Ua);

        // Audit new values must use masked AccountNumber
        capturedNewValues.Should().NotBeNull();
        var newValStr = capturedNewValues!.ToString()!;
        newValStr.Should().Contain("****4321");
        newValStr.Should().NotContain("87654321");

        // Audit old values must also use masked AccountNumber
        capturedOldValues.Should().NotBeNull();
        var oldValStr = capturedOldValues!.ToString()!;
        oldValStr.Should().Contain("****5678");
        oldValStr.Should().NotContain("12345678");
    }

    // ── C1: GetAsync masked response — IBAN must be ****{last4} ─────────────

    [Fact]
    public async Task GetAsync_Response_IbanIsMaskedAsLast4()
    {
        var account = new BankAccount
        {
            Id = Guid.NewGuid(),
            TenantId = null,
            AccountTitleEncrypted = "ENC(ACME)",
            AccountNumberEncrypted = "ENC(12345678)",
            BankNameEncrypted = "ENC(HBL)",
            BranchCodeEncrypted = "ENC(0012)",
            IbanEncrypted = "ENC(PK36SCBL0000001123456702)",
            IsActive = true
        };
        _bankRepo.Setup(r => r.GetPlatformAccountAsync()).ReturnsAsync(account);
        _encryption.Setup(e => e.Decrypt(It.IsAny<string>())).Returns<string>(s =>
            s.StartsWith("ENC(", StringComparison.Ordinal) ? s[4..^1] : s);
        _encryption.Setup(e => e.MaskAccountNumber(It.IsAny<string>())).Returns("****5678");

        ResponseData<BankAccountResponse> result = await CreateService().GetAsync(null);

        result.IsSuccess.Should().BeTrue();
        // IBAN must be masked to ****{last4} — NOT the full plaintext
        result.Data!.Iban.Should().Be("****6702");
        result.Data.Iban.Should().NotBe("PK36SCBL0000001123456702");
        result.Data.Iban.Should().StartWith("****");
    }

    [Fact]
    public async Task GetFullAsync_ReturnsFullDecryptedIban_NotMasked()
    {
        (User _, Guid userId, var totpCode) = SetupMfaUser();
        var account = new BankAccount
        {
            Id = Guid.NewGuid(),
            TenantId = null,
            AccountTitleEncrypted = "ENC(ACME)",
            AccountNumberEncrypted = "ENC(12345678)",
            BankNameEncrypted = "ENC(HBL)",
            BranchCodeEncrypted = "ENC(0012)",
            IbanEncrypted = "ENC(PK36SCBL0000001123456702)",
            IsActive = true
        };
        _bankRepo.Setup(r => r.GetPlatformAccountAsync()).ReturnsAsync(account);
        _encryption.Setup(e => e.Decrypt(It.Is<string>(s => s != "ENC(secret)"))).Returns<string>(s =>
            s.StartsWith("ENC(", StringComparison.Ordinal) ? s[4..^1] : s);

        ResponseData<BankAccountFullResponse> result = await CreateService().GetFullAsync(null, userId, totpCode);

        result.IsSuccess.Should().BeTrue();
        // GetFullAsync must return the COMPLETE unmasked IBAN
        result.Data!.Iban.Should().Be("PK36SCBL0000001123456702");
        result.Data.Iban.Should().NotStartWith("****");
        // MaskAccountNumber must never be called on the full-fetch path
        _encryption.Verify(e => e.MaskAccountNumber(It.IsAny<string>()), Times.Never);
    }

    // ── GetFullAsync: returns unmasked AccountNumber ─────────────────────────

    [Fact]
    public async Task GetFullAsync_ReturnsFullDecryptedAccountNumber_NotMasked()
    {
        (User _, Guid userId, var totpCode) = SetupMfaUser();
        var account = new BankAccount
        {
            Id = Guid.NewGuid(),
            TenantId = null,
            AccountTitleEncrypted = "ENC(ACME)",
            AccountNumberEncrypted = "ENC(12345678)",
            BankNameEncrypted = "ENC(HBL)",
            BranchCodeEncrypted = "ENC(0012)",
            IbanEncrypted = "ENC(PK36SCBL)",
            IsActive = true
        };
        _bankRepo.Setup(r => r.GetPlatformAccountAsync()).ReturnsAsync(account);
        _encryption.Setup(e => e.Decrypt(It.Is<string>(s => s != "ENC(secret)"))).Returns<string>(s =>
            s.StartsWith("ENC(", StringComparison.Ordinal) ? s[4..^1] : s);

        ResponseData<BankAccountFullResponse> result = await CreateService().GetFullAsync(null, userId, totpCode);

        result.IsSuccess.Should().BeTrue();
        // Must be full plaintext, not masked
        result.Data!.AccountNumber.Should().Be("12345678");
        result.Data.AccountNumber.Should().NotStartWith("****");

        // MaskAccountNumber must NEVER be called for the full-fetch path
        _encryption.Verify(e => e.MaskAccountNumber(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task GetFullAsync_NoAccount_ReturnsNotFound()
    {
        (User _, Guid userId, var totpCode) = SetupMfaUser();
        _bankRepo.Setup(r => r.GetPlatformAccountAsync()).ReturnsAsync((BankAccount?)null);
        ResponseData<BankAccountFullResponse> result = await CreateService().GetFullAsync(null, userId, totpCode);
        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task GetFullAsync_TenantAccount_ReturnsFullDecryptedAccountNumber()
    {
        (User _, Guid userId, var totpCode) = SetupMfaUser();
        var tenantId = Guid.NewGuid();
        var account = new BankAccount
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            AccountTitleEncrypted = "ENC(TenantCorp)",
            AccountNumberEncrypted = "ENC(98765432)",
            BankNameEncrypted = "ENC(MCB)",
            BranchCodeEncrypted = "ENC(0099)",
            IbanEncrypted = "ENC(PK36MCB)",
            IsActive = true
        };
        _bankRepo.Setup(r => r.GetByTenantIdAsync(tenantId)).ReturnsAsync(account);
        _encryption.Setup(e => e.Decrypt(It.Is<string>(s => s != "ENC(secret)"))).Returns<string>(s =>
            s.StartsWith("ENC(", StringComparison.Ordinal) ? s[4..^1] : s);

        ResponseData<BankAccountFullResponse> result = await CreateService().GetFullAsync(tenantId, userId, totpCode);

        result.IsSuccess.Should().BeTrue();
        result.Data!.AccountNumber.Should().Be("98765432");
        result.Data.AccountNumber.Should().NotStartWith("****");
        _encryption.Verify(e => e.MaskAccountNumber(It.IsAny<string>()), Times.Never);
    }

    // ── UpdateAsync: domain event raised ────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_RaisesBankAccountChangedEvent()
    {
        var user = new User { Id = Guid.NewGuid(), PasswordHash = "hash", Email = "admin@x.com" };
        var account = new BankAccount
        {
            Id = Guid.NewGuid(),
            TenantId = null,
            AccountTitleEncrypted = "ENC(T)",
            AccountNumberEncrypted = "ENC(11111111)",
            BankNameEncrypted = "ENC(B)",
            BranchCodeEncrypted = "ENC(C)",
            IbanEncrypted = "ENC(I)",
            IsActive = true
        };

        _userRepo.Setup(r => r.GetByIdAsync(user.Id)).ReturnsAsync(user);
        _hasher.Setup(h => h.Verify("pwd", "hash")).Returns(true);
        _bankRepo.Setup(r => r.GetPlatformAccountAsync()).ReturnsAsync(account);
        _bankRepo.Setup(r => r.UpdateAsync(account)).Returns(Task.CompletedTask);
        _encryption.Setup(e => e.Encrypt(It.IsAny<string>())).Returns("cipher");
        _encryption.Setup(e => e.Decrypt(It.IsAny<string>())).Returns("plain");
        _encryption.Setup(e => e.MaskAccountNumber(It.IsAny<string>())).Returns("****0000");
        _email.Setup(e => e.SendBankAccountChangedAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        SetupUow();
        SetupAudit();

        await CreateService().UpdateAsync(
            new UpdateBankAccountRequest
            {
                AccountTitle = "T",
                AccountNumber = "1234",
                BankName = "B",
                BranchCode = "C",
                Iban = "I",
                CurrentPassword = "pwd"
            },
            user.Id, null, Ip, Ua);

        account.DomainEvents.Should().ContainSingle(e => e.GetType().Name == "BankAccountChangedEvent");
    }

    // ── CreateAsync: domain event raised ────────────────────────────────────

    [Fact]
    public async Task CreateAsync_RaisesBankAccountChangedEvent()
    {
        var user = new User { Id = Guid.NewGuid(), PasswordHash = "hash", Email = "admin@x.com" };
        _userRepo.Setup(r => r.GetByIdAsync(user.Id)).ReturnsAsync(user);
        _hasher.Setup(h => h.Verify("pwd", "hash")).Returns(true);
        _bankRepo.Setup(r => r.GetPlatformAccountAsync()).ReturnsAsync((BankAccount?)null);
        _encryption.Setup(e => e.Encrypt(It.IsAny<string>())).Returns("cipher");
        _encryption.Setup(e => e.Decrypt(It.IsAny<string>())).Returns("plain");
        _encryption.Setup(e => e.MaskAccountNumber(It.IsAny<string>())).Returns("****0000");
        _email.Setup(e => e.SendBankAccountChangedAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        SetupUow();
        SetupAudit();

        BankAccount? captured = null;
        _bankRepo.Setup(r => r.AddAsync(It.IsAny<BankAccount>()))
            .Callback<BankAccount>(a => captured = a)
            .Returns(Task.CompletedTask);

        await CreateService().CreateAsync(
            new CreateBankAccountRequest
            {
                AccountTitle = "T",
                AccountNumber = "1234",
                BankName = "B",
                BranchCode = "C",
                Iban = "I",
                CurrentPassword = "pwd"
            },
            user.Id, null, Ip, Ua);

        captured.Should().NotBeNull();
        captured!.DomainEvents.Should().ContainSingle(e => e.GetType().Name == "BankAccountChangedEvent");
    }

    // ── GetFullAsync: TOTP step-up gate ─────────────────────────────────────

    [Fact]
    public async Task GetFullAsync_WrongTotpCode_Returns403()
    {
        var userId = Guid.NewGuid();
        var mfaSettings = new UserMfaSettings { IsEnrolled = true, TotpSecretEncrypted = "ENC(secret)" };
        var user = new User { Id = userId, PasswordHash = "hash", Email = "admin@x.com" };
        user.MfaSettings = mfaSettings;

        _userRepo.Setup(r => r.GetByIdWithRolesAsync(userId)).ReturnsAsync(user);
        _encryption.Setup(e => e.Decrypt("ENC(secret)")).Returns("secret");
        _totp.Setup(t => t.Verify("secret", "wrong")).Returns(false);

        ResponseData<BankAccountFullResponse> result = await CreateService().GetFullAsync(null, userId, "wrong");

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
        result.Message.Should().Be("Invalid verification code.");
        _bankRepo.Verify(r => r.GetPlatformAccountAsync(), Times.Never);
    }

    [Fact]
    public async Task GetFullAsync_UserNotEnrolledInMfa_Returns403()
    {
        var userId = Guid.NewGuid();
        var user = new User { Id = userId, PasswordHash = "hash", Email = "admin@x.com" };
        // MfaSettings is null — not enrolled
        _userRepo.Setup(r => r.GetByIdWithRolesAsync(userId)).ReturnsAsync(user);

        ResponseData<BankAccountFullResponse> result = await CreateService().GetFullAsync(null, userId, "123456");

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
        result.Message.Should().Be("MFA required.");
        _bankRepo.Verify(r => r.GetPlatformAccountAsync(), Times.Never);
    }

    [Fact]
    public async Task GetFullAsync_CorrectTotpCode_ReturnsFullAccountNumber()
    {
        (User _, Guid userId, var totpCode) = SetupMfaUser();
        var account = new BankAccount
        {
            Id = Guid.NewGuid(),
            TenantId = null,
            AccountTitleEncrypted = "ENC(Corp)",
            AccountNumberEncrypted = "ENC(99998888)",
            BankNameEncrypted = "ENC(HBL)",
            BranchCodeEncrypted = "ENC(0012)",
            IbanEncrypted = "ENC(PK36HBL)",
            IsActive = true
        };
        _bankRepo.Setup(r => r.GetPlatformAccountAsync()).ReturnsAsync(account);
        _encryption.Setup(e => e.Decrypt(It.Is<string>(s => s != "ENC(secret)"))).Returns<string>(s =>
            s.StartsWith("ENC(", StringComparison.Ordinal) ? s[4..^1] : s);

        ResponseData<BankAccountFullResponse> result = await CreateService().GetFullAsync(null, userId, totpCode);

        result.IsSuccess.Should().BeTrue();
        result.Data!.AccountNumber.Should().Be("99998888");
        result.Data.AccountNumber.Should().NotStartWith("****");
        _encryption.Verify(e => e.MaskAccountNumber(It.IsAny<string>()), Times.Never);
    }
}
