using FashionSaaS.Application.Interfaces;
using FashionSaaS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FashionSaaS.Infrastructure.Persistence.Repositories;

public class GenericRepository<T>(ApplicationDbContext context) : IGenericRepository<T> where T : BaseEntity
{
    protected readonly DbSet<T> DbSet = context.Set<T>();
    protected readonly ApplicationDbContext Context = context;

    public async Task<T?> GetByIdAsync(Guid id) => await DbSet.FindAsync(id);

    public async Task<IReadOnlyList<T>> GetAllAsync() => await DbSet.ToListAsync();

    public async Task<IReadOnlyList<T>> FindAsync(ISpecification<T> spec)
        => await SpecificationEvaluator<T>.GetQuery(DbSet.AsQueryable(), spec).ToListAsync();

    public async Task<int> CountAsync(ISpecification<T> spec)
        => await SpecificationEvaluator<T>.GetQuery(DbSet.AsQueryable(), spec).CountAsync();

    public async Task AddAsync(T entity)
    {
        entity.CreatedAt = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;
        await DbSet.AddAsync(entity);
    }

    public Task UpdateAsync(T entity)
    {
        entity.UpdatedAt = DateTime.UtcNow;

        // If a different instance with the same key is already tracked (e.g. the caller
        // mutated a value fetched via an AsNoTracking query), copy the values onto the
        // tracked instance instead of attaching — attaching a second instance with the
        // same key throws.
        var tracked = Context.ChangeTracker.Entries<T>()
            .FirstOrDefault(e => e.Entity.Id == entity.Id && !ReferenceEquals(e.Entity, entity));

        if (tracked is not null)
            tracked.CurrentValues.SetValues(entity);
        else
            Context.Entry(entity).State = EntityState.Modified;

        return Task.CompletedTask;
    }

    public Task DeleteAsync(T entity)
    {
        DbSet.Remove(entity);
        return Task.CompletedTask;
    }
}
