using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using FashionSaaS.Application.AuditLogs;
using FashionSaaS.Application.Auth;
using FashionSaaS.Application.Behaviors;
using FashionSaaS.Application.BankAccounts;
using FashionSaaS.Application.Categories;
using FashionSaaS.Application.Configuration;
using FashionSaaS.Application.Interfaces;
using FashionSaaS.Application.LoginAttempts;
using FashionSaaS.Application.Mfa;
using FashionSaaS.Application.SubscriptionPlans;
using FashionSaaS.Application.Subscriptions;
using FashionSaaS.Application.Tenants;
using FashionSaaS.Application.Users;
using FashionSaaS.Infrastructure.EventHandlers;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace FashionSaaS.API.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<AuthService>();
        services.AddScoped<MfaService>();
        services.AddScoped<TenantService>();
        services.AddScoped<UserService>();
        services.AddScoped<SubscriptionPlanService>();
        services.AddScoped<SubscriptionService>();
        services.AddScoped<BankAccountService>();
        services.AddScoped<CategoryService>();
        services.AddScoped<AuditLogQueryService>();
        services.AddScoped<LoginAttemptService>();
        services.AddScoped<ISuperAdminIpGuardService, SuperAdminIpGuardService>();
        return services;
    }

    public static IServiceCollection AddJwtAuthentication(this IServiceCollection services,
        IConfiguration configuration)
    {
        var jwtSettings = configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>()
            ?? throw new InvalidOperationException("JwtSettings section is missing from configuration.");
        if (string.IsNullOrEmpty(jwtSettings.Secret))
            throw new InvalidOperationException("JwtSettings:Secret is not set.");

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Secret)),
                    ValidateIssuer = true,
                    ValidIssuer = jwtSettings.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwtSettings.Audience,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero
                };
            });

        return services;
    }

    public static IServiceCollection AddRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            // Public endpoints: 10 req/min per client IP (fixed window, partitioned)
            options.AddPolicy("PublicPolicy", httpContext =>
            {
                var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                return RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 10,
                    Window = TimeSpan.FromMinutes(1),
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = 0
                });
            });

            // Authenticated endpoints: 300 req/min per TenantId (sliding window, partitioned)
            options.AddPolicy("AuthenticatedPolicy", httpContext =>
            {
                var tenantId = httpContext.User.FindFirst("tenant_id")?.Value;
                var key = string.IsNullOrEmpty(tenantId)
                    ? httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"
                    : tenantId;
                return RateLimitPartition.GetSlidingWindowLimiter(key, _ => new SlidingWindowRateLimiterOptions
                {
                    PermitLimit = 300,
                    Window = TimeSpan.FromMinutes(1),
                    SegmentsPerWindow = 6,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = 0
                });
            });

            // Super Admin: 60 req/min per UserId (token bucket, partitioned)
            options.AddPolicy("SuperAdminPolicy", httpContext =>
            {
                var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                    ?? httpContext.User.FindFirst("sub")?.Value;
                var key = string.IsNullOrEmpty(userId)
                    ? httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"
                    : userId;
                return RateLimitPartition.GetTokenBucketLimiter(key, _ => new TokenBucketRateLimiterOptions
                {
                    TokenLimit = 60,
                    ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                    TokensPerPeriod = 60,
                    AutoReplenishment = true,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = 0
                });
            });

            options.RejectionStatusCode = 429;
        });

        return services;
    }

    public static IServiceCollection AddMediatRWithBehaviors(this IServiceCollection services)
    {
        services.AddMediatR(cfg =>
        {
            // Application layer: AuthService, behaviours, etc.
            cfg.RegisterServicesFromAssembly(typeof(AuthService).Assembly);
            // Infrastructure layer: domain event handlers (e.g. SuperAdminLoginFromNewIpEventHandler)
            cfg.RegisterServicesFromAssembly(typeof(SuperAdminLoginFromNewIpEventHandler).Assembly);
            cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
            cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
        });
        return services;
    }
}
