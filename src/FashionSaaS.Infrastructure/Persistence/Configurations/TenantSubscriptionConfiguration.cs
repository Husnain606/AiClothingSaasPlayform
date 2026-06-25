using FashionSaaS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FashionSaaS.Infrastructure.Persistence.Configurations;

public class TenantSubscriptionConfiguration : IEntityTypeConfiguration<TenantSubscription>
{
    public void Configure(EntityTypeBuilder<TenantSubscription> builder)
    {
        builder.HasKey(s => s.Id);
        builder.HasIndex(s => new { s.TenantId, s.Status });
        builder.HasOne(s => s.Tenant).WithMany(t => t.Subscriptions)
            .HasForeignKey(s => s.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(s => s.Plan).WithMany(p => p.TenantSubscriptions)
            .HasForeignKey(s => s.PlanId).OnDelete(DeleteBehavior.Restrict);
    }
}
