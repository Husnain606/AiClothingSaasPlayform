using FashionSaaS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FashionSaaS.Infrastructure.Persistence.Configurations;

public class OrderPaymentProofConfiguration : IEntityTypeConfiguration<OrderPaymentProof>
{
    public void Configure(EntityTypeBuilder<OrderPaymentProof> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.StorageKey).HasMaxLength(500).IsRequired();
        builder.Property(p => p.ContentType).HasMaxLength(100).IsRequired();
        builder.Property(p => p.OriginalFileName).HasMaxLength(260).IsRequired();

        builder.HasIndex(p => p.TenantId);

        // One proof per order.
        builder.HasIndex(p => p.OrderId).IsUnique();

        builder.HasOne(p => p.Order).WithOne(o => o.PaymentProof)
            .HasForeignKey<OrderPaymentProof>(p => p.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
