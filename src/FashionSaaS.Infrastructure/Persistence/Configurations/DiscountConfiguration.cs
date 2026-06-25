using FashionSaaS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FashionSaaS.Infrastructure.Persistence.Configurations;

public class DiscountConfiguration : IEntityTypeConfiguration<Discount>
{
    public void Configure(EntityTypeBuilder<Discount> builder)
    {
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Code).HasMaxLength(100).IsRequired();
        builder.Property(d => d.Value).HasPrecision(18, 2);
        builder.Property(d => d.MinOrderAmount).HasPrecision(18, 2);

        builder.HasIndex(d => new { d.TenantId, d.Code }).IsUnique();
    }
}
