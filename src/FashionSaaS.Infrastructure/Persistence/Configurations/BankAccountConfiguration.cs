using FashionSaaS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FashionSaaS.Infrastructure.Persistence.Configurations;

public class BankAccountConfiguration : IEntityTypeConfiguration<BankAccount>
{
    public void Configure(EntityTypeBuilder<BankAccount> builder)
    {
        builder.HasKey(b => b.Id);
        builder.Property(b => b.TenantId).IsRequired(false);
        builder.Property(b => b.AccountTitleEncrypted).HasMaxLength(2000).IsRequired();
        builder.Property(b => b.AccountNumberEncrypted).HasMaxLength(2000).IsRequired();
        builder.Property(b => b.BankNameEncrypted).HasMaxLength(2000).IsRequired();
        builder.Property(b => b.BranchCodeEncrypted).HasMaxLength(2000).IsRequired();
        builder.Property(b => b.IbanEncrypted).HasMaxLength(2000).IsRequired();

        builder.HasOne(b => b.Tenant).WithMany(t => t.BankAccounts)
            .HasForeignKey(b => b.TenantId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
    }
}
