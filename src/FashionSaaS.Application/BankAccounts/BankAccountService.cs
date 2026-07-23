using FashionSaaS.Application.BankAccounts.DTOs;
using FashionSaaS.Application.Common;
using FashionSaaS.Application.Interfaces;
using FashionSaaS.Domain.Entities;
using FashionSaaS.Domain.Events;
using Microsoft.Extensions.Logging;

namespace FashionSaaS.Application.BankAccounts;

public class BankAccountService(
    IBankAccountRepository bankAccountRepository,
    IUserRepository userRepository,
    IFieldEncryptionService fieldEncryption,
    IPasswordHasher passwordHasher,
    IAuditLogService auditLogService,
    IEmailService emailService,
    IUnitOfWork unitOfWork,
    ITotpService totpService,
    ILogger<BankAccountService> logger)
{
    public async Task<ResponseData<BankAccountResponse>> GetAsync(Guid? tenantId)
    {
        BankAccount? account = tenantId.HasValue
            ? await bankAccountRepository.GetByTenantIdAsync(tenantId.Value)
            : await bankAccountRepository.GetPlatformAccountAsync();

        if (account is null)
            return ResponseData<BankAccountResponse>.Failure("Bank account not found.", 404);

        return ResponseData<BankAccountResponse>.Success(MapMasked(account));
    }

    /// <summary>
    /// Returns the bank account with the AccountNumber FULLY DECRYPTED and UNMASKED.
    /// <para>
    /// SENSITIVE: Requires the caller to supply a current TOTP code verified against their own
    /// MFA secret (step-up re-verification) before plaintext data is returned.
    /// </para>
    /// </summary>
    public async Task<ResponseData<BankAccountFullResponse>> GetFullAsync(
        Guid? tenantId, Guid requestingUserId, string totpCode)
    {
        // Step-up: load the requesting user's MFA settings (navigation included by GetByIdWithRolesAsync)
        User? user = await userRepository.GetByIdWithRolesAsync(requestingUserId);
        if (user?.MfaSettings is null || !user.MfaSettings.IsEnrolled)
            return ResponseData<BankAccountFullResponse>.Failure("MFA required.", 403);

        var secret = fieldEncryption.Decrypt(user.MfaSettings.TotpSecretEncrypted!);
        if (!totpService.Verify(secret, totpCode))
            return ResponseData<BankAccountFullResponse>.Failure("Invalid verification code.", 403);

        BankAccount? account = tenantId.HasValue
            ? await bankAccountRepository.GetByTenantIdAsync(tenantId.Value)
            : await bankAccountRepository.GetPlatformAccountAsync();

        if (account is null)
            return ResponseData<BankAccountFullResponse>.Failure("Bank account not found.", 404);

        return ResponseData<BankAccountFullResponse>.Success(MapFull(account));
    }

    public async Task<ResponseData<BankAccountResponse>> CreateAsync(CreateBankAccountRequest request,
        Guid userId, Guid? tenantId, string ip, string ua)
    {
        User? user = await userRepository.GetByIdAsync(userId);
        if (user is null || !passwordHasher.Verify(request.CurrentPassword, user.PasswordHash))
            return ResponseData<BankAccountResponse>.Failure("Password verification failed.", 401);

        BankAccount? existing = tenantId.HasValue
            ? await bankAccountRepository.GetByTenantIdAsync(tenantId.Value)
            : await bankAccountRepository.GetPlatformAccountAsync();
        if (existing is not null)
            return ResponseData<BankAccountResponse>.Failure("Bank account already exists. Use update.", 409);

        var account = new BankAccount
        {
            TenantId = tenantId,
            AccountTitleEncrypted = fieldEncryption.Encrypt(request.AccountTitle),
            AccountNumberEncrypted = fieldEncryption.Encrypt(request.AccountNumber),
            BankNameEncrypted = fieldEncryption.Encrypt(request.BankName),
            BranchCodeEncrypted = fieldEncryption.Encrypt(request.BranchCode),
            IbanEncrypted = fieldEncryption.Encrypt(request.Iban),
            IsActive = true
        };

        account.AddDomainEvent(new BankAccountChangedEvent(account.Id, tenantId, user.Email, "Created"));
        await bankAccountRepository.AddAsync(account);
        await unitOfWork.SaveChangesAsync();

        await auditLogService.LogAsync(userId, tenantId, "BankAccountCreated", "BankAccount", account.Id,
            null, new { AccountNumber = fieldEncryption.MaskAccountNumber(request.AccountNumber) }, ip, ua);

        // Best-effort: the bank account row already committed above (SaveChangesAsync). A
        // notification-send failure must never turn an already-successful write into a 500.
        try
        {
            await emailService.SendBankAccountChangedAsync(user.Email);
        }
#pragma warning disable CA1031
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Failed to send BankAccountChanged (Created) email to {Email} for bank account {BankAccountId}.",
                user.Email, account.Id);
        }
