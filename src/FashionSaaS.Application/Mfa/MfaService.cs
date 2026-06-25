using FashionSaaS.Application.Common;
using FashionSaaS.Application.Configuration;
using FashionSaaS.Application.Interfaces;
using FashionSaaS.Application.Mfa.DTOs;
using FashionSaaS.Domain.Entities;
using Microsoft.Extensions.Options;

namespace FashionSaaS.Application.Mfa;

public class MfaService(
    IUserRepository userRepository,
    ITotpService totpService,
    IPasswordHasher passwordHasher,
    IFieldEncryptionService fieldEncryption,
    IUnitOfWork unitOfWork,
    IOptions<JwtSettings> jwtOptions)
{
    public async Task<ResponseData<MfaSetupResponse>> SetupAsync(Guid userId)
    {
        var user = await userRepository.GetByIdWithRolesAsync(userId);
        if (user is null)
            return ResponseData<MfaSetupResponse>.Failure("User not found.", 404);

        var issuer = jwtOptions.Value.Issuer is { Length: > 0 } iss ? iss : "FashionSaaS";
        var (secret, qrUrl) = totpService.GenerateSetup(user.Email, issuer);

        if (user.MfaSettings is null)
        {
            var mfaSettings = new UserMfaSettings
            {
                UserId = userId,
                IsEnabled = false,
                TotpSecretEncrypted = fieldEncryption.Encrypt(secret),
                IsEnrolled = false
            };
            // Attach to context — normally through a dedicated repo
            user.MfaSettings = mfaSettings;
        }
        else
        {
            user.MfaSettings.TotpSecretEncrypted = fieldEncryption.Encrypt(secret);
            user.MfaSettings.IsEnrolled = false;
        }

        await userRepository.UpdateAsync(user);
        await unitOfWork.SaveChangesAsync();

        return ResponseData<MfaSetupResponse>.Success(new MfaSetupResponse
        {
            QrCodeUrl = qrUrl,
            SecretBase32 = secret
        });
    }

    public async Task<ResponseData<IReadOnlyList<string>>> VerifySetupAsync(Guid userId, string totpCode)
    {
        var user = await userRepository.GetByIdWithRolesAsync(userId);
        if (user?.MfaSettings is null)
            return ResponseData<IReadOnlyList<string>>.Failure("MFA setup not started.", 400);

        var secret = fieldEncryption.Decrypt(user.MfaSettings.TotpSecretEncrypted!);
        if (!totpService.Verify(secret, totpCode))
            return ResponseData<IReadOnlyList<string>>.Failure("Invalid TOTP code.", 400);

        var rawCodes = totpService.GenerateBackupCodes();
        user.MfaSettings.IsEnabled = true;
        user.MfaSettings.IsEnrolled = true;
        user.MfaSettings.BackupCodes.Clear();

        foreach (var code in rawCodes)
        {
            user.MfaSettings.BackupCodes.Add(new MfaBackupCode
            {
                UserMfaSettingsId = user.MfaSettings.Id,
                CodeHash = passwordHasher.Hash(code)
            });
        }

        await userRepository.UpdateAsync(user);
        await unitOfWork.SaveChangesAsync();

        return ResponseData<IReadOnlyList<string>>.Success(rawCodes, "MFA enabled. Store backup codes safely.");
    }

    public async Task<ResponseData<IReadOnlyList<string>>> RegenerateBackupCodesAsync(Guid userId)
    {
        var user = await userRepository.GetByIdWithRolesAsync(userId);
        if (user?.MfaSettings is null || !user.MfaSettings.IsEnrolled)
            return ResponseData<IReadOnlyList<string>>.Failure("MFA not enrolled.", 400);

        var rawCodes = totpService.GenerateBackupCodes();
        user.MfaSettings.BackupCodes.Clear();
        foreach (var code in rawCodes)
        {
            user.MfaSettings.BackupCodes.Add(new MfaBackupCode
            {
                UserMfaSettingsId = user.MfaSettings.Id,
                CodeHash = passwordHasher.Hash(code)
            });
        }

        await userRepository.UpdateAsync(user);
        await unitOfWork.SaveChangesAsync();

        return ResponseData<IReadOnlyList<string>>.Success(rawCodes, "Backup codes regenerated.");
    }
}
