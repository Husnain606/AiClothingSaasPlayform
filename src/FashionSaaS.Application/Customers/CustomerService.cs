using FashionSaaS.Application.Common;
using FashionSaaS.Application.Customers.DTOs;
using FashionSaaS.Application.Interfaces;
using FashionSaaS.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace FashionSaaS.Application.Customers;

/// <summary>
/// Customer management. Input-shape validation (required fields, lengths, email format)
/// is handled by FluentValidation at the API boundary (CONVENTIONS §8); this service only
/// enforces business rules: email uniqueness per tenant, tenant scoping, and soft
/// deactivation. No authentication/password handling — that arrives in Phase 3.
/// </summary>
public class CustomerService(
    ICustomerRepository customerRepository,
    IUnitOfWork unitOfWork,
    IAuditLogService auditLogService,
    ICurrentTenantService currentTenant,
    ILogger<CustomerService> logger)
{
    public async Task<ResponseData<CustomerResponse>> CreateAsync(CreateCustomerRequest request,
        Guid createdByUserId, string ipAddress, string userAgent, CancellationToken ct = default)
    {
        if (currentTenant.TenantId is not { } tenantId)
            return ResponseData<CustomerResponse>.Failure("Tenant could not be resolved.", 400);

        if (await customerRepository.EmailExistsAsync(tenantId, request.Email, null, ct))
            return ResponseData<CustomerResponse>.Failure($"Email '{request.Email}' is already in use.", 409);

        var customer = new Customer
        {
            TenantId = tenantId,
            FirstName = request.FirstName,
            LastName = request.LastName,
            Email = request.Email,
            Phone = request.Phone,
            IsActive = true
        };

        await customerRepository.AddAsync(customer);
        await unitOfWork.SaveChangesAsync(ct);

        await auditLogService.LogAsync(createdByUserId, tenantId, "CustomerCreated", "Customer", customer.Id,
            null, new { customer.FirstName, customer.LastName, customer.Email }, ipAddress, userAgent);

        logger.LogInformation("Customer {CustomerId} created for tenant {TenantId}", customer.Id, tenantId);

        return ResponseData<CustomerResponse>.Success(MapToResponse(customer), "Customer created.", 201);
    }

    public async Task<ResponseData<CustomerResponse>> UpdateAsync(Guid id, UpdateCustomerRequest request,
        Guid updatedByUserId, string ipAddress, string userAgent, CancellationToken ct = default)
    {
        if (currentTenant.TenantId is not { } tenantId)
            return ResponseData<CustomerResponse>.Failure("Tenant could not be resolved.", 400);

        var customer = await customerRepository.GetByIdAsync(id);
        if (customer is null || customer.TenantId != tenantId)
            return ResponseData<CustomerResponse>.Failure("Customer not found.", 404);

        if (await customerRepository.EmailExistsAsync(tenantId, request.Email, id, ct))
            return ResponseData<CustomerResponse>.Failure($"Email '{request.Email}' is already in use.", 409);

        var old = new { customer.FirstName, customer.LastName, customer.Email, customer.Phone };
        customer.FirstName = request.FirstName;
        customer.LastName = request.LastName;
        customer.Email = request.Email;
        customer.Phone = request.Phone;

        await customerRepository.UpdateAsync(customer);
        await unitOfWork.SaveChangesAsync(ct);

        await auditLogService.LogAsync(updatedByUserId, tenantId, "CustomerUpdated", "Customer", customer.Id,
            old, new { customer.FirstName, customer.LastName, customer.Email, customer.Phone }, ipAddress, userAgent);

        logger.LogInformation("Customer {CustomerId} updated for tenant {TenantId}", customer.Id, tenantId);

        return ResponseData<CustomerResponse>.Success(MapToResponse(customer), "Customer updated.");
    }

    public async Task<ResponseData<bool>> DeactivateAsync(Guid id,
        Guid deactivatedByUserId, string ipAddress, string userAgent, CancellationToken ct = default)
    {
        if (currentTenant.TenantId is not { } tenantId)
            return ResponseData<bool>.Failure("Tenant could not be resolved.", 400);

        var customer = await customerRepository.GetByIdAsync(id);
        if (customer is null || customer.TenantId != tenantId)
            return ResponseData<bool>.Failure("Customer not found.", 404);

        var previous = customer.IsActive;
        customer.IsActive = false;

        await customerRepository.UpdateAsync(customer);
        await unitOfWork.SaveChangesAsync(ct);

        await auditLogService.LogAsync(deactivatedByUserId, tenantId, "CustomerDeactivated", "Customer", customer.Id,
            new { IsActive = previous }, new { IsActive = false }, ipAddress, userAgent);

        logger.LogInformation("Customer {CustomerId} deactivated for tenant {TenantId}", customer.Id, tenantId);
        return ResponseData<bool>.Success(true, "Customer deactivated.");
    }

    public async Task<ResponseData<PagedResult<CustomerResponse>>> GetAllAsync(CustomerFilter filter,
        CancellationToken ct = default)
    {
        if (currentTenant.TenantId is not { } tenantId)
            return ResponseData<PagedResult<CustomerResponse>>.Failure("Tenant could not be resolved.", 400);

        // Enforce tenant scope regardless of the inbound filter value.
        filter.TenantId = tenantId;

        var (items, total) = await customerRepository.GetPagedAsync(filter, ct);

        var page = new PagedResult<CustomerResponse>
        {
            Items = items.Select(MapToResponse).ToList(),
            TotalCount = total,
            Page = filter.Page,
            PageSize = filter.PageSize
        };

        return ResponseData<PagedResult<CustomerResponse>>.Success(page);
    }

    public async Task<ResponseData<CustomerResponse>> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        if (currentTenant.TenantId is not { } tenantId)
            return ResponseData<CustomerResponse>.Failure("Tenant could not be resolved.", 400);

        var customer = await customerRepository.GetByIdAsync(id);
        if (customer is null || customer.TenantId != tenantId)
            return ResponseData<CustomerResponse>.Failure("Customer not found.", 404);

        return ResponseData<CustomerResponse>.Success(MapToResponse(customer));
    }

    private static CustomerResponse MapToResponse(Customer c) => new()
    {
        Id = c.Id,
        FirstName = c.FirstName,
        LastName = c.LastName,
        Email = c.Email,
        Phone = c.Phone,
        IsActive = c.IsActive,
        CreatedAt = c.CreatedAt
    };
}
