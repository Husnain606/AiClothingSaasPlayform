using FashionSaaS.Application.Interfaces;
using FashionSaaS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FashionSaaS.Infrastructure.Persistence.Repositories;

public class TenantRepository(ApplicationDbContext context)
    : GenericRepository<Tenant>(context), ITenantRepository
{
    public async Task<Tenant?> GetBySlugAsync(string slug)
        => await DbSet.FirstOrDefaultAsync(t => t.Slug == slug);

    public async Task<bool> SlugExistsAsync(string slug)
        => await DbSet.AnyAsync(t => t.Slug == slug);

    public async Task<bool> EmailExistsAsync(string email)
        => await DbSet.AnyAsync(t => t.Email == email);
}
