using FashionSaaS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FashionSaaS.Infrastructure.Persistence.Configurations;

public class PasswordResetTokenConfiguration : IEntityTypeConfiguration<PasswordResetToken>
{
    public void Configure(EntityTypeBuilder<PasswordResetToken> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.TokenHash).HasMaxLength(500).IsRequired();
        builder.HasIndex(p => p.TokenHash);
    }
}
