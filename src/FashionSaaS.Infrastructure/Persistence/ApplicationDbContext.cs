using FashionSaaS.Application.Interfaces;
using FashionSaaS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FashionSaaS.Infrastructure.Persistence;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options, ICurrentTenantService currentTenantService)
    : DbContext(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<PasswordHistory> PasswordHistories => Set<PasswordHistory>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<UserMfaSettings> UserMfaSettings => Set<UserMfaSettings>();
    public DbSet<MfaBackupCode> MfaBackupCodes => Set<MfaBackupCode>();
    public DbSet<SubscriptionPlan> SubscriptionPlans => Set<SubscriptionPlan>();
    public DbSet<TenantSubscription> TenantSubscriptions => Set<TenantSubscription>();
    public DbSet<SubscriptionPayment> SubscriptionPayments => Set<SubscriptionPayment>();
    public DbSet<BankAccount> BankAccounts => Set<BankAccount>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<UserLoginAttempt> UserLoginAttempts => Set<UserLoginAttempt>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

        // Global query filter for multi-tenancy — applies to all TenantOwnedEntity subclasses
        // BankAccount has nullable TenantId (null = platform account), so filter only when resolved
        var tenantId = currentTenantService.TenantId;
        if (tenantId.HasValue)
        {
            modelBuilder.Entity<BankAccount>()
                .HasQueryFilter(b => b.TenantId == tenantId.Value);
        }
    }
}
