using FashionSaaS.Application.Configuration;
using FashionSaaS.Application.Interfaces;
using FashionSaaS.Infrastructure.BackgroundJobs;
using FashionSaaS.Infrastructure.Persistence;
using FashionSaaS.Infrastructure.Persistence.Repositories;
using FashionSaaS.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FashionSaaS.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Options bindings (CONVENTIONS §2) — JWT and Encryption fail fast at startup; SMTP is lazy
        services.AddOptions<JwtSettings>()
            .Bind(configuration.GetSection(JwtSettings.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<EncryptionSettings>()
            .Bind(configuration.GetSection(EncryptionSettings.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.Configure<SmtpSettings>(configuration.GetSection(SmtpSettings.SectionName));
        services.Configure<CorsSettings>(configuration.GetSection(CorsSettings.SectionName));
        services.AddOptions<CloudinarySettings>()
            .Bind(configuration.GetSection(CloudinarySettings.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<PaymentProofStorageSettings>()
            .Bind(configuration.GetSection(PaymentProofStorageSettings.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // DbContext
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseSqlServer(
                configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection not set."),
                b => b.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName)));

        // Tenant service — Scoped so it's populated per-request by middleware
        services.AddScoped<ICurrentTenantService, CurrentTenantService>();

        // Security services
        services.AddScoped<IPasswordHasher, PasswordHasherService>();
        services.AddScoped<IFieldEncryptionService, FieldEncryptionService>();
        services.AddScoped<IJwtService, JwtService>();
        services.AddScoped<ITotpService, TotpService>();

        // Email
        services.AddScoped<IEmailService, SmtpEmailService>();

        // Image storage (Cloudinary)
        services.AddScoped<IImageStorageService, CloudinaryImageStorageService>();

        // Payment proof storage — THE Azure swap point. To move to Azure Blob Storage, implement
        // IPaymentProofStorageService as AzureBlobPaymentProofStorageService and change only this line.
        services.AddScoped<IPaymentProofStorageService, LocalFilePaymentProofStorageService>();

        // Audit log
        services.AddScoped<IAuditLogService, AuditLogService>();

        // Unit of Work
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        // Repositories
        services.AddScoped<ITenantRepository, TenantRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IPasswordHistoryRepository, PasswordHistoryRepository>();
        services.AddScoped<IPasswordResetTokenRepository, PasswordResetTokenRepository>();
        services.AddScoped<ISubscriptionPlanRepository, SubscriptionPlanRepository>();
        services.AddScoped<ISubscriptionRepository, SubscriptionRepository>();
        services.AddScoped<IPaymentRepository, PaymentRepository>();
        services.AddScoped<IBankAccountRepository, BankAccountRepository>();
        services.AddScoped<IAuditLogRepository, AuditLogRepository>();
        services.AddScoped<ILoginAttemptRepository, LoginAttemptRepository>();
        services.AddScoped<IRoleRepository, RoleRepository>();

        // Phase 2 — Catalog repositories
        services.AddScoped<ICategoryRepository, CategoryRepository>();
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<IProductVariantRepository, ProductVariantRepository>();
        services.AddScoped<IProductImageRepository, ProductImageRepository>();
        services.AddScoped<IStockAdjustmentRepository, StockAdjustmentRepository>();
        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<IDiscountRepository, DiscountRepository>();
        services.AddScoped<IReviewRepository, ReviewRepository>();
        services.AddScoped<IWishlistRepository, WishlistRepository>();
        services.AddScoped<IOrderRepository, OrderRepository>();

        // Phase 4a — Reporting
        services.AddScoped<IReportRepository, ReportRepository>();

        // Phase 7 — Notifications
        services.AddScoped<INotificationRepository, NotificationRepository>();

        // Background jobs
        services.AddHostedService<SubscriptionExpiryJob>();

        return services;
    }
}
