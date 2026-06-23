using FashionSaaS.Domain.Enums;

namespace FashionSaaS.Domain.Entities;

public class Role : BaseEntity
{
    public RoleType Name { get; set; }
    public RoleScope Scope { get; set; }

    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
}
