using FashionSaaS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FashionSaaS.Infrastructure.Persistence.Configurations;

public class UserLoginAttemptConfiguration : IEntityTypeConfiguration<UserLoginAttempt>
{
    public void Configure(EntityTypeBuilder<UserLoginAttempt> builder)
    {
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Email).HasMaxLength(320).IsRequired();
        builder.Property(l => l.IpAddress).HasMaxLength(45).IsRequired();
        builder.Property(l => l.UserAgent).HasMaxLength(500).IsRequired();
        builder.Property(l => l.FailureReason).HasMaxLength(200);
        builder.HasIndex(l => new { l.Email, l.CreatedAt });
    }
}
