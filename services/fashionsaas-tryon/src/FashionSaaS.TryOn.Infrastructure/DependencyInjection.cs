using System.Text;
using Azure.Messaging.ServiceBus;
using FashionSaaS.TryOn.Application;
using FashionSaaS.TryOn.Application.Messaging;
using FashionSaaS.TryOn.Application.Quota;
using FashionSaaS.TryOn.Infrastructure.Messaging;
using FashionSaaS.TryOn.Infrastructure.Persistence;
using FashionSaaS.TryOn.Infrastructure.Quota;
using FashionSaaS.TryOn.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace FashionSaaS.TryOn.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddTryOnInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<TryOnDbContext>(options =>
            options.UseSqlServer(
                configuration.GetConnectionString("TryOnConnection")
                ?? throw new InvalidOperationException("ConnectionStrings:TryOnConnection not set."),
                b => b.MigrationsAssembly(typeof(TryOnDbContext).Assembly.FullName)));

        services.AddScoped<TryOn.TryOnService>();
        services.AddScoped<IUsageQuotaService, UsageQuotaService>();

        services.AddOptions<ServiceBusSettings>()
            .Bind(configuration.GetSection(ServiceBusSettings.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton(sp =>
            new ServiceBusClient(sp.GetRequiredService<IOptions<ServiceBusSettings>>().Value.ConnectionString));
        services.AddScoped<ITryOnEventPublisher, ServiceBusTryOnEventPublisher>();

        return services;
    }


    public static IServiceCollection AddTryOnAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        JwtSettings jwtSettings = configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>()
            ?? throw new InvalidOperationException("JwtSettings section is missing from configuration.");
        if (string.IsNullOrEmpty(jwtSettings.Secret))
            throw new InvalidOperationException("JwtSettings:Secret is not set.");

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentTryOnContext, CurrentTryOnContext>();

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
}
