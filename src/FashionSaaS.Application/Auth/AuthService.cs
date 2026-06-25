using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using FashionSaaS.Application.Auth.DTOs;
using FashionSaaS.Application.Common;
using FashionSaaS.Application.Interfaces;
using FashionSaaS.Domain.Entities;
using FashionSaaS.Domain.Enums;
using FashionSaaS.Domain.Events;

namespace FashionSaaS.Application.Auth;

public class AuthService(
    IUserRepository userRepository,
    IRefreshTokenRepository refreshTokenRepository,
    ILoginAttemptRepository loginAttemptRepository,
    IPasswordHasher passwordHasher,
    IJwtService jwtService,
    IUnitOfWork unitOfWork,
    IAuditLogService auditLogService,
    IEmailService emailService,
    IFieldEncryptionService fieldEncryption,
    ISuperAdminIpGuardService ipGuardService)
{
    private static readonly Regex PasswordPolicy =
        new(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^a-zA-Z\d]).{8,}$", RegexOptions.Compiled);

    public async Task<ResponseData<LoginResponse>> LoginAsync(
        LoginRequest request, string ipAddress, string userAgent)
    {
        var user = await userRepository.GetByEmailAsync(request.Email);

        // B3 — Persistent lock check: a SuperAdmin unlock is required to clear this.
        if (user is not null && user.IsLocked)
            return ResponseData<LoginResponse>.Failure("Account locked. Contact an administrator.", 423);

        // Check rate-based lockout FIRST — before verifying the password — so a locked account
        // cannot be probed for valid credentials and no extra failure row is recorded.
        var recentFailures = await loginAttemptRepository.GetRecentFailureCountAsync(request.Email, 15);

        // B3 — 10-failure tier: set persistent lock and persist before returning.
        if (recentFailures >= 10 && user is not null && !user.IsLocked)
        {
            user.IsLocked = true;
            await userRepository.UpdateAsync(user);
            await unitOfWork.SaveChangesAsync();
            return ResponseData<LoginResponse>.Failure("Account locked. Contact an administrator.", 423);
        }

        if (recentFailures >= 5)
        {
            if (user is not null)
                await emailService.SendAccountLockedAsync(user.Email);
            return ResponseData<LoginResponse>.Failure("Account temporarily locked. Try again in 15 minutes.", 423);
        }

        // Now verify credentials; record a failure row only on actual bad credentials.
        if (user is null || !passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            await RecordAttemptAsync(request.Email, false, "Invalid credentials", ipAddress, userAgent);
            return ResponseData<LoginResponse>.Failure("Invalid email or password.", 401);
        }

        if (!user.IsActive)
            return ResponseData<LoginResponse>.Failure("Account is disabled.", 403);

        // Load with roles + Tenant navigation (needed for tenantSlug and role check)
        var userWithRoles = await userRepository.GetByIdWithRolesAsync(user.Id);
        var roles = userWithRoles?.UserRoles.Select(ur => ur.Role.Name.ToString()).ToList()
                    ?? new List<string>();

        var isSuperAdmin = roles.Contains(RoleType.SuperAdmin.ToString());
        await RecordAttemptAsync(request.Email, true, null, ipAddress, userAgent);

        if (isSuperAdmin)
        {
            // B1 — Step 1 of 2: password verified; issue a short-lived MFA-challenge token.
            // No JWT issued until MFA is complete (security requirement: mfa_verified=true).
            // The raw user GUID is no longer exposed in the response.
            var mfaToken = jwtService.GenerateMfaChallengeToken(user.Id);
            return ResponseData<LoginResponse>.Success(new LoginResponse
            {
                MfaRequired = true,
                MfaToken = mfaToken
            }, "MFA verification required.");
        }

        var tenantSlug = userWithRoles?.Tenant?.Slug;
        var (accessToken, rawRefreshToken) = await IssueTokensAsync(userWithRoles ?? user, roles, tenantSlug, mfaVerified: false);

        return ResponseData<LoginResponse>.Success(new LoginResponse
        {
            AccessToken = accessToken,
            RefreshToken = rawRefreshToken,
            MfaRequired = false
        });
    }

    public async Task<ResponseData<LoginResponse>> LoginMfaAsync(
        LoginMfaRequest request, ITotpService totpService, string ipAddress, string userAgent)
    {
        // B1 — Validate the MFA-challenge token issued by the password step.
        // Returns null if the token is invalid, expired, or has the wrong purpose.
        var userId = jwtService.ValidateMfaChallengeToken(request.MfaToken);
        if (userId is null)
            return ResponseData<LoginResponse>.Failure("Invalid or expired MFA challenge.", 401);

        var user = await userRepository.GetByIdWithRolesAsync(userId.Value);
        if (user is null)
            return ResponseData<LoginResponse>.Failure("User not found.", 404);

        if (!user.IsActive)
            return ResponseData<LoginResponse>.Failure("Account is disabled.", 403);

        // B3 — Persistent lock check also applies to MFA step.
        if (user.IsLocked)
            return ResponseData<LoginResponse>.Failure("Account locked. Contact an administrator.", 423);

        // B2 — Pre-check rate-based lockout before verifying TOTP so brute-forcing the
        // TOTP code is subject to the same 5-fail/15-min window as the password step.
        var recentFailures = await loginAttemptRepository.GetRecentFailureCountAsync(user.Email, 15);
        if (recentFailures >= 5)
            return ResponseData<LoginResponse>.Failure("Account temporarily locked. Try again in 15 minutes.", 423);

        if (user.MfaSettings is null || !user.MfaSettings.IsEnrolled)
            return ResponseData<LoginResponse>.Failure("MFA not configured.", 400);

        var secret = fieldEncryption.Decrypt(user.MfaSettings.TotpSecretEncrypted!);

        // B2 — Wrong TOTP: record a failure attempt so it counts toward the lockout window.
        if (!totpService.Verify(secret, request.Code))
        {
            await RecordAttemptAsync(user.Email, false, "Invalid MFA code", ipAddress, userAgent);
            return ResponseData<LoginResponse>.Failure("Invalid TOTP code.", 401);
        }

        var roles = user.UserRoles.Select(ur => ur.Role.Name.ToString()).ToList();

        // tenantSlug from navigation property (null for SuperAdmin who has no TenantId)
        var tenantSlug = user.Tenant?.Slug;

        // Anomaly detection — raise domain event before SaveChangesAsync so UnitOfWork dispatches it.
        // Guard: only SuperAdmins should trigger this alert; non-SuperAdmin MFA enrollees must not.
        var isSuperAdmin = roles.Contains(nameof(RoleType.SuperAdmin));
        if (isSuperAdmin && await ipGuardService.IsNewIpAsync(user.Email, ipAddress))
        {
            var newIpEvent = new SuperAdminLoginFromNewIpEvent(user.Id, user.Email, ipAddress, DateTime.UtcNow);
            user.AddDomainEvent(newIpEvent);
        }

        // mfaVerified=true is mandatory for SuperAdmin JWT (security requirement)
        var (accessToken, rawRefreshToken) = await IssueTokensAsync(user, roles, tenantSlug, mfaVerified: true);

        await auditLogService.LogAsync(
            user.Id, user.TenantId, "SuperAdminLogin", "User", user.Id,
            null, new { Email = user.Email, IpAddress = ipAddress }, ipAddress, userAgent);

        return ResponseData<LoginResponse>.Success(new LoginResponse
        {
            AccessToken = accessToken,
            RefreshToken = rawRefreshToken
        });
    }

    /// <summary>
    /// Rotate refresh token by userId + raw token lookup.
    /// The raw token is sent from the controller's HttpOnly cookie.
    /// </summary>
    public async Task<ResponseData<LoginResponse>> RefreshTokenByUserIdAsync(
        Guid userId, string rawToken, string ipAddress, string userAgent)
    {
        var existing = await refreshTokenRepository.GetActiveByUserIdAsync(userId);
        if (existing is null || !passwordHasher.Verify(rawToken, existing.TokenHash))
            return ResponseData<LoginResponse>.Failure("Invalid or expired refresh token.", 401);

        existing.IsRevoked = true;
        existing.RevokedAt = DateTime.UtcNow;
        await refreshTokenRepository.UpdateAsync(existing);

        var userWithRoles = await userRepository.GetByIdWithRolesAsync(userId);
        if (userWithRoles is null || !userWithRoles.IsActive)
            return ResponseData<LoginResponse>.Failure("User not found or disabled.", 401);

        var roles = userWithRoles.UserRoles.Select(ur => ur.Role.Name.ToString()).ToList();
        var isSuperAdmin = roles.Contains(RoleType.SuperAdmin.ToString());
        var tenantSlug = userWithRoles.Tenant?.Slug;

        // SuperAdmin refresh preserves mfa_verified=true (they already completed TOTP)
        // IssueTokensAsync stages the new token and calls SaveChangesAsync once, covering
        // both the revocation (staged above via UpdateAsync) and the new token — single commit.
        var (accessToken, newRawToken) = await IssueTokensAsync(
            userWithRoles, roles, tenantSlug, mfaVerified: isSuperAdmin);

        return ResponseData<LoginResponse>.Success(new LoginResponse
        {
            AccessToken = accessToken,
            RefreshToken = newRawToken
        });
    }

    public async Task<ResponseData<bool>> LogoutAsync(Guid userId)
    {
        await refreshTokenRepository.RevokeAllByUserIdAsync(userId);
        await unitOfWork.SaveChangesAsync();
        return ResponseData<bool>.Success(true, "Logged out successfully.");
    }

    // -------------------------------------------------------------------------
    // Password management
    // -------------------------------------------------------------------------

    public async Task<ResponseData<bool>> ForgotPasswordAsync(string email, string baseUrl,
        IPasswordResetTokenRepository resetTokenRepo)
    {
        var user = await userRepository.GetByEmailAsync(email);
        if (user is null)
            return ResponseData<bool>.Success(true, "If this email is registered, a reset link has been sent.");

        await resetTokenRepo.InvalidateAllByUserIdAsync(user.Id);

        var rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));

        var resetToken = new PasswordResetToken
        {
            UserId = user.Id,
            TokenHash = tokenHash,
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        };
        await resetTokenRepo.AddAsync(resetToken);
        await unitOfWork.SaveChangesAsync();

        var resetLink = $"{baseUrl}/reset-password?token={Uri.EscapeDataString(rawToken)}";
        await emailService.SendPasswordResetAsync(user.Email, resetLink);

        return ResponseData<bool>.Success(true, "If this email is registered, a reset link has been sent.");
    }

    public async Task<ResponseData<bool>> ResetPasswordAsync(ResetPasswordRequest request,
        IPasswordResetTokenRepository resetTokenRepo, IPasswordHistoryRepository historyRepo)
    {
        if (!IsPasswordCompliant(request.NewPassword))
            return ResponseData<bool>.Failure("Password does not meet complexity requirements.", 400);

        var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.Token)));
        var resetToken = await resetTokenRepo.GetValidByHashAsync(tokenHash);
        if (resetToken is null)
            return ResponseData<bool>.Failure("Invalid or expired reset token.", 400);

        var user = await userRepository.GetByIdAsync(resetToken.UserId);
        if (user is null)
            return ResponseData<bool>.Failure("User not found.", 404);

        // Check last 5 passwords
        var history = await historyRepo.GetLastNAsync(user.Id, 5);
        if (history.Any(h => passwordHasher.Verify(request.NewPassword, h.PasswordHash)))
            return ResponseData<bool>.Failure("Cannot reuse one of your last 5 passwords.", 400);

        user.PasswordHash = passwordHasher.Hash(request.NewPassword);
        resetToken.IsUsed = true;

        await userRepository.UpdateAsync(user);
        await resetTokenRepo.UpdateAsync(resetToken);

        var newHistory = new PasswordHistory { UserId = user.Id, PasswordHash = user.PasswordHash };
        await historyRepo.AddAsync(newHistory);

        // Revoke all refresh tokens on password change
        await refreshTokenRepository.RevokeAllByUserIdAsync(user.Id);
        await unitOfWork.SaveChangesAsync();

        return ResponseData<bool>.Success(true, "Password reset successfully.");
    }

    public async Task<ResponseData<bool>> ChangePasswordAsync(Guid userId, ChangePasswordRequest request,
        IPasswordHistoryRepository historyRepo)
    {
        if (!IsPasswordCompliant(request.NewPassword))
            return ResponseData<bool>.Failure("Password does not meet complexity requirements.", 400);

        var user = await userRepository.GetByIdAsync(userId);
        if (user is null)
            return ResponseData<bool>.Failure("User not found.", 404);

        if (!passwordHasher.Verify(request.CurrentPassword, user.PasswordHash))
            return ResponseData<bool>.Failure("Current password is incorrect.", 401);

        var history = await historyRepo.GetLastNAsync(userId, 5);
        if (history.Any(h => passwordHasher.Verify(request.NewPassword, h.PasswordHash)))
            return ResponseData<bool>.Failure("Cannot reuse one of your last 5 passwords.", 400);

        user.PasswordHash = passwordHasher.Hash(request.NewPassword);
        await userRepository.UpdateAsync(user);

        var newHistory = new PasswordHistory { UserId = userId, PasswordHash = user.PasswordHash };
        await historyRepo.AddAsync(newHistory);

        await refreshTokenRepository.RevokeAllByUserIdAsync(userId);
        await unitOfWork.SaveChangesAsync();

        return ResponseData<bool>.Success(true, "Password changed. All sessions revoked.");
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private async Task<(string accessToken, string rawRefreshToken)> IssueTokensAsync(
        User user, IEnumerable<string> roles, string? tenantSlug, bool mfaVerified)
    {
        // Materialize once — roles is used for both JWT generation and SuperAdmin check below.
        var roleList = roles as IReadOnlyList<string> ?? roles.ToList();

        // Pass tenantSlug so the JWT carries the tenant_slug claim (security requirement)
        var accessToken = jwtService.GenerateAccessToken(user, roleList, tenantSlug, mfaVerified);
        var rawRefreshToken = jwtService.GenerateRefreshToken();
        var hashedToken = passwordHasher.Hash(rawRefreshToken);

        // Revoke any existing tokens before issuing new one (rotation)
        await refreshTokenRepository.RevokeAllByUserIdAsync(user.Id);

        var isSuperAdmin = roleList.Contains(RoleType.SuperAdmin.ToString());
        // SuperAdmin gets 24-hour refresh window; all others get 7 days
        var expiry = isSuperAdmin ? DateTime.UtcNow.AddHours(24) : DateTime.UtcNow.AddDays(7);

        var refreshToken = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = hashedToken,
            ExpiresAt = expiry
        };
        await refreshTokenRepository.AddAsync(refreshToken);
        await unitOfWork.SaveChangesAsync();

        return (accessToken, rawRefreshToken);
    }

    private async Task RecordAttemptAsync(
        string email, bool success, string? reason, string ipAddress, string userAgent)
    {
        var attempt = new UserLoginAttempt
        {
            Email = email,
            IsSuccess = success,
            FailureReason = reason,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await loginAttemptRepository.AddAsync(attempt);
        await unitOfWork.SaveChangesAsync();
    }

    public static bool IsPasswordCompliant(string password)
        => PasswordPolicy.IsMatch(password);
}
