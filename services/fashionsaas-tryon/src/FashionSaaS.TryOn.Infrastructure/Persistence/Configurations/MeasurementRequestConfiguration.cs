using FashionSaaS.TryOn.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FashionSaaS.TryOn.Infrastructure.Persistence.Configurations;

public class MeasurementRequestConfiguration : IEntityTypeConfiguration<MeasurementRequest>
{
    public void Configure(EntityTypeBuilder<MeasurementRequest> builder)
    {
        builder.HasKey(m => m.Id);
        builder.Property(m => m.FailureReason).HasMaxLength(500);
        builder.Property(m => m.ChestCm).HasPrecision(5, 1);
        builder.Property(m => m.WaistCm).HasPrecision(5, 1);
        builder.Property(m => m.HipsCm).HasPrecision(5, 1);
        builder.Property(m => m.ShoulderWidthCm).HasPrecision(5, 1);
        builder.Property(m => m.InseamCm).HasPrecision(5, 1);
        builder.Property(m => m.ConfidenceScore).HasPrecision(3, 2);

        // Same shape as TryOnRequestConfiguration's index — IUsageQuotaService.GetUsedThisMonthAsync
        // filters WHERE TenantId = X AND Status = Completed AND CreatedAt >= start-of-month.
        builder.HasIndex(m => new { m.TenantId, m.Status, m.CreatedAt });
    }
}
