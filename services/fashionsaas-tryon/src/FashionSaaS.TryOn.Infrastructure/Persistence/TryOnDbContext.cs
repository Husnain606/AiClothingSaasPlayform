using FashionSaaS.TryOn.Domain;
using Microsoft.EntityFrameworkCore;

namespace FashionSaaS.TryOn.Infrastructure.Persistence;

public class TryOnDbContext(DbContextOptions<TryOnDbContext> options) : DbContext(options)
{
    public DbSet<TryOnRequest> TryOnRequests => Set<TryOnRequest>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TryOnDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
