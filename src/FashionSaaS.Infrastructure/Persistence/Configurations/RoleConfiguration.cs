using FashionSaaS.Domain.Entities;
using FashionSaaS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FashionSaaS.Infrastructure.Persistence.Configurations;

public class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Name).IsRequired();
        builder.Property(r => r.Scope).IsRequired();
        builder.HasIndex(r => r.Name).IsUnique();

        var seedDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        builder.HasData(
            new Role { Id = Guid.Parse("10000000-0000-0000-0000-000000000001"), Name = RoleType.SuperAdmin, Scope = RoleScope.Platform, CreatedAt = seedDate, UpdatedAt = seedDate },
            new Role { Id = Guid.Parse("10000000-0000-0000-0000-000000000002"), Name = RoleType.AdminOwner, Scope = RoleScope.Tenant, CreatedAt = seedDate, UpdatedAt = seedDate },
            new Role { Id = Guid.Parse("10000000-0000-0000-0000-000000000003"), Name = RoleType.StoreManager, Scope = RoleScope.Tenant, CreatedAt = seedDate, UpdatedAt = seedDate },
            new Role { Id = Guid.Parse("10000000-0000-0000-0000-000000000004"), Name = RoleType.InventoryManager, Scope = RoleScope.Tenant, CreatedAt = seedDate, UpdatedAt = seedDate },
            new Role { Id = Guid.Parse("10000000-0000-0000-0000-000000000005"), Name = RoleType.OrderManager, Scope = RoleScope.Tenant, CreatedAt = seedDate, UpdatedAt = seedDate },
            new Role { Id = Guid.Parse("10000000-0000-0000-0000-000000000006"), Name = RoleType.ContentManager, Scope = RoleScope.Tenant, CreatedAt = seedDate, UpdatedAt = seedDate },
            new Role { Id = Guid.Parse("10000000-0000-0000-0000-000000000007"), Name = RoleType.Customer, Scope = RoleScope.Customer, CreatedAt = seedDate, UpdatedAt = seedDate }
        );
    }
}
