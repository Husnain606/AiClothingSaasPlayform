using FashionSaaS.TryOn.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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

        return services;
    }
}
