using FashionSaaS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FashionSaaS.Infrastructure.Persistence.Configurations;

public class UserMfaSettingsConfiguration : IEntityTypeConfiguration<UserMfaSettings>
{
    public void Configure(EntityTypeBuilder<UserMfaSettings> builder)
    {
        builder.HasKey(m => m.Id);
        builder.Property(m => m.TotpSecretEncrypted).HasMaxLength(1000);
        builder.HasMany(m => m.BackupCodes).WithOne(b => b.MfaSettings)
            .HasForeignKey(b => b.UserMfaSettingsId).OnDelete(DeleteBehavior.Cascade);
    }
}
