using FashionSaaS.Domain.Entities;

namespace FashionSaaS.Application.Interfaces;

public interface ITenantRepository : IGenericRepository<Tenant>
{
    Task<Tenant?> GetBySlugAsync(string slug);
    Task<bool> SlugExistsAsync(string slug);
    Task<bool> EmailExistsAsync(string email);
}
