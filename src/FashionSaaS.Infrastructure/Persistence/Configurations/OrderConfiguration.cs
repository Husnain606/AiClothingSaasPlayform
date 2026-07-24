using FashionSaaS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FashionSaaS.Infrastructure.Persistence.Configurations;

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.HasKey(o => o.Id);
        builder.Property(o => o.OrderNumber).HasMaxLength(20).IsRequired();
        builder.Property(o => o.ShippingFirstName).HasMaxLength(100).IsRequired();
        builder.Property(o => o.ShippingLastName).HasMaxLength(100).IsRequired();
        builder.Property(o => o.ShippingEmail).HasMaxLength(256).IsRequired();
        builder.Property(o => o.ShippingPhone).HasMaxLength(30).IsRequired();
        builder.Property(o => o.ShippingStreet).HasMaxLength(200).IsRequired();
        builder.Property(o => o.ShippingCity).HasMaxLength(100).IsRequired();
        builder.Property(o => o.ShippingState).HasMaxLength(100).IsRequired();
        builder.Property(o => o.ShippingZipCode).HasMaxLength(20).IsRequired();
        builder.Property(o => o.ShippingCountry).HasMaxLength(2).IsRequired();
        builder.Property(o => o.CardLast4).HasMaxLength(4).IsRequired();
        builder.Property(o => o.TrackingNumber).HasMaxLength(100);
        builder.Property(o => o.CancelReason).HasMaxLength(500);
        builder.Property(o => o.Subtotal).HasPrecision(18, 2);
        builder.Property(o => o.Tax).HasPrecision(18, 2);
        builder.Property(o => o.ShippingCost).HasPrecision(18, 2);
        builder.Property(o => o.Total).HasPrecision(18, 2);
        builder.Property(o => o.DiscountCode).HasMaxLength(50);
        builder.Property(o => o.DiscountAmount).HasPrecision(18, 2);

        builder.HasIndex(o => new { o.TenantId, o.OrderNumber }).IsUnique();
        builder.HasIndex(o => new { o.TenantId, o.OrderDate });
        builder.HasIndex(o => new { o.TenantId, o.Status });
        builder.HasIndex(o => new { o.TenantId, o.CustomerId });

        builder.HasOne(o => o.Customer).WithMany().HasForeignKey(o => o.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(o => o.Items).WithOne(i => i.Order).HasForeignKey(i => i.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
