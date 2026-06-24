using FashionSaaS.Application.BankAccounts.DTOs;
using FashionSaaS.Application.Common;
using FashionSaaS.Application.Interfaces;
using FashionSaaS.Domain.Entities;
using FashionSaaS.Domain.Events;

namespace FashionSaaS.Application.BankAccounts;

public class BankAccountService(
    IBankAccountRepository bankAccountRepository,
    IUserRepository userRepository,
    IFieldEncryptionService fieldEncryption,
    IPasswordHasher passwordHasher,
    IAuditLogService auditLogService,
    IEmailService emailService,
    IUnitOfWork unitOfWork)
{
    public async Task<ResponseData<BankAccountResponse>> GetAsync(Guid? tenantId)
    {
        var account = tenantId.HasValue
            ? await bankAccountRepository.GetByTenantIdAsync(tenantId.Value)
            : await bankAccountRepository.GetPlatformAccountAsync();

        if (account is null)
            return ResponseData<BankAccountResponse>.Failure("Bank account not found.", 404);

        return ResponseData<BankAccountResponse>.Success(MapMasked(account));
    }

    public async Task<ResponseData<BankAccountResponse>> CreateAsync(CreateBankAccountRequest request,
        Guid userId, Guid? tenantId, string ip, string ua)
    {
        var user = await userRepository.GetByIdAsync(userId);
        if (user is null || !passwordHasher.Verify(request.CurrentPassword, user.PasswordHash))
            return ResponseData<BankAccountResponse>.Failure("Password verification failed.", 401);

        var existing = tenantId.HasValue
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
        await emailService.SendBankAccountChangedAsync(user.Email);

        return ResponseData<BankAccountResponse>.Success(MapMasked(account), "Bank account created.", 201);
    }

    public async Task<ResponseData<BankAccountResponse>> UpdateAsync(UpdateBankAccountRequest request,
        Guid userId, Guid? tenantId, string ip, string ua)
    {
        var user = await userRepository.GetByIdAsync(userId);
        if (user is null || !passwordHasher.Verify(request.CurrentPassword, user.PasswordHash))
            return ResponseData<BankAccountResponse>.Failure("Password verification failed.", 401);

        var account = tenantId.HasValue
            ? await bankAccountRepository.GetByTenantIdAsync(tenantId.Value)
            : await bankAccountRepository.GetPlatformAccountAsync();

        if (account is null)
            return ResponseData<BankAccountResponse>.Failure("Bank account not found.", 404);

        var oldMasked = new { AccountNumber = fieldEncryption.MaskAccountNumber(
            fieldEncryption.Decrypt(account.AccountNumberEncrypted)) };

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
        await emailService.SendBankAccountChangedAsync(user.Email);

        return ResponseData<BankAccountResponse>.Success(MapMasked(account));
    }

    private BankAccountResponse MapMasked(BankAccount a) => new()
    {
        Id = a.Id, TenantId = a.TenantId, IsActive = a.IsActive,
        AccountTitle = fieldEncryption.Decrypt(a.AccountTitleEncrypted),
        AccountNumber = fieldEncryption.MaskAccountNumber(fieldEncryption.Decrypt(a.AccountNumberEncrypted)),
        BankName = fieldEncryption.Decrypt(a.BankNameEncrypted),
        BranchCode = fieldEncryption.Decrypt(a.BranchCodeEncrypted),
        Iban = fieldEncryption.Decrypt(a.IbanEncrypted)
    };
}
