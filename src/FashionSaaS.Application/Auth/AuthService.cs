using System.Text.RegularExpressions;
using FashionSaaS.Application.Auth.DTOs;
using FashionSaaS.Application.Common;
using FashionSaaS.Application.Interfaces;
using FashionSaaS.Domain.Entities;
using FashionSaaS.Domain.Enums;

namespace FashionSaaS.Application.Auth;

public class AuthService(
    IUserRepository userRepository,
    IRefreshTokenRepository refreshTokenRepository,
    ILoginAttemptRepository loginAttemptRepository,
    IPasswordHasher passwordHasher,
    IJwtService jwtService,
    IUnitOfWork unitOfWork,
    IAuditLogService auditLogService,
    IEmailService emailService)
{
    private static readonly Regex PasswordPolicy =
        new(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[!@#$%^&*]).{8,}$", RegexOptions.Compiled);

    public async Task<ResponseData<LoginResponse>> LoginAsync(
        LoginRequest request, string ipAddress, string userAgent)
    {
        await RecordAttemptAsync(request.Email, false, "Login initiated", ipAddress, userAgent);

        var user = await userRepository.GetByEmailAsync(request.Email);
        if (user is null || !passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            await RecordAttemptAsync(request.Email, false, "Invalid credentials", ipAddress, userAgent);
            return ResponseData<LoginResponse>.Failure("Invalid email or password.", 401);
        }

        if (!user.IsActive)
            return ResponseData<LoginResponse>.Failure("Account is disabled.", 403);

        // Check lockout: 5 failures in 15 min → lock 15 min
        var recentFailures = await loginAttemptRepository.GetRecentFailureCountAsync(request.Email, 15);
        if (recentFailures >= 5)
        {
            await emailService.SendAccountLockedAsync(user.Email);
            return ResponseData<LoginResponse>.Failure("Account temporarily locked. Try again in 15 minutes.", 423);
        }

        // Load with roles + Tenant navigation (needed for tenantSlug and role check)
        var userWithRoles = await userRepository.GetByIdWithRolesAsync(user.Id);
        var roles = userWithRoles?.UserRoles.Select(ur => ur.Role.Name.ToString()).ToList()
                    ?? new List<string>();

        var isSuperAdmin = roles.Contains(RoleType.SuperAdmin.ToString());
        await RecordAttemptAsync(request.Email, true, null, ipAddress, userAgent);

        if (isSuperAdmin)
        {
            // Step 1 of 2: password verified; TOTP required next.
            // No JWT issued until MFA is complete (security requirement: mfa_verified=true).
            return ResponseData<LoginResponse>.Success(new LoginResponse
            {
                MfaRequired = true,
                MfaUserId = user.Id
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
        var user = await userRepository.GetByIdWithRolesAsync(request.UserId);
        if (user is null)
            return ResponseData<LoginResponse>.Failure("User not found.", 404);

        if (user.MfaSettings is null || !user.MfaSettings.IsEnrolled)
            return ResponseData<LoginResponse>.Failure("MFA not configured.", 400);

        var secret = user.MfaSettings.TotpSecretEncrypted!;
        if (!totpService.Verify(secret, request.Code))
            return ResponseData<LoginResponse>.Failure("Invalid TOTP code.", 401);

        var roles = user.UserRoles.Select(ur => ur.Role.Name.ToString()).ToList();

        // tenantSlug from navigation property (null for SuperAdmin who has no TenantId)
        var tenantSlug = user.Tenant?.Slug;

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
        var (accessToken, newRawToken) = await IssueTokensAsync(
            userWithRoles, roles, tenantSlug, mfaVerified: isSuperAdmin);

        await unitOfWork.SaveChangesAsync();
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
    // Private helpers
    // -------------------------------------------------------------------------

    private async Task<(string accessToken, string rawRefreshToken)> IssueTokensAsync(
        User user, IList<string> roles, string? tenantSlug, bool mfaVerified)
    {
        // Pass tenantSlug so the JWT carries the tenant_slug claim (security requirement)
        var accessToken = jwtService.GenerateAccessToken(user, roles, tenantSlug, mfaVerified);
        var rawRefreshToken = jwtService.GenerateRefreshToken();
        var hashedToken = passwordHasher.Hash(rawRefreshToken);

        // Revoke any existing tokens before issuing new one (rotation)
        await refreshTokenRepository.RevokeAllByUserIdAsync(user.Id);

        var isSuperAdmin = roles.Contains(RoleType.SuperAdmin.ToString());
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
