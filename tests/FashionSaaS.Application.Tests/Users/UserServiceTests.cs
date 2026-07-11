using FashionSaaS.Application.Common;
using FashionSaaS.Application.Interfaces;
using FashionSaaS.Application.Users;
using FashionSaaS.Application.Users.DTOs;
using FashionSaaS.Domain.Entities;
using FashionSaaS.Domain.Enums;
using FluentAssertions;
using Moq;

namespace FashionSaaS.Application.Tests.Users;

public class UserServiceTests
{
    private readonly Mock<IUserRepository> _userRepo = new();
    private readonly Mock<IPasswordHasher> _hasher = new();
    private readonly Mock<IEmailService> _email = new();
    private readonly Mock<IAuditLogService> _audit = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IRoleRepository> _roleRepo = new();
    private readonly Mock<ILoginAttemptRepository> _loginAttemptRepo = new();

    private UserService CreateService() => new(
        _userRepo.Object,
        _hasher.Object,
        _email.Object,
        _audit.Object,
        _uow.Object,
        _roleRepo.Object,
        _loginAttemptRepo.Object);

    // -------------------------------------------------------------------------
    // CreateAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CreateAsync_NewEmail_CreatesUser()
    {
        var seededRole = new Role { Id = Guid.Parse("10000000-0000-0000-0000-000000000003"), Name = RoleType.StoreManager, Scope = RoleScope.Tenant };
        _userRepo.Setup(r => r.EmailExistsAsync("new@brand.com")).ReturnsAsync(false);
        _hasher.Setup(h => h.Hash(It.IsAny<string>())).Returns("hashed");
        _roleRepo.Setup(r => r.GetByRoleTypeAsync(RoleType.StoreManager, default)).ReturnsAsync(seededRole);

        ResponseData<UserResponse> result = await CreateService().CreateAsync(
            new CreateUserRequest { Email = "new@brand.com", FirstName = "Ali", LastName = "Khan", Role = RoleType.StoreManager },
            Guid.NewGuid(), "127.0.0.1", "Mozilla");

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(201);
    }

