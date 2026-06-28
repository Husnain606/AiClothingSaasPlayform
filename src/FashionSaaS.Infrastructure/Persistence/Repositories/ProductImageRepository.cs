using FashionSaaS.Application.Interfaces;
using FashionSaaS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FashionSaaS.Infrastructure.Persistence.Repositories;

public class ProductImageRepository(ApplicationDbContext context)
    : GenericRepository<ProductImage>(context), IProductImageRepository
{
    public async Task<IReadOnlyList<ProductImage>> GetByProductAsync(Guid productId, CancellationToken ct = default)
        => await DbSet
            .AsNoTracking()
            .Where(i => i.ProductId == productId)
            .OrderBy(i => i.SortOrder)
            .ToListAsync(ct);

    public async Task<ProductImage?> GetPrimaryAsync(Guid productId, CancellationToken ct = default)
        => await DbSet
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.ProductId == productId && i.IsPrimary, ct);
}
