using FashionSaaS.Application.Categories;
using FashionSaaS.Application.Categories.DTOs;
using FashionSaaS.Application.Common;
using FashionSaaS.Application.Interfaces;
using FashionSaaS.Domain.Entities;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FashionSaaS.Application.Tests.Categories;

public class CategoryServiceTests
{
    private readonly Mock<ICategoryRepository> _repo = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IAuditLogService> _audit = new();
    private readonly Mock<ICurrentTenantService> _tenant = new();
    private readonly Guid _tenantId = Guid.NewGuid();

    public CategoryServiceTests()
    {
        _tenant.SetupGet(t => t.TenantId).Returns(_tenantId);
    }

    private CategoryService CreateService() =>
        new(_repo.Object, _uow.Object, _audit.Object, _tenant.Object, NullLogger<CategoryService>.Instance);

    private Category Cat(Guid id, Guid? parent = null, int sort = 0) => new()
    {
        Id = id,
        TenantId = _tenantId,
        Name = "C",
        Slug = "c",
        ParentCategoryId = parent,
        SortOrder = sort
    };

    // ── Create ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_NewSlug_ReturnsCreated()
    {
        _repo.Setup(r => r.SlugExistsAsync(_tenantId, "shoes", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        ResponseData<CategoryResponse> result = await CreateService().CreateAsync(
            new CreateCategoryRequest { Name = "Shoes", Slug = "shoes" },
            Guid.NewGuid(), "127.0.0.1", "ua");

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(201);
        _repo.Verify(r => r.AddAsync(It.IsAny<Category>()), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_DuplicateSlug_Returns409()
    {
        _repo.Setup(r => r.SlugExistsAsync(_tenantId, "shoes", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        ResponseData<CategoryResponse> result = await CreateService().CreateAsync(
            new CreateCategoryRequest { Name = "Shoes", Slug = "shoes" },
            Guid.NewGuid(), "127.0.0.1", "ua");

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
        _repo.Verify(r => r.AddAsync(It.IsAny<Category>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_ParentFromAnotherTenant_Returns404()
    {
        var parentId = Guid.NewGuid();
        _repo.Setup(r => r.SlugExistsAsync(_tenantId, "shoes", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _repo.Setup(r => r.GetByIdAsync(parentId))
            .ReturnsAsync(new Category { Id = parentId, TenantId = Guid.NewGuid() }); // different tenant

        ResponseData<CategoryResponse> result = await CreateService().CreateAsync(
            new CreateCategoryRequest { Name = "Shoes", Slug = "shoes", ParentCategoryId = parentId },
            Guid.NewGuid(), "127.0.0.1", "ua");

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    // ── Update ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_SlugConflictExcludingSelf_Returns409()
    {
        var id = Guid.NewGuid();
        _repo.Setup(r => r.GetByIdAsync(id)).ReturnsAsync(Cat(id));
        _repo.Setup(r => r.SlugExistsAsync(_tenantId, "taken", id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        ResponseData<CategoryResponse> result = await CreateService().UpdateAsync(id,
            new UpdateCategoryRequest { Name = "X", Slug = "taken" },
            Guid.NewGuid(), "127.0.0.1", "ua");

        result.StatusCode.Should().Be(409);
    }

    // ── Move (cycle prevention) ─────────────────────────────────────────────────

    [Fact]
    public async Task MoveAsync_UnderSelf_Returns400()
    {
        var id = Guid.NewGuid();
        _repo.Setup(r => r.GetByIdAsync(id)).ReturnsAsync(Cat(id));

        ResponseData<CategoryResponse> result = await CreateService().MoveAsync(
            id, new MoveCategoryRequest { NewParentId = id },
            Guid.NewGuid(), "127.0.0.1", "ua");

        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task MoveAsync_UnderOwnDescendant_Returns400Cycle()
    {
        // root -> child -> grandchild. Move root under grandchild => cycle.
        var root = Guid.NewGuid();
        var child = Guid.NewGuid();
        var grandchild = Guid.NewGuid();

        _repo.Setup(r => r.GetByIdAsync(root)).ReturnsAsync(Cat(root));
        _repo.Setup(r => r.GetByIdAsync(grandchild)).ReturnsAsync(Cat(grandchild, child));
        _repo.Setup(r => r.GetTreeAsync(_tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Category> { Cat(root), Cat(child, root), Cat(grandchild, child) });

        ResponseData<CategoryResponse> result = await CreateService().MoveAsync(
            root, new MoveCategoryRequest { NewParentId = grandchild },
            Guid.NewGuid(), "127.0.0.1", "ua");

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        _repo.Verify(r => r.UpdateAsync(It.IsAny<Category>()), Times.Never);
    }

    [Fact]
    public async Task MoveAsync_ValidNewParent_Succeeds()
    {
        var node = Guid.NewGuid();
        var newParent = Guid.NewGuid();

        _repo.Setup(r => r.GetByIdAsync(node)).ReturnsAsync(Cat(node));
        _repo.Setup(r => r.GetByIdAsync(newParent)).ReturnsAsync(Cat(newParent));
        _repo.Setup(r => r.GetTreeAsync(_tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Category> { Cat(node), Cat(newParent) });

        ResponseData<CategoryResponse> result = await CreateService().MoveAsync(
            node, new MoveCategoryRequest { NewParentId = newParent },
            Guid.NewGuid(), "127.0.0.1", "ua");

        result.IsSuccess.Should().BeTrue();
        _repo.Verify(r => r.UpdateAsync(It.Is<Category>(c => c.ParentCategoryId == newParent)), Times.Once);
    }

    // ── Delete ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_WithChildren_Returns409()
    {
        var id = Guid.NewGuid();
        _repo.Setup(r => r.GetByIdAsync(id)).ReturnsAsync(Cat(id));
        _repo.Setup(r => r.HasChildrenAsync(_tenantId, id, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        ResponseData<bool> result = await CreateService().DeleteAsync(id, Guid.NewGuid(), "127.0.0.1", "ua");

        result.StatusCode.Should().Be(409);
        _repo.Verify(r => r.DeleteAsync(It.IsAny<Category>()), Times.Never);
    }

    [Fact]
    public async Task DeleteAsync_WithProducts_Returns409()
    {
        var id = Guid.NewGuid();
        _repo.Setup(r => r.GetByIdAsync(id)).ReturnsAsync(Cat(id));
        _repo.Setup(r => r.HasChildrenAsync(_tenantId, id, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _repo.Setup(r => r.HasProductsAsync(_tenantId, id, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        ResponseData<bool> result = await CreateService().DeleteAsync(id, Guid.NewGuid(), "127.0.0.1", "ua");

        result.StatusCode.Should().Be(409);
        _repo.Verify(r => r.DeleteAsync(It.IsAny<Category>()), Times.Never);
    }

    [Fact]
    public async Task DeleteAsync_LeafNoProducts_Succeeds()
    {
        var id = Guid.NewGuid();
        _repo.Setup(r => r.GetByIdAsync(id)).ReturnsAsync(Cat(id));
        _repo.Setup(r => r.HasChildrenAsync(_tenantId, id, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _repo.Setup(r => r.HasProductsAsync(_tenantId, id, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        ResponseData<bool> result = await CreateService().DeleteAsync(id, Guid.NewGuid(), "127.0.0.1", "ua");

        result.IsSuccess.Should().BeTrue();
        _repo.Verify(r => r.DeleteAsync(It.IsAny<Category>()), Times.Once);
    }

    // ── GetTree (nesting) ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetTreeAsync_BuildsCorrectNesting()
    {
        var root = Guid.NewGuid();
        var childA = Guid.NewGuid();
        var childB = Guid.NewGuid();
        var grandchild = Guid.NewGuid();

        _repo.Setup(r => r.GetTreeAsync(_tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Category>
            {
                Cat(root),
                Cat(childA, root, sort: 0),
                Cat(childB, root, sort: 1),
                Cat(grandchild, childA)
            });

        ResponseData<IReadOnlyList<CategoryTreeNode>> result = await CreateService().GetTreeAsync();

        result.IsSuccess.Should().BeTrue();
        result.Data.Should().ContainSingle(n => n.Id == root);
        CategoryTreeNode rootNode = result.Data!.Single(n => n.Id == root);
        rootNode.Children.Should().HaveCount(2);
        rootNode.Children[0].Id.Should().Be(childA); // ordered by SortOrder
        rootNode.Children[0].Children.Should().ContainSingle(n => n.Id == grandchild);
        rootNode.Children[1].Id.Should().Be(childB);
    }

    [Fact]
    public async Task CreateAsync_NoTenant_Returns400()
    {
        _tenant.SetupGet(t => t.TenantId).Returns((Guid?)null);

        ResponseData<CategoryResponse> result = await CreateService().CreateAsync(
            new CreateCategoryRequest { Name = "X", Slug = "x" }, Guid.NewGuid(), "127.0.0.1", "ua");

        result.StatusCode.Should().Be(400);
    }
}
