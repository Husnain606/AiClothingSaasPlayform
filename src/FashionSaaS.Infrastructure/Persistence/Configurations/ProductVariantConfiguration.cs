using FashionSaaS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FashionSaaS.Infrastructure.Persistence.Configurations;

public class ProductVariantConfiguration : IEntityTypeConfiguration<ProductVariant>
{
    public void Configure(EntityTypeBuilder<ProductVariant> builder)
    {
        builder.HasKey(v => v.Id);
        builder.Property(v => v.Size).HasMaxLength(100).IsRequired();
        builder.Property(v => v.Color).HasMaxLength(100).IsRequired();
        builder.Property(v => v.Sku).HasMaxLength(100).IsRequired();
        builder.Property(v => v.PriceOverride).HasPrecision(18, 2);

        builder.HasIndex(v => new { v.TenantId, v.Sku }).IsUnique();
        builder.HasIndex(v => v.ProductId);
        builder.HasIndex(v => new { v.ProductId, v.Size, v.Color }).IsUnique();
    }
}
