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

    // Phase 2 catalog
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();
    public DbSet<ProductImage> ProductImages => Set<ProductImage>();
    public DbSet<StockAdjustment> StockAdjustments => Set<StockAdjustment>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Discount> Discounts => Set<Discount>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<Wishlist> Wishlists => Set<Wishlist>();
    public DbSet<WishlistItem> WishlistItems => Set<WishlistItem>();

    // Phase 4a orders
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();

    // Phase 9a manual payment proof
    public DbSet<OrderPaymentProof> OrderPaymentProofs => Set<OrderPaymentProof>();

    // Phase 7 notifications
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<NotificationRead> NotificationReads => Set<NotificationRead>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

        // Global query filter for multi-tenancy — installed unconditionally so EF does not skip it
        // when the model is first built (OnModelCreating runs once with TenantId null).
        // The lambda closes over the injected service instance; EF re-evaluates TenantId per query.
        // When TenantId is null (SuperAdmin/platform context) this resolves to TenantId == null,
        // returning only platform-owned rows — any path that must read across tenants or by a
        // specific tenant must call .IgnoreQueryFilters() (see BankAccountRepository).
        modelBuilder.Entity<BankAccount>()
            .HasQueryFilter(b => b.TenantId == currentTenantService.TenantId);

        // Phase 2 catalog — same dynamic tenant filter. The lambda references the injected
        // currentTenantService instance (not a captured local) so EF re-evaluates TenantId per query.
        // EF requires consistent filters across required relationships, so every tenant-owned entity
        // in the catalog graph is filtered (filtered principal ↔ filtered dependent).
        modelBuilder.Entity<Category>()
            .HasQueryFilter(c => c.TenantId == currentTenantService.TenantId);
        modelBuilder.Entity<Product>()
            .HasQueryFilter(p => p.TenantId == currentTenantService.TenantId);
        modelBuilder.Entity<ProductVariant>()
            .HasQueryFilter(v => v.TenantId == currentTenantService.TenantId);
        modelBuilder.Entity<ProductImage>()
            .HasQueryFilter(i => i.TenantId == currentTenantService.TenantId);
        modelBuilder.Entity<StockAdjustment>()
            .HasQueryFilter(s => s.TenantId == currentTenantService.TenantId);
        modelBuilder.Entity<Customer>()
            .HasQueryFilter(c => c.TenantId == currentTenantService.TenantId);
        modelBuilder.Entity<Discount>()
            .HasQueryFilter(d => d.TenantId == currentTenantService.TenantId);
        modelBuilder.Entity<Review>()
            .HasQueryFilter(r => r.TenantId == currentTenantService.TenantId);
        modelBuilder.Entity<Wishlist>()
            .HasQueryFilter(w => w.TenantId == currentTenantService.TenantId);
        modelBuilder.Entity<WishlistItem>()
            .HasQueryFilter(i => i.TenantId == currentTenantService.TenantId);

        // Phase 4a orders — same dynamic tenant filter pattern as the catalog entities above.
        modelBuilder.Entity<Order>()
            .HasQueryFilter(o => o.TenantId == currentTenantService.TenantId);

        // Phase 9a manual payment proof — same dynamic tenant filter pattern as Order above.
        modelBuilder.Entity<OrderPaymentProof>()
            .HasQueryFilter(p => p.TenantId == currentTenantService.TenantId);

        // Phase 7 notifications — TenantId is nullable (null = platform/SuperAdmin-scoped row).
        // Strict equality, matching the BankAccount/catalog pattern above: fail-closed means a
        // null-TenantId row is visible ONLY when the current context is also tenant-less
        // (true SuperAdmin), never to every tenant. Broadcast-vs-targeted delivery within a
        // tenant (RecipientUserId null vs a specific user) is handled by NotificationService's
        // queries, not by this global tenant-boundary filter.
        modelBuilder.Entity<Notification>()
            .HasQueryFilter(n => n.TenantId == currentTenantService.TenantId);
    }
}
