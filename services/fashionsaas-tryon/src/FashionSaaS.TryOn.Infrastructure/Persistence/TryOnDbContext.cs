using FashionSaaS.TryOn.Domain;
using Microsoft.EntityFrameworkCore;

namespace FashionSaaS.TryOn.Infrastructure.Persistence;

public class TryOnDbContext(DbContextOptions<TryOnDbContext> options) : DbContext(options)
{
    public DbSet<TryOnRequest> TryOnRequests => Set<TryOnRequest>();

    public DbSet<MeasurementRequest> MeasurementRequests => Set<MeasurementRequest>();

    public DbSet<ChatRequest> ChatRequests => Set<ChatRequest>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TryOnDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
