using FashionSaaS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FashionSaaS.Infrastructure.Persistence.Configurations;

public class MfaBackupCodeConfiguration : IEntityTypeConfiguration<MfaBackupCode>
{
    public void Configure(EntityTypeBuilder<MfaBackupCode> builder)
    {
        builder.HasKey(b => b.Id);
        builder.Property(b => b.CodeHash).HasMaxLength(500).IsRequired();
    }
}
