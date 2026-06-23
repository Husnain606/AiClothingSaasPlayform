using FashionSaaS.Domain.Enums;

namespace FashionSaaS.Application.Users.DTOs;

public class CreateUserRequest
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public Guid? TenantId { get; set; }
    public RoleType Role { get; set; }
}
