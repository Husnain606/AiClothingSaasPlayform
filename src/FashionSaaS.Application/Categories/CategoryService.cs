using FashionSaaS.Application.Categories.DTOs;
using FashionSaaS.Application.Common;
using FashionSaaS.Application.Interfaces;
using FashionSaaS.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace FashionSaaS.Application.Categories;

/// <summary>
/// Hierarchical category management. Input-shape validation is handled by
/// FluentValidation at the API boundary (CONVENTIONS §8); this service only
/// enforces business rules: slug uniqueness, parent existence/tenant scoping,
/// cycle prevention on move, and delete-blocking when children/products exist.
/// </summary>
public class CategoryService(
    ICategoryRepository categoryRepository,
    IUnitOfWork unitOfWork,
    IAuditLogService auditLogService,
    ICurrentTenantService currentTenant,
    ILogger<CategoryService> logger)
{
    public async Task<ResponseData<CategoryResponse>> CreateAsync(CreateCategoryRequest request,
        Guid createdByUserId, string ipAddress, string userAgent, CancellationToken ct = default)
    {
        if (currentTenant.TenantId is not { } tenantId)
            return ResponseData<CategoryResponse>.Failure("Tenant could not be resolved.", 400);

        if (await categoryRepository.SlugExistsAsync(tenantId, request.Slug, null, ct))
            return ResponseData<CategoryResponse>.Failure($"Slug '{request.Slug}' is already in use.", 409);

        if (request.ParentCategoryId is { } parentId)
        {
            Category? parent = await categoryRepository.GetByIdAsync(parentId);
            if (parent is null || parent.TenantId != tenantId)
                return ResponseData<CategoryResponse>.Failure("Parent category not found.", 404);
        }

        var category = new Category
        {
            TenantId = tenantId,
            Name = request.Name,
            Slug = request.Slug,
            Description = request.Description,
            ParentCategoryId = request.ParentCategoryId,
            SortOrder = request.SortOrder,
            IsActive = true
        };

        await categoryRepository.AddAsync(category);
        await unitOfWork.SaveChangesAsync(ct);

        await auditLogService.LogAsync(createdByUserId, tenantId, "CategoryCreated", "Category", category.Id,
            null, new { category.Name, category.Slug, category.ParentCategoryId }, ipAddress, userAgent);

        logger.LogInformation("Category {CategoryId} created for tenant {TenantId}", category.Id, tenantId);
        return ResponseData<CategoryResponse>.Success(MapToResponse(category), "Category created.", 201);
    }

    public async Task<ResponseData<CategoryResponse>> UpdateAsync(Guid id, UpdateCategoryRequest request,
        Guid updatedByUserId, string ipAddress, string userAgent, CancellationToken ct = default)
    {
        if (currentTenant.TenantId is not { } tenantId)
            return ResponseData<CategoryResponse>.Failure("Tenant could not be resolved.", 400);

        Category? category = await categoryRepository.GetByIdAsync(id);
        if (category is null || category.TenantId != tenantId)
            return ResponseData<CategoryResponse>.Failure("Category not found.", 404);

        if (await categoryRepository.SlugExistsAsync(tenantId, request.Slug, id, ct))
            return ResponseData<CategoryResponse>.Failure($"Slug '{request.Slug}' is already in use.", 409);

        var old = new { category.Name, category.Slug, category.Description, category.SortOrder, category.IsActive };
        category.Name = request.Name;
        category.Slug = request.Slug;
        category.Description = request.Description;
        category.SortOrder = request.SortOrder;
        category.IsActive = request.IsActive;

        await categoryRepository.UpdateAsync(category);
        await unitOfWork.SaveChangesAsync(ct);

        await auditLogService.LogAsync(updatedByUserId, tenantId, "CategoryUpdated", "Category", category.Id,
            old, new { category.Name, category.Slug, category.Description, category.SortOrder, category.IsActive },
            ipAddress, userAgent);

        logger.LogInformation("Category {CategoryId} updated for tenant {TenantId}", category.Id, tenantId);
        return ResponseData<CategoryResponse>.Success(MapToResponse(category), "Category updated.");
    }

    public async Task<ResponseData<CategoryResponse>> MoveAsync(Guid id, MoveCategoryRequest request,
        Guid movedByUserId, string ipAddress, string userAgent, CancellationToken ct = default)
    {
        if (currentTenant.TenantId is not { } tenantId)
            return ResponseData<CategoryResponse>.Failure("Tenant could not be resolved.", 400);

        Category? category = await categoryRepository.GetByIdAsync(id);
        if (category is null || category.TenantId != tenantId)
            return ResponseData<CategoryResponse>.Failure("Category not found.", 404);

        if (request.NewParentId is { } newParentId)
        {
            if (newParentId == category.Id)
                return ResponseData<CategoryResponse>.Failure("A category cannot be moved under itself.", 400);

            Category? newParent = await categoryRepository.GetByIdAsync(newParentId);
            if (newParent is null || newParent.TenantId != tenantId)
                return ResponseData<CategoryResponse>.Failure("New parent category not found.", 404);

            // Cycle prevention: the new parent must not be a descendant of the moved node.
            IReadOnlyList<Category> tree = await categoryRepository.GetTreeAsync(tenantId, ct);
            if (IsDescendant(tree, category.Id, newParentId))
            {
                return ResponseData<CategoryResponse>.Failure(
                    "Cannot move a category under one of its own descendants (would create a cycle).", 400);
            }
        }

        Guid? oldParentId = category.ParentCategoryId;
        category.ParentCategoryId = request.NewParentId;

        await categoryRepository.UpdateAsync(category);
        await unitOfWork.SaveChangesAsync(ct);

        await auditLogService.LogAsync(movedByUserId, tenantId, "CategoryMoved", "Category", category.Id,
            new { OldParentId = oldParentId }, new { NewParentId = request.NewParentId }, ipAddress, userAgent);

        logger.LogInformation("Category {CategoryId} moved to parent {NewParentId} for tenant {TenantId}",
            category.Id, request.NewParentId, tenantId);
        return ResponseData<CategoryResponse>.Success(MapToResponse(category), "Category moved.");
    }

    public async Task<ResponseData<bool>> ReorderAsync(ReorderCategoryRequest request,
        Guid reorderedByUserId, string ipAddress, string userAgent, CancellationToken ct = default)
    {
        if (currentTenant.TenantId is not { } tenantId)
            return ResponseData<bool>.Failure("Tenant could not be resolved.", 400);

        foreach (CategoryOrderItem item in request.Items)
        {
            Category? category = await categoryRepository.GetByIdAsync(item.Id);
            if (category is null || category.TenantId != tenantId)
                return ResponseData<bool>.Failure($"Category '{item.Id}' not found.", 404);

            category.SortOrder = item.SortOrder;
            await categoryRepository.UpdateAsync(category);
        }

        await unitOfWork.SaveChangesAsync(ct);

        await auditLogService.LogAsync(reorderedByUserId, tenantId, "CategoryReordered", "Category", tenantId,
            null, new { Count = request.Items.Count }, ipAddress, userAgent);

        logger.LogInformation("Reordered {Count} categories for tenant {TenantId}", request.Items.Count, tenantId);
        return ResponseData<bool>.Success(true, "Categories reordered.");
    }

    public async Task<ResponseData<bool>> DeleteAsync(Guid id,
        Guid deletedByUserId, string ipAddress, string userAgent, CancellationToken ct = default)
    {
        if (currentTenant.TenantId is not { } tenantId)
            return ResponseData<bool>.Failure("Tenant could not be resolved.", 400);

        Category? category = await categoryRepository.GetByIdAsync(id);
        if (category is null || category.TenantId != tenantId)
            return ResponseData<bool>.Failure("Category not found.", 404);

        // Block delete when the node has children or assigned products (no silent reparenting — spec §8).
        if (await categoryRepository.HasChildrenAsync(tenantId, id, ct))
        {
            return ResponseData<bool>.Failure(
                "Cannot delete a category that has child categories. Move or delete the children first.", 409);
        }

        if (await categoryRepository.HasProductsAsync(tenantId, id, ct))
        {
            return ResponseData<bool>.Failure(
                "Cannot delete a category that has assigned products. Reassign the products first.", 409);
        }

        await categoryRepository.DeleteAsync(category);
        await unitOfWork.SaveChangesAsync(ct);

        await auditLogService.LogAsync(deletedByUserId, tenantId, "CategoryDeleted", "Category", category.Id,
            new { category.Name, category.Slug }, null, ipAddress, userAgent);

        logger.LogInformation("Category {CategoryId} deleted for tenant {TenantId}", category.Id, tenantId);
        return ResponseData<bool>.Success(true, "Category deleted.");
    }

    public async Task<ResponseData<CategoryResponse>> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        if (currentTenant.TenantId is not { } tenantId)
            return ResponseData<CategoryResponse>.Failure("Tenant could not be resolved.", 400);

        Category? category = await categoryRepository.GetByIdAsync(id);
        if (category is null || category.TenantId != tenantId)
            return ResponseData<CategoryResponse>.Failure("Category not found.", 404);

        return ResponseData<CategoryResponse>.Success(MapToResponse(category));
    }

    public async Task<ResponseData<IReadOnlyList<CategoryResponse>>> GetAllAsync(CancellationToken ct = default)
    {
        if (currentTenant.TenantId is not { } tenantId)
            return ResponseData<IReadOnlyList<CategoryResponse>>.Failure("Tenant could not be resolved.", 400);

        IReadOnlyList<Category> categories = await categoryRepository.GetTreeAsync(tenantId, ct);
        IReadOnlyList<CategoryResponse> list = categories
            .OrderBy(c => c.SortOrder)
            .Select(MapToResponse)
            .ToList();

        return ResponseData<IReadOnlyList<CategoryResponse>>.Success(list);
    }

    public async Task<ResponseData<IReadOnlyList<CategoryTreeNode>>> GetTreeAsync(CancellationToken ct = default)
    {
        if (currentTenant.TenantId is not { } tenantId)
            return ResponseData<IReadOnlyList<CategoryTreeNode>>.Failure("Tenant could not be resolved.", 400);

        IReadOnlyList<Category> categories = await categoryRepository.GetTreeAsync(tenantId, ct);
        IReadOnlyList<CategoryTreeNode> tree = BuildTree(categories);
        return ResponseData<IReadOnlyList<CategoryTreeNode>>.Success(tree);
    }

    // ── Tree helpers ──────────────────────────────────────────────────────────

    private static IReadOnlyList<CategoryTreeNode> BuildTree(IReadOnlyList<Category> categories)
    {
        var childrenByParent = categories
            .Where(c => c.ParentCategoryId.HasValue)
            .GroupBy(c => c.ParentCategoryId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderBy(c => c.SortOrder).ToList());

        var roots = categories
            .Where(c => c.ParentCategoryId is null)
            .OrderBy(c => c.SortOrder)
            .ToList();

        IReadOnlyList<CategoryTreeNode> BuildChildren(IEnumerable<Category> nodes) =>
            nodes.Select(c => new CategoryTreeNode
            {
                Id = c.Id,
                Name = c.Name,
                Slug = c.Slug,
                SortOrder = c.SortOrder,
                Children = childrenByParent.TryGetValue(c.Id, out List<Category>? kids)
                    ? BuildChildren(kids)
                    : []
            }).ToList();

        return BuildChildren(roots);
    }

    /// <summary>True when <paramref name="candidateId"/> is the moved node or sits in its subtree.</summary>
    private static bool IsDescendant(IReadOnlyList<Category> categories, Guid rootId, Guid candidateId)
    {
        var childrenByParent = categories
            .Where(c => c.ParentCategoryId.HasValue)
            .GroupBy(c => c.ParentCategoryId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(c => c.Id).ToList());

        var stack = new Stack<Guid>();
        stack.Push(rootId);
        while (stack.Count > 0)
        {
            Guid current = stack.Pop();
            if (current == candidateId)
                return true;
            if (childrenByParent.TryGetValue(current, out List<Guid>? childIds))
            {
                foreach (Guid childId in childIds)
                    stack.Push(childId);
            }
        }
        return false;
    }

    private static CategoryResponse MapToResponse(Category c) => new()
    {
        Id = c.Id,
        Name = c.Name,
        Slug = c.Slug,
        Description = c.Description,
        ParentCategoryId = c.ParentCategoryId,
        SortOrder = c.SortOrder,
        IsActive = c.IsActive,
        CreatedAt = c.CreatedAt
    };
}
