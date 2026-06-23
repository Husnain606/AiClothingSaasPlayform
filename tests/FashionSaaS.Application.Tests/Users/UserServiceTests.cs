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

    private UserService CreateService() => new(_userRepo.Object, _hasher.Object,
        _email.Object, _audit.Object, _uow.Object);

    [Fact]
    public async Task CreateAsync_NewEmail_CreatesUser()
    {
        _userRepo.Setup(r => r.EmailExistsAsync("new@brand.com")).ReturnsAsync(false);
        _hasher.Setup(h => h.Hash(It.IsAny<string>())).Returns("hashed");

        var result = await CreateService().CreateAsync(
            new CreateUserRequest { Email = "new@brand.com", FirstName = "Ali", LastName = "Khan", Role = RoleType.StoreManager },
            Guid.NewGuid(), "127.0.0.1", "Mozilla");

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(201);
    }

    [Fact]
    public async Task CreateAsync_DuplicateEmail_ReturnsConflict()
    {
        _userRepo.Setup(r => r.EmailExistsAsync("dup@brand.com")).ReturnsAsync(true);

        var result = await CreateService().CreateAsync(
            new CreateUserRequest { Email = "dup@brand.com", FirstName = "A", LastName = "B", Role = RoleType.StoreManager },
            Guid.NewGuid(), "127.0.0.1", "Mozilla");

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task CreateAsync_NeverReturnsPasswordHash()
    {
        _userRepo.Setup(r => r.EmailExistsAsync("safe@brand.com")).ReturnsAsync(false);
        _hasher.Setup(h => h.Hash(It.IsAny<string>())).Returns("secret-hash");

        var result = await CreateService().CreateAsync(
            new CreateUserRequest { Email = "safe@brand.com", FirstName = "Test", LastName = "User", Role = RoleType.StoreManager },
            Guid.NewGuid(), "127.0.0.1", "Mozilla");

        // UserResponse must NOT contain PasswordHash
        result.IsSuccess.Should().BeTrue();
        result.Data.Should().NotBeNull();
        // Verify the response DTO has no hash property (structural check via type)
        var props = result.Data!.GetType().GetProperties().Select(p => p.Name);
        props.Should().NotContain("PasswordHash");
    }

    [Fact]
    public async Task DeactivateAsync_ExistingUser_SetsInactive()
    {
        var user = new User { Id = Guid.NewGuid(), IsActive = true, Email = "user@brand.com" };
        _userRepo.Setup(r => r.GetByIdAsync(user.Id)).ReturnsAsync(user);

        var result = await CreateService().DeactivateAsync(user.Id, Guid.NewGuid(), "127.0.0.1", "Mozilla");

        result.IsSuccess.Should().BeTrue();
        user.IsActive.Should().BeFalse();
        _uow.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task DeactivateAsync_UserNotFound_Returns404()
    {
        _userRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((User?)null);

        var result = await CreateService().DeactivateAsync(Guid.NewGuid(), Guid.NewGuid(), "127.0.0.1", "Mozilla");

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task UnlockAsync_LockedUser_SetsActive()
    {
        var user = new User { Id = Guid.NewGuid(), IsActive = false, Email = "locked@brand.com" };
        _userRepo.Setup(r => r.GetByIdAsync(user.Id)).ReturnsAsync(user);

        var result = await CreateService().UnlockAsync(user.Id, Guid.NewGuid(), "127.0.0.1", "Mozilla");

        result.IsSuccess.Should().BeTrue();
        user.IsActive.Should().BeTrue();
        _uow.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task UnlockAsync_UserNotFound_Returns404()
    {
        _userRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((User?)null);

        var result = await CreateService().UnlockAsync(Guid.NewGuid(), Guid.NewGuid(), "127.0.0.1", "Mozilla");

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task GetByIdAsync_ExistingUser_ReturnsUserResponse()
    {
        var user = new User
        {
            Id = Guid.NewGuid(), FirstName = "Ali", LastName = "Khan",
            Email = "ali@brand.com", IsActive = true,
            UserRoles = new List<UserRole>
            {
                new() { Role = new Role { Name = RoleType.StoreManager, Scope = RoleScope.Tenant } }
            }
        };
        _userRepo.Setup(r => r.GetByIdWithRolesAsync(user.Id)).ReturnsAsync(user);

        var result = await CreateService().GetByIdAsync(user.Id);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Email.Should().Be("ali@brand.com");
        result.Data.Roles.Should().Contain("StoreManager");
    }

    [Fact]
    public async Task GetByIdAsync_NotFound_Returns404()
    {
        _userRepo.Setup(r => r.GetByIdWithRolesAsync(It.IsAny<Guid>())).ReturnsAsync((User?)null);

        var result = await CreateService().GetByIdAsync(Guid.NewGuid());

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task DeleteAsync_ExistingUser_Deletes()
    {
        var user = new User { Id = Guid.NewGuid(), Email = "del@brand.com" };
        _userRepo.Setup(r => r.GetByIdAsync(user.Id)).ReturnsAsync(user);

        var result = await CreateService().DeleteAsync(user.Id, Guid.NewGuid(), "127.0.0.1", "Mozilla");

        result.IsSuccess.Should().BeTrue();
        _userRepo.Verify(r => r.DeleteAsync(user), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_ExistingUser_UpdatesNameFields()
    {
        var user = new User { Id = Guid.NewGuid(), FirstName = "Old", LastName = "Name", Email = "u@brand.com" };
        _userRepo.Setup(r => r.GetByIdAsync(user.Id)).ReturnsAsync(user);

        var result = await CreateService().UpdateAsync(user.Id,
            new UpdateUserRequest { FirstName = "New", LastName = "Name2" },
            Guid.NewGuid(), "127.0.0.1", "Mozilla");

        result.IsSuccess.Should().BeTrue();
        user.FirstName.Should().Be("New");
        user.LastName.Should().Be("Name2");
    }

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

        var result = await CreateService().GetByTenantAsync(tenantId,
            new UserFilterRequest { IsActive = true, Page = 1, PageSize = 5 });

        result.IsSuccess.Should().BeTrue();
        result.Data!.Items.Count.Should().Be(5);
        result.Data.TotalCount.Should().Be(12); // 25 users, even indices active: 2,4,6..24 = 12
    }
}
