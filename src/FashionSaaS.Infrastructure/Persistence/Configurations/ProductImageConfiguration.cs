using FashionSaaS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FashionSaaS.Infrastructure.Persistence.Configurations;

public class ProductImageConfiguration : IEntityTypeConfiguration<ProductImage>
{
    public void Configure(EntityTypeBuilder<ProductImage> builder)
    {
        builder.HasKey(i => i.Id);
        builder.Property(i => i.CloudinaryPublicId).HasMaxLength(500).IsRequired();
        builder.Property(i => i.Url).HasMaxLength(2000).IsRequired();
        builder.Property(i => i.AltText).HasMaxLength(500);

        builder.HasIndex(i => i.ProductId);
    }
}
