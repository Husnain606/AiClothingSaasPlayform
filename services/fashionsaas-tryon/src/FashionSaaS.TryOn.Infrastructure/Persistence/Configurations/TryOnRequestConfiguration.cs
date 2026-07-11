using FashionSaaS.TryOn.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FashionSaaS.TryOn.Infrastructure.Persistence.Configurations;

public class TryOnRequestConfiguration : IEntityTypeConfiguration<TryOnRequest>
{
    public void Configure(EntityTypeBuilder<TryOnRequest> builder)
    {
        builder.HasKey(t => t.Id);
        builder.Property(t => t.FailureReason).HasMaxLength(500);

        // Quota-counting query filters on these three; CreatedAt additionally orders the
        // month-window scan (D8's COUNT(*) WHERE TenantId = X AND Status = Completed AND
        // CreatedAt >= start-of-month).
        builder.HasIndex(t => new { t.TenantId, t.Status, t.CreatedAt });
    }
}
