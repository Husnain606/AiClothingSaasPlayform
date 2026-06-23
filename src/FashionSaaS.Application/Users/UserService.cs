using System.Security.Cryptography;
using FashionSaaS.Application.Common;
using FashionSaaS.Application.Interfaces;
using FashionSaaS.Application.Users.DTOs;
using FashionSaaS.Domain.Entities;
using FashionSaaS.Domain.Enums;
using FashionSaaS.Domain.Events;

namespace FashionSaaS.Application.Users;

public class UserService(
    IUserRepository userRepository,
    IPasswordHasher passwordHasher,
    IEmailService emailService,
    IAuditLogService auditLogService,
    IUnitOfWork unitOfWork)
{
    public async Task<ResponseData<UserResponse>> CreateAsync(CreateUserRequest request,
        Guid createdByUserId, string ipAddress, string userAgent)
    {
        if (await userRepository.EmailExistsAsync(request.Email))
            return ResponseData<UserResponse>.Failure("Email already registered.", 409);

        var tempPassword = GenerateTempPassword();
        var user = new User
        {
            FirstName = request.FirstName,
            LastName = request.LastName,
            Email = request.Email,
            PasswordHash = passwordHasher.Hash(tempPassword),
            TenantId = request.TenantId,
            IsActive = true
        };

        user.AddDomainEvent(new UserCreatedEvent(user.Id, user.Email, tempPassword, user.TenantId));
        await userRepository.AddAsync(user);
        await unitOfWork.SaveChangesAsync();

        await emailService.SendCredentialsAsync(user.Email, user.Email, tempPassword);
        await auditLogService.LogAsync(createdByUserId, user.TenantId, "UserCreated", "User", user.Id,
            null, new { user.Email, user.TenantId }, ipAddress, userAgent);

        return ResponseData<UserResponse>.Success(MapToResponse(user, new List<string>()), "User created.", 201);
    }

    public async Task<ResponseData<UserResponse>> UpdateAsync(Guid userId, UpdateUserRequest request,
        Guid updatedByUserId, string ipAddress, string userAgent)
    {
        var user = await userRepository.GetByIdAsync(userId);
        if (user is null)
            return ResponseData<UserResponse>.Failure("User not found.", 404);

        var old = new { user.FirstName, user.LastName };
        user.FirstName = request.FirstName;
        user.LastName = request.LastName;

        await userRepository.UpdateAsync(user);
        await unitOfWork.SaveChangesAsync();

        await auditLogService.LogAsync(updatedByUserId, user.TenantId, "UserUpdated", "User", userId,
            old, new { user.FirstName, user.LastName }, ipAddress, userAgent);

        return ResponseData<UserResponse>.Success(MapToResponse(user, new List<string>()), "User updated.");
    }

    public async Task<ResponseData<UserResponse>> GetByIdAsync(Guid id)
    {
        var user = await userRepository.GetByIdWithRolesAsync(id);
        if (user is null)
            return ResponseData<UserResponse>.Failure("User not found.", 404);
        var roles = user.UserRoles.Select(ur => ur.Role.Name.ToString()).ToList();
        return ResponseData<UserResponse>.Success(MapToResponse(user, roles));
    }

    public async Task<ResponseData<PagedResult<UserResponse>>> GetByTenantAsync(Guid tenantId, UserFilterRequest filter)
    {
        var users = await userRepository.GetByTenantAsync(tenantId);
        var filtered = users.AsEnumerable();
        if (!string.IsNullOrEmpty(filter.Search))
            filtered = filtered.Where(u =>
                u.Email.Contains(filter.Search, StringComparison.OrdinalIgnoreCase) ||
                u.FirstName.Contains(filter.Search, StringComparison.OrdinalIgnoreCase));
        if (filter.IsActive.HasValue)
            filtered = filtered.Where(u => u.IsActive == filter.IsActive.Value);

        var list = filtered.ToList();
        var paged = new PagedResult<UserResponse>
        {
            Items = list.Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize)
                .Select(u => MapToResponse(u, new List<string>())).ToList(),
            TotalCount = list.Count,
            Page = filter.Page,
            PageSize = filter.PageSize
        };
        return ResponseData<PagedResult<UserResponse>>.Success(paged);
    }

    public async Task<ResponseData<bool>> AssignRoleAsync(Guid userId, RoleType role,
        Guid adminId, string ipAddress, string userAgent)
    {
        var user = await userRepository.GetByIdWithRolesAsync(userId);
        if (user is null)
            return ResponseData<bool>.Failure("User not found.", 404);

        // Replace existing roles by clearing and adding the new one
        user.UserRoles.Clear();
        user.UserRoles.Add(new UserRole
        {
            UserId = userId,
            RoleId = Guid.Empty, // resolved by infrastructure/EF via Role navigation
            Role = new Role { Name = role, Scope = RoleScope.Tenant }
        });

        await userRepository.UpdateAsync(user);
        await unitOfWork.SaveChangesAsync();

        await auditLogService.LogAsync(adminId, user.TenantId, "UserRoleAssigned", "User", userId,
            null, new { Role = role.ToString() }, ipAddress, userAgent);

        return ResponseData<bool>.Success(true, "Role assigned.");
    }

    public async Task<ResponseData<bool>> DeactivateAsync(Guid userId, Guid adminId,
        string ipAddress, string userAgent)
    {
        var user = await userRepository.GetByIdAsync(userId);
        if (user is null)
            return ResponseData<bool>.Failure("User not found.", 404);

        user.IsActive = false;
        await userRepository.UpdateAsync(user);
        await unitOfWork.SaveChangesAsync();

        await auditLogService.LogAsync(adminId, user.TenantId, "UserDeactivated", "User", userId,
            new { WasActive = true }, new { IsActive = false }, ipAddress, userAgent);

        return ResponseData<bool>.Success(true, "User deactivated.");
    }

    public async Task<ResponseData<bool>> UnlockAsync(Guid userId, Guid adminId,
        string ipAddress, string userAgent)
    {
        var user = await userRepository.GetByIdAsync(userId);
        if (user is null)
            return ResponseData<bool>.Failure("User not found.", 404);

        user.IsActive = true;
        await userRepository.UpdateAsync(user);
        await unitOfWork.SaveChangesAsync();

        await auditLogService.LogAsync(adminId, user.TenantId, "UserUnlocked", "User", userId,
            null, new { UserId = userId }, ipAddress, userAgent);

        return ResponseData<bool>.Success(true, "User account unlocked.");
    }

    public async Task<ResponseData<bool>> DeleteAsync(Guid userId, Guid adminId,
        string ipAddress, string userAgent)
    {
        var user = await userRepository.GetByIdAsync(userId);
        if (user is null)
            return ResponseData<bool>.Failure("User not found.", 404);

        await userRepository.DeleteAsync(user);
        await unitOfWork.SaveChangesAsync();

        await auditLogService.LogAsync(adminId, user.TenantId, "UserDeleted", "User", userId,
            new { user.Email }, null, ipAddress, userAgent);

        return ResponseData<bool>.Success(true, "User deleted.");
    }

    private static string GenerateTempPassword()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789!@#$";
        var bytes = RandomNumberGenerator.GetBytes(12);
        return new string(bytes.Select(b => chars[b % chars.Length]).ToArray());
    }

    private static UserResponse MapToResponse(User u, IList<string> roles) => new()
    {
        Id = u.Id, FirstName = u.FirstName, LastName = u.LastName,
        Email = u.Email, TenantId = u.TenantId, IsActive = u.IsActive,
        Roles = roles, CreatedAt = u.CreatedAt
    };
}
