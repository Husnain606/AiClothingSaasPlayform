using FashionSaaS.Domain.Entities;

namespace FashionSaaS.Application.Interfaces;

public interface IGenericRepository<T> where T : BaseEntity
{
    Task<T?> GetByIdAsync(Guid id);
    Task<IReadOnlyList<T>> GetAllAsync();
    Task<IReadOnlyList<T>> FindAsync(ISpecification<T> spec);
    Task<int> CountAsync(ISpecification<T> spec);
    Task AddAsync(T entity);
    Task UpdateAsync(T entity);
    Task DeleteAsync(T entity);
}
