using System.Text;
using System.Threading.RateLimiting;
using FashionSaaS.Application.AuditLogs;
using FashionSaaS.Application.Auth;
using FashionSaaS.Application.Behaviors;
using FashionSaaS.Application.BankAccounts;
using FashionSaaS.Application.LoginAttempts;
using FashionSaaS.Application.Mfa;
using FashionSaaS.Application.SubscriptionPlans;
using FashionSaaS.Application.Subscriptions;
using FashionSaaS.Application.Tenants;
using FashionSaaS.Application.Users;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
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
        services.AddScoped<AuditLogQueryService>();
        services.AddScoped<LoginAttemptService>();
        return services;
    }

    public static IServiceCollection AddJwtAuthentication(this IServiceCollection services,
        IConfiguration configuration)
    {
        var secret = configuration["JwtSettings:Secret"]
            ?? throw new InvalidOperationException("JwtSettings:Secret is not set.");

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
                    ValidateIssuer = true,
                    ValidIssuer = configuration["JwtSettings:Issuer"],
                    ValidateAudience = true,
                    ValidAudience = configuration["JwtSettings:Audience"],
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
            // Public endpoints: 10 req/min per IP (fixed window)
            options.AddFixedWindowLimiter("PublicPolicy", cfg =>
            {
                cfg.PermitLimit = 10;
                cfg.Window = TimeSpan.FromMinutes(1);
                cfg.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                cfg.QueueLimit = 0;
            });

            // Authenticated endpoints: 300 req/min per TenantId (sliding window)
            options.AddSlidingWindowLimiter("AuthenticatedPolicy", cfg =>
            {
                cfg.PermitLimit = 300;
                cfg.Window = TimeSpan.FromMinutes(1);
                cfg.SegmentsPerWindow = 6;
                cfg.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                cfg.QueueLimit = 0;
            });

            // Super Admin: 60 req/min per UserId (token bucket)
            options.AddTokenBucketLimiter("SuperAdminPolicy", cfg =>
            {
                cfg.TokenLimit = 60;
                cfg.ReplenishmentPeriod = TimeSpan.FromMinutes(1);
                cfg.TokensPerPeriod = 60;
                cfg.AutoReplenishment = true;
            });

            options.RejectionStatusCode = 429;
        });

        return services;
    }

    public static IServiceCollection AddMediatRWithBehaviors(this IServiceCollection services)
    {
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(typeof(AuthService).Assembly);
            cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
            cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
        });
        return services;
    }
}
