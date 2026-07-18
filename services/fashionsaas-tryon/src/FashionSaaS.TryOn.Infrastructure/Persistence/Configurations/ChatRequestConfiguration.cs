using FashionSaaS.TryOn.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FashionSaaS.TryOn.Infrastructure.Persistence.Configurations;

public class ChatRequestConfiguration : IEntityTypeConfiguration<ChatRequest>
{
    public void Configure(EntityTypeBuilder<ChatRequest> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.FailureReason).HasMaxLength(500);

        // Same shape as TryOnRequestConfiguration's index — required by IUsageQuotaService.
        builder.HasIndex(c => new { c.TenantId, c.Status, c.CreatedAt });
    }
}
