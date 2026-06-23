using FashionSaaS.Application.Common;
using FashionSaaS.Application.Interfaces;
using FashionSaaS.Application.Tenants.DTOs;
using FashionSaaS.Domain.Entities;
using FashionSaaS.Domain.Events;

namespace FashionSaaS.Application.Tenants;

public class TenantService(
    ITenantRepository tenantRepository,
    IUnitOfWork unitOfWork,
    IAuditLogService auditLogService,
    IEmailService emailService)
{
    public async Task<ResponseData<TenantResponse>> CreateAsync(CreateTenantRequest request,
        Guid createdByUserId, string ipAddress, string userAgent)
    {
        if (await tenantRepository.SlugExistsAsync(request.Slug))
            return ResponseData<TenantResponse>.Failure($"Slug '{request.Slug}' is already taken.", 409);

        if (await tenantRepository.EmailExistsAsync(request.Email))
            return ResponseData<TenantResponse>.Failure("A tenant with this email already exists.", 409);

        var tenant = new Tenant
        {
            Name = request.Name,
            Slug = request.Slug,
            Email = request.Email,
            Phone = request.Phone,
            LogoUrl = request.LogoUrl,
            CoverImageUrl = request.CoverImageUrl,
            IsActive = true
        };

        tenant.AddDomainEvent(new TenantCreatedEvent(tenant.Id, tenant.Name, tenant.Email));
        await tenantRepository.AddAsync(tenant);
        await unitOfWork.SaveChangesAsync();

        await auditLogService.LogAsync(createdByUserId, null, "TenantCreated", "Tenant", tenant.Id,
            null, new { tenant.Name, tenant.Slug, tenant.Email }, ipAddress, userAgent);

        return ResponseData<TenantResponse>.Success(MapToResponse(tenant), "Tenant created.", 201);
    }

    public async Task<ResponseData<TenantResponse>> UpdateAsync(Guid id, UpdateTenantRequest request,
        Guid updatedByUserId, string ipAddress, string userAgent)
    {
        var tenant = await tenantRepository.GetByIdAsync(id);
        if (tenant is null)
            return ResponseData<TenantResponse>.Failure("Tenant not found.", 404);

        var old = new { tenant.Name, tenant.Phone, tenant.LogoUrl };
        tenant.Name = request.Name;
        tenant.Phone = request.Phone;
        tenant.LogoUrl = request.LogoUrl;
        tenant.CoverImageUrl = request.CoverImageUrl;

        await tenantRepository.UpdateAsync(tenant);
        await unitOfWork.SaveChangesAsync();

        await auditLogService.LogAsync(updatedByUserId, null, "TenantUpdated", "Tenant", tenant.Id,
            old, new { tenant.Name, tenant.Phone, tenant.LogoUrl }, ipAddress, userAgent);

        return ResponseData<TenantResponse>.Success(MapToResponse(tenant));
    }

    public async Task<ResponseData<TenantResponse>> GetByIdAsync(Guid id)
    {
        var tenant = await tenantRepository.GetByIdAsync(id);
        if (tenant is null)
            return ResponseData<TenantResponse>.Failure("Tenant not found.", 404);
        return ResponseData<TenantResponse>.Success(MapToResponse(tenant));
    }

    public async Task<ResponseData<PagedResult<TenantResponse>>> GetAllAsync(TenantFilterRequest filter)
    {
        var tenants = await tenantRepository.GetAllAsync();
        var filtered = tenants.AsEnumerable();
        if (!string.IsNullOrEmpty(filter.Search))
            filtered = filtered.Where(t => t.Name.Contains(filter.Search, StringComparison.OrdinalIgnoreCase)
                || t.Slug.Contains(filter.Search, StringComparison.OrdinalIgnoreCase));
        if (filter.IsActive.HasValue)
            filtered = filtered.Where(t => t.IsActive == filter.IsActive.Value);

        var list = filtered.ToList();
        var paged = new PagedResult<TenantResponse>
        {
            Items = list.Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize)
                .Select(MapToResponse).ToList(),
            TotalCount = list.Count,
            Page = filter.Page,
            PageSize = filter.PageSize
        };
        return ResponseData<PagedResult<TenantResponse>>.Success(paged);
    }

    public async Task<ResponseData<bool>> SuspendAsync(Guid id, Guid adminUserId,
        string ipAddress, string userAgent)
    {
        var tenant = await tenantRepository.GetByIdAsync(id);
        if (tenant is null)
            return ResponseData<bool>.Failure("Tenant not found.", 404);

        tenant.IsActive = false;
        tenant.AddDomainEvent(new TenantSuspendedEvent(tenant.Id, tenant.Email));
        await tenantRepository.UpdateAsync(tenant);
        await unitOfWork.SaveChangesAsync();

        await auditLogService.LogAsync(adminUserId, null, "TenantSuspended", "Tenant", tenant.Id,
            new { WasActive = true }, new { IsActive = false }, ipAddress, userAgent);
        await emailService.SendTenantSuspendedAsync(tenant.Email, "Administrative action");

        return ResponseData<bool>.Success(true, "Tenant suspended.");
    }

    public async Task<ResponseData<bool>> ActivateAsync(Guid id, Guid adminUserId,
        string ipAddress, string userAgent)
    {
        var tenant = await tenantRepository.GetByIdAsync(id);
        if (tenant is null)
            return ResponseData<bool>.Failure("Tenant not found.", 404);

        tenant.IsActive = true;
        tenant.AddDomainEvent(new TenantActivatedEvent(tenant.Id, tenant.Email));
        await tenantRepository.UpdateAsync(tenant);
        await unitOfWork.SaveChangesAsync();

        await auditLogService.LogAsync(adminUserId, null, "TenantActivated", "Tenant", tenant.Id,
            new { WasActive = false }, new { IsActive = true }, ipAddress, userAgent);

        return ResponseData<bool>.Success(true, "Tenant activated.");
    }

    public async Task<ResponseData<bool>> DeleteAsync(Guid id, Guid adminUserId,
        string ipAddress, string userAgent)
    {
        var tenant = await tenantRepository.GetByIdAsync(id);
        if (tenant is null)
            return ResponseData<bool>.Failure("Tenant not found.", 404);

        await tenantRepository.DeleteAsync(tenant);
        await unitOfWork.SaveChangesAsync();

        await auditLogService.LogAsync(adminUserId, null, "TenantDeleted", "Tenant", tenant.Id,
            new { tenant.Name, tenant.Slug }, null, ipAddress, userAgent);

        return ResponseData<bool>.Success(true, "Tenant deleted.");
    }

    private static TenantResponse MapToResponse(Tenant t) => new()
    {
        Id = t.Id, Name = t.Name, Slug = t.Slug, Email = t.Email,
        Phone = t.Phone, LogoUrl = t.LogoUrl, IsActive = t.IsActive, CreatedAt = t.CreatedAt
    };
}