#pragma warning restore CA1031

        return ResponseData<BankAccountResponse>.Success(MapMasked(account), "Bank account created.", 201);
    }

    public async Task<ResponseData<BankAccountResponse>> UpdateAsync(UpdateBankAccountRequest request,
        Guid userId, Guid? tenantId, string ip, string ua)
    {
        User? user = await userRepository.GetByIdAsync(userId);
        if (user is null || !passwordHasher.Verify(request.CurrentPassword, user.PasswordHash))
            return ResponseData<BankAccountResponse>.Failure("Password verification failed.", 401);

        BankAccount? account = tenantId.HasValue
            ? await bankAccountRepository.GetByTenantIdAsync(tenantId.Value)
            : await bankAccountRepository.GetPlatformAccountAsync();

        if (account is null)
            return ResponseData<BankAccountResponse>.Failure("Bank account not found.", 404);

        var oldMasked = new
        {
            AccountNumber = fieldEncryption.MaskAccountNumber(
            fieldEncryption.Decrypt(account.AccountNumberEncrypted))
        };

        account.AccountTitleEncrypted = fieldEncryption.Encrypt(request.AccountTitle);
        account.AccountNumberEncrypted = fieldEncryption.Encrypt(request.AccountNumber);
        account.BankNameEncrypted = fieldEncryption.Encrypt(request.BankName);
        account.BranchCodeEncrypted = fieldEncryption.Encrypt(request.BranchCode);
        account.IbanEncrypted = fieldEncryption.Encrypt(request.Iban);

        account.AddDomainEvent(new BankAccountChangedEvent(account.Id, tenantId, user.Email, "Updated"));
        await bankAccountRepository.UpdateAsync(account);
        await unitOfWork.SaveChangesAsync();

        await auditLogService.LogAsync(userId, tenantId, "BankAccountUpdated", "BankAccount", account.Id,
            oldMasked, new { AccountNumber = fieldEncryption.MaskAccountNumber(request.AccountNumber) }, ip, ua);

        // Best-effort: the bank account row already committed above (SaveChangesAsync). A
        // notification-send failure must never turn an already-successful write into a 500.
        try
        {
            await emailService.SendBankAccountChangedAsync(user.Email);
        }
#pragma warning disable CA1031
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Failed to send BankAccountChanged (Updated) email to {Email} for bank account {BankAccountId}.",
                user.Email, account.Id);
        }
#pragma warning restore CA1031

        return ResponseData<BankAccountResponse>.Success(MapMasked(account));
    }

    private BankAccountResponse MapMasked(BankAccount a)
    {
        var plainIban = fieldEncryption.Decrypt(a.IbanEncrypted);
        var maskedIban = string.IsNullOrEmpty(plainIban) || plainIban.Length <= 4
            ? $"****{plainIban}"
            : $"****{plainIban[^4..]}";

        return new()
        {
            Id = a.Id,
            TenantId = a.TenantId,
            IsActive = a.IsActive,
            AccountTitle = fieldEncryption.Decrypt(a.AccountTitleEncrypted),
            AccountNumber = fieldEncryption.MaskAccountNumber(fieldEncryption.Decrypt(a.AccountNumberEncrypted)),
            BankName = fieldEncryption.Decrypt(a.BankNameEncrypted),
            BranchCode = fieldEncryption.Decrypt(a.BranchCodeEncrypted),
            Iban = maskedIban
        };
    }

    // NOTE: Do NOT log the return value of this mapper — AccountNumber is plaintext.
    private BankAccountFullResponse MapFull(BankAccount a) => new()
    {
        Id = a.Id,
        TenantId = a.TenantId,
        IsActive = a.IsActive,
        AccountTitle = fieldEncryption.Decrypt(a.AccountTitleEncrypted),
        AccountNumber = fieldEncryption.Decrypt(a.AccountNumberEncrypted),
        BankName = fieldEncryption.Decrypt(a.BankNameEncrypted),
        BranchCode = fieldEncryption.Decrypt(a.BranchCodeEncrypted),
        Iban = fieldEncryption.Decrypt(a.IbanEncrypted)
    };
}