    [Fact]
    public async Task CreateAsync_AssignsRoleWithRealSeededRoleId()
    {
        var seededRoleId = Guid.Parse("10000000-0000-0000-0000-000000000003");
        var seededRole = new Role { Id = seededRoleId, Name = RoleType.StoreManager, Scope = RoleScope.Tenant };

        _userRepo.Setup(r => r.EmailExistsAsync(It.IsAny<string>())).ReturnsAsync(false);
        _hasher.Setup(h => h.Hash(It.IsAny<string>())).Returns("hashed");
        _roleRepo.Setup(r => r.GetByRoleTypeAsync(RoleType.StoreManager, default)).ReturnsAsync(seededRole);

        User? capturedUser = null;
        _userRepo.Setup(r => r.AddAsync(It.IsAny<User>()))
            .Callback<User>(u => capturedUser = u)
            .Returns(Task.CompletedTask);

        await CreateService().CreateAsync(
            new CreateUserRequest { Email = "role@brand.com", FirstName = "Ali", LastName = "Khan", Role = RoleType.StoreManager },
            Guid.NewGuid(), "127.0.0.1", "Mozilla");

        capturedUser.Should().NotBeNull();
        capturedUser!.UserRoles.Should().HaveCount(1);
        capturedUser.UserRoles.Single().RoleId.Should().Be(seededRoleId);
        capturedUser.UserRoles.Single().RoleId.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task CreateAsync_RoleNotFound_Returns404()
    {
        _userRepo.Setup(r => r.EmailExistsAsync(It.IsAny<string>())).ReturnsAsync(false);
        _roleRepo.Setup(r => r.GetByRoleTypeAsync(It.IsAny<RoleType>(), default)).ReturnsAsync((Role?)null);

        ResponseData<UserResponse> result = await CreateService().CreateAsync(
            new CreateUserRequest { Email = "x@brand.com", FirstName = "A", LastName = "B", Role = RoleType.StoreManager },
            Guid.NewGuid(), "127.0.0.1", "Mozilla");

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task CreateAsync_DuplicateEmail_ReturnsConflict()
    {
        _userRepo.Setup(r => r.EmailExistsAsync("dup@brand.com")).ReturnsAsync(true);

        ResponseData<UserResponse> result = await CreateService().CreateAsync(
            new CreateUserRequest { Email = "dup@brand.com", FirstName = "A", LastName = "B", Role = RoleType.StoreManager },
            Guid.NewGuid(), "127.0.0.1", "Mozilla");

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task CreateAsync_NeverReturnsPasswordHash()
    {
        var seededRole = new Role { Id = Guid.NewGuid(), Name = RoleType.StoreManager, Scope = RoleScope.Tenant };
        _userRepo.Setup(r => r.EmailExistsAsync("safe@brand.com")).ReturnsAsync(false);
        _hasher.Setup(h => h.Hash(It.IsAny<string>())).Returns("secret-hash");
        _roleRepo.Setup(r => r.GetByRoleTypeAsync(RoleType.StoreManager, default)).ReturnsAsync(seededRole);

        ResponseData<UserResponse> result = await CreateService().CreateAsync(
            new CreateUserRequest { Email = "safe@brand.com", FirstName = "Test", LastName = "User", Role = RoleType.StoreManager },
            Guid.NewGuid(), "127.0.0.1", "Mozilla");

        result.IsSuccess.Should().BeTrue();
        result.Data.Should().NotBeNull();
        IEnumerable<string> props = result.Data!.GetType().GetProperties().Select(p => p.Name);
        props.Should().NotContain("PasswordHash");
    }

    // -------------------------------------------------------------------------
    // AssignRoleAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AssignRoleAsync_UsesRealSeededRoleId_NotGuidEmpty()
    {
        var userId = Guid.NewGuid();
        var seededRoleId = Guid.Parse("10000000-0000-0000-0000-000000000003");
        var seededRole = new Role { Id = seededRoleId, Name = RoleType.StoreManager, Scope = RoleScope.Tenant };
        var user = new User { Id = userId, Email = "u@brand.com", UserRoles = new List<UserRole>() };

        _userRepo.Setup(r => r.GetByIdWithRolesAsync(userId)).ReturnsAsync(user);
        _roleRepo.Setup(r => r.GetByRoleTypeAsync(RoleType.StoreManager, default)).ReturnsAsync(seededRole);

        ResponseData<bool> result = await CreateService().AssignRoleAsync(userId, RoleType.StoreManager,
            Guid.NewGuid(), "127.0.0.1", "Mozilla");

        result.IsSuccess.Should().BeTrue();
        user.UserRoles.Should().HaveCount(1);
        user.UserRoles.Single().RoleId.Should().Be(seededRoleId);
        user.UserRoles.Single().RoleId.Should().NotBe(Guid.Empty);
        // No new Role navigation object should be set (avoids EF INSERT on seeded row)
        user.UserRoles.Single().Role.Should().BeNull();
    }

    [Fact]
    public async Task AssignRoleAsync_RoleNotFound_Returns404()
    {
        var userId = Guid.NewGuid();
        var user = new User { Id = userId, Email = "u@brand.com", UserRoles = new List<UserRole>() };
        _userRepo.Setup(r => r.GetByIdWithRolesAsync(userId)).ReturnsAsync(user);
        _roleRepo.Setup(r => r.GetByRoleTypeAsync(It.IsAny<RoleType>(), default)).ReturnsAsync((Role?)null);

        ResponseData<bool> result = await CreateService().AssignRoleAsync(userId, RoleType.StoreManager,
            Guid.NewGuid(), "127.0.0.1", "Mozilla");

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task AssignRoleAsync_UserNotFound_Returns404()
    {
        _userRepo.Setup(r => r.GetByIdWithRolesAsync(It.IsAny<Guid>())).ReturnsAsync((User?)null);

        ResponseData<bool> result = await CreateService().AssignRoleAsync(Guid.NewGuid(), RoleType.StoreManager,
            Guid.NewGuid(), "127.0.0.1", "Mozilla");

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    // -------------------------------------------------------------------------
    // DeactivateAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DeactivateAsync_ExistingUser_SetsInactive()
    {
        var user = new User { Id = Guid.NewGuid(), IsActive = true, Email = "user@brand.com" };
        _userRepo.Setup(r => r.GetByIdAsync(user.Id)).ReturnsAsync(user);

        ResponseData<bool> result = await CreateService().DeactivateAsync(user.Id, Guid.NewGuid(), "127.0.0.1", "Mozilla");

        result.IsSuccess.Should().BeTrue();
        user.IsActive.Should().BeFalse();
        _uow.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task DeactivateAsync_AuditOldValue_UsesActualState()
    {
        // Fix 4: the old-value WasActive must reflect the actual state before mutation.
        var user = new User { Id = Guid.NewGuid(), IsActive = false, Email = "user@brand.com" };
        _userRepo.Setup(r => r.GetByIdAsync(user.Id)).ReturnsAsync(user);

        object? capturedOldValue = null;
        _audit.Setup(a => a.LogAsync(
                It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<Guid>(), It.IsAny<object?>(), It.IsAny<object?>(),
                It.IsAny<string>(), It.IsAny<string>()))
            .Callback((Guid? _, Guid? __, string ___, string ____, Guid _____, object? oldVal, object? ______, string _______, string ________) =>
                capturedOldValue = oldVal)
            .Returns(Task.CompletedTask);

        await CreateService().DeactivateAsync(user.Id, Guid.NewGuid(), "127.0.0.1", "Mozilla");

        // WasActive should be false (the actual state), not hardcoded true.
        capturedOldValue.Should().NotBeNull();
        var wasActive = (bool)capturedOldValue!.GetType().GetProperty("WasActive")!.GetValue(capturedOldValue)!;
        wasActive.Should().BeFalse();
    }

    [Fact]
    public async Task DeactivateAsync_UserNotFound_Returns404()
    {
        _userRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((User?)null);

        ResponseData<bool> result = await CreateService().DeactivateAsync(Guid.NewGuid(), Guid.NewGuid(), "127.0.0.1", "Mozilla");

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    // -------------------------------------------------------------------------
    // UnlockAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task UnlockAsync_LockedUser_SetsActive()
    {
        var user = new User { Id = Guid.NewGuid(), IsActive = false, Email = "locked@brand.com" };
        _userRepo.Setup(r => r.GetByIdAsync(user.Id)).ReturnsAsync(user);

        ResponseData<bool> result = await CreateService().UnlockAsync(user.Id, Guid.NewGuid(), "127.0.0.1", "Mozilla");

        result.IsSuccess.Should().BeTrue();
        user.IsActive.Should().BeTrue();
        _uow.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task UnlockAsync_ResetsRecentFailedAttempts()
    {
        // Fix 3: ResetRecentFailedAttemptsAsync must be called so lockout is cleared.
        var user = new User { Id = Guid.NewGuid(), IsActive = false, Email = "locked@brand.com" };
        _userRepo.Setup(r => r.GetByIdAsync(user.Id)).ReturnsAsync(user);

        ResponseData<bool> result = await CreateService().UnlockAsync(user.Id, Guid.NewGuid(), "127.0.0.1", "Mozilla");

        result.IsSuccess.Should().BeTrue();
        _loginAttemptRepo.Verify(
            r => r.ResetRecentFailedAttemptsAsync(user.Email, default),
            Times.Once);
    }

    [Fact]
    public async Task UnlockAsync_UserNotFound_Returns404()
    {
        _userRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((User?)null);

        ResponseData<bool> result = await CreateService().UnlockAsync(Guid.NewGuid(), Guid.NewGuid(), "127.0.0.1", "Mozilla");

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    // -------------------------------------------------------------------------
    // GetByIdAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetByIdAsync_ExistingUser_ReturnsUserResponse()
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            FirstName = "Ali",
            LastName = "Khan",
            Email = "ali@brand.com",
            IsActive = true,
            UserRoles = new List<UserRole>
            {
                new() { Role = new Role { Name = RoleType.StoreManager, Scope = RoleScope.Tenant } }
            }
        };
        _userRepo.Setup(r => r.GetByIdWithRolesAsync(user.Id)).ReturnsAsync(user);

        ResponseData<UserResponse> result = await CreateService().GetByIdAsync(user.Id);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Email.Should().Be("ali@brand.com");
        result.Data.Roles.Should().Contain("StoreManager");
    }

    [Fact]
    public async Task GetByIdAsync_NotFound_Returns404()
    {
        _userRepo.Setup(r => r.GetByIdWithRolesAsync(It.IsAny<Guid>())).ReturnsAsync((User?)null);

        ResponseData<UserResponse> result = await CreateService().GetByIdAsync(Guid.NewGuid());

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    // -------------------------------------------------------------------------
    // DeleteAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DeleteAsync_ExistingUser_Deletes()
    {
        var user = new User { Id = Guid.NewGuid(), Email = "del@brand.com" };
        _userRepo.Setup(r => r.GetByIdAsync(user.Id)).ReturnsAsync(user);

        ResponseData<bool> result = await CreateService().DeleteAsync(user.Id, Guid.NewGuid(), "127.0.0.1", "Mozilla");

        result.IsSuccess.Should().BeTrue();
        _userRepo.Verify(r => r.DeleteAsync(user), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    // -------------------------------------------------------------------------
    // UpdateAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task UpdateAsync_ExistingUser_UpdatesNameFields()
    {
        var user = new User { Id = Guid.NewGuid(), FirstName = "Old", LastName = "Name", Email = "u@brand.com" };
        _userRepo.Setup(r => r.GetByIdAsync(user.Id)).ReturnsAsync(user);

        ResponseData<UserResponse> result = await CreateService().UpdateAsync(user.Id,
            new UpdateUserRequest { FirstName = "New", LastName = "Name2" },
            Guid.NewGuid(), "127.0.0.1", "Mozilla");

        result.IsSuccess.Should().BeTrue();
        user.FirstName.Should().Be("New");
        user.LastName.Should().Be("Name2");
    }

    // -------------------------------------------------------------------------
    // GetByTenantAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetByTenantAsync_FiltersAndPaginatesCorrectly()
    {
        var tenantId = Guid.NewGuid();
        var users = Enumerable.Range(1, 25).Select(i => new User
        {
            Id = Guid.NewGuid(),
            FirstName = $"User{i}",
            LastName = "Test",
            Email = $"user{i}@brand.com",
            IsActive = i % 2 == 0,
            TenantId = tenantId
        }).ToList();

        _userRepo.Setup(r => r.GetByTenantAsync(tenantId)).ReturnsAsync(users);

        ResponseData<PagedResult<UserResponse>> result = await CreateService().GetByTenantAsync(tenantId,
            new UserFilterRequest { IsActive = true, Page = 1, PageSize = 5 });

        result.IsSuccess.Should().BeTrue();
        result.Data!.Items.Count.Should().Be(5);
        result.Data.TotalCount.Should().Be(12); // 25 users, even indices active: 2,4,6..24 = 12
    }
}
