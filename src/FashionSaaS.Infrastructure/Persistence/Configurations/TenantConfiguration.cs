using FashionSaaS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FashionSaaS.Infrastructure.Persistence.Configurations;

public class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Name).HasMaxLength(200).IsRequired();
        builder.Property(t => t.Slug).HasMaxLength(50).IsRequired();
        builder.HasIndex(t => t.Slug).IsUnique();
        builder.Property(t => t.Email).HasMaxLength(320).IsRequired();
        builder.HasIndex(t => t.Email).IsUnique();
        builder.Property(t => t.Phone).HasMaxLength(20);
        builder.Property(t => t.LogoUrl).HasMaxLength(500);
        builder.Property(t => t.CoverImageUrl).HasMaxLength(500);
    }
}
