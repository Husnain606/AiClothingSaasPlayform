using FashionSaaS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FashionSaaS.Infrastructure.Persistence.Configurations;

public class SubscriptionPaymentConfiguration : IEntityTypeConfiguration<SubscriptionPayment>
{
    public void Configure(EntityTypeBuilder<SubscriptionPayment> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Amount).HasPrecision(18, 2);
        builder.HasOne(p => p.Subscription).WithMany(s => s.Payments)
            .HasForeignKey(p => p.SubscriptionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(p => p.Tenant).WithMany()
            .HasForeignKey(p => p.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}
