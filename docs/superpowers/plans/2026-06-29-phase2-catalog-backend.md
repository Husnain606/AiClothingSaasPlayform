# Phase 2 Catalog Backend Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Complete Phase 2 product catalog backend implementation with full test coverage for repositories and catalog operations.

**Current Status:** Controllers, services, and migration exist. Repositories implemented. **Missing:** repository integration tests and complete entity configurations.

**Architecture:** Controller → Service → Repository → DbContext. Services enforce business rules; FluentValidation handles input shape at API boundary. Unit tests mock services; integration tests use in-memory EF DbContext.

**Tech Stack:** .NET 10, EF Core 10, Xunit, FluentAssertions, Moq, in-memory testing

## Global Constraints

- No changes to .NET 10 or ASP.NET Core 10 versions
- Multi-tenant queries must use ICurrentTenantService query filter
- All repositories extend GenericRepository<T>
- All controllers return ResponseData<T> with [ProducesResponseType] for 200/400/500
- FluentValidation validators for all request DTOs (input shape only)
- Service layer enforces business rules (uniqueness, existence, state, tenant scoping)
- EF queries use AsNoTracking() for read-only, AsSplitQuery() for 2+ includes, paginate in SQL
- Structured Serilog logging with named properties, no secrets/PII

---

## Phase 1: Repository Integration Tests (Infrastructure Layer)

### Task 1: CategoryRepository Integration Tests

**Files:**
- Create: `tests/FashionSaaS.Infrastructure.Tests/Repositories/CategoryRepositoryTests.cs`
- Reference: `tests/FashionSaaS.Infrastructure.Tests/Repositories/TenantRepositoryTests.cs`
- Use: `src/FashionSaaS.Infrastructure/Persistence/Repositories/CategoryRepository.cs`

**Interfaces:**
- Consumes: `ApplicationDbContext`, `ICurrentTenantService`, `CategoryRepository`
- Produces: Test cases for CRUD, slug uniqueness, tree structure, parent validation, child/product checks

- [ ] **Step 1: Create test class with DbContext factory**

```csharp
using FashionSaaS.Application.Interfaces;
using FashionSaaS.Domain.Entities;
using FashionSaaS.Infrastructure.Persistence;
using FashionSaaS.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace FashionSaaS.Infrastructure.Tests.Repositories;

public class CategoryRepositoryTests
{
    private Guid _tenantId = Guid.NewGuid();
    
    private ApplicationDbContext CreateContext()
    {
        var currentTenant = new Mock<ICurrentTenantService>();
        currentTenant.Setup(c => c.TenantId).Returns(_tenantId);

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options, currentTenant.Object);
    }
}
```

- [ ] **Step 2: Add test for SlugExistsAsync**

```csharp
[Fact]
public async Task SlugExistsAsync_ExistingSlug_ReturnsTrue()
{
    await using var ctx = CreateContext();
    var category = new Category 
    { 
        TenantId = _tenantId, 
        Name = "Shoes", 
        Slug = "shoes",
        SortOrder = 1,
        IsActive = true 
    };
    ctx.Categories.Add(category);
    await ctx.SaveChangesAsync();

    var repo = new CategoryRepository(ctx);
    var exists = await repo.SlugExistsAsync(_tenantId, "shoes");

    exists.Should().BeTrue();
}

[Fact]
public async Task SlugExistsAsync_ExcludeId_IgnoresSpecificId()
{
    await using var ctx = CreateContext();
    var cat1 = new Category 
    { 
        TenantId = _tenantId, 
        Name = "Shoes", 
        Slug = "shoes",
        SortOrder = 1,
        IsActive = true 
    };
    ctx.Categories.Add(cat1);
    await ctx.SaveChangesAsync();

    var repo = new CategoryRepository(ctx);
    var exists = await repo.SlugExistsAsync(_tenantId, "shoes", cat1.Id);

    exists.Should().BeFalse();
}

[Fact]
public async Task SlugExistsAsync_DifferentTenant_ReturnsFalse()
{
    await using var ctx = CreateContext();
    var otherTenantId = Guid.NewGuid();
    var category = new Category 
    { 
        TenantId = otherTenantId, 
        Name = "Shoes", 
        Slug = "shoes",
        SortOrder = 1,
        IsActive = true 
    };
    ctx.Categories.Add(category);
    await ctx.SaveChangesAsync();

    var repo = new CategoryRepository(ctx);
    var exists = await repo.SlugExistsAsync(_tenantId, "shoes");

    exists.Should().BeFalse();
}
```

- [ ] **Step 3: Add test for GetTreeAsync**

```csharp
[Fact]
public async Task GetTreeAsync_CategoriesWithParents_ReturnsSortedByParentThenOrder()
{
    await using var ctx = CreateContext();
    var parentCat = new Category 
    { 
        TenantId = _tenantId, 
        Name = "Apparel", 
        Slug = "apparel",
        SortOrder = 1,
        IsActive = true 
    };
    var child1 = new Category 
    { 
        TenantId = _tenantId, 
        Name = "Shirts", 
        Slug = "shirts",
        ParentCategoryId = parentCat.Id,
        SortOrder = 2,
        IsActive = true 
    };
    var child2 = new Category 
    { 
        TenantId = _tenantId, 
        Name = "Pants", 
        Slug = "pants",
        ParentCategoryId = parentCat.Id,
        SortOrder = 1,
        IsActive = true 
    };
    ctx.Categories.AddRange(parentCat, child1, child2);
    await ctx.SaveChangesAsync();

    var repo = new CategoryRepository(ctx);
    var result = await repo.GetTreeAsync(_tenantId);

    result.Should().HaveCount(3);
    result.Should().SatisfyRespectively(
        x => x.Id.Should().Be(parentCat.Id),
        x => x.Id.Should().Be(child2.Id), // SortOrder 1
        x => x.Id.Should().Be(child1.Id)  // SortOrder 2
    );
}
```

- [ ] **Step 4: Add test for HasChildrenAsync**

```csharp
[Fact]
public async Task HasChildrenAsync_CategoryWithChildren_ReturnsTrue()
{
    await using var ctx = CreateContext();
    var parent = new Category 
    { 
        TenantId = _tenantId, 
        Name = "Apparel", 
        Slug = "apparel",
        SortOrder = 1,
        IsActive = true 
    };
    var child = new Category 
    { 
        TenantId = _tenantId, 
        Name = "Shirts", 
        Slug = "shirts",
        ParentCategoryId = parent.Id,
        SortOrder = 1,
        IsActive = true 
    };
    ctx.Categories.AddRange(parent, child);
    await ctx.SaveChangesAsync();

    var repo = new CategoryRepository(ctx);
    var hasChildren = await repo.HasChildrenAsync(_tenantId, parent.Id);

    hasChildren.Should().BeTrue();
}

[Fact]
public async Task HasChildrenAsync_CategoryWithoutChildren_ReturnsFalse()
{
    await using var ctx = CreateContext();
    var category = new Category 
    { 
        TenantId = _tenantId, 
        Name = "Shoes", 
        Slug = "shoes",
        SortOrder = 1,
        IsActive = true 
    };
    ctx.Categories.Add(category);
    await ctx.SaveChangesAsync();

    var repo = new CategoryRepository(ctx);
    var hasChildren = await repo.HasChildrenAsync(_tenantId, category.Id);

    hasChildren.Should().BeFalse();
}
```

- [ ] **Step 5: Add test for HasProductsAsync**

```csharp
[Fact]
public async Task HasProductsAsync_CategoryWithProducts_ReturnsTrue()
{
    await using var ctx = CreateContext();
    var category = new Category 
    { 
        TenantId = _tenantId, 
        Name = "Shoes", 
        Slug = "shoes",
        SortOrder = 1,
        IsActive = true 
    };
    var product = new Product 
    { 
        TenantId = _tenantId, 
        Name = "Nike Air Max",
        Slug = "nike-air-max",
        Description = "Great shoes",
        CategoryId = category.Id,
        Price = 99.99m,
        IsActive = true 
    };
    ctx.Categories.Add(category);
    ctx.Products.Add(product);
    await ctx.SaveChangesAsync();

    var repo = new CategoryRepository(ctx);
    var hasProducts = await repo.HasProductsAsync(_tenantId, category.Id);

    hasProducts.Should().BeTrue();
}
```

- [ ] **Step 6: Run tests to verify they pass**

```bash
dotnet test tests/FashionSaaS.Infrastructure.Tests/FashionSaaS.Infrastructure.Tests.csproj --filter "CategoryRepositoryTests" -v
```

Expected: All tests PASS

- [ ] **Step 7: Commit**

```bash
git add tests/FashionSaaS.Infrastructure.Tests/Repositories/CategoryRepositoryTests.cs
git commit -m "test(infrastructure): add CategoryRepository integration tests"
```

---

### Task 2: ProductRepository Integration Tests

**Files:**
- Create: `tests/FashionSaaS.Infrastructure.Tests/Repositories/ProductRepositoryTests.cs`
- Use: `src/FashionSaaS.Infrastructure/Persistence/Repositories/ProductRepository.cs`

**Interfaces:**
- Consumes: `ApplicationDbContext`, `ProductRepository`
- Produces: Test cases for CRUD, slug uniqueness, category filtering, variant/image checks

- [ ] **Step 1: Create test class and helper DbContext**

```csharp
using FashionSaaS.Application.Interfaces;
using FashionSaaS.Domain.Entities;
using FashionSaaS.Infrastructure.Persistence;
using FashionSaaS.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace FashionSaaS.Infrastructure.Tests.Repositories;

public class ProductRepositoryTests
{
    private Guid _tenantId = Guid.NewGuid();
    private Guid _categoryId = Guid.NewGuid();
    
    private ApplicationDbContext CreateContext()
    {
        var currentTenant = new Mock<ICurrentTenantService>();
        currentTenant.Setup(c => c.TenantId).Returns(_tenantId);

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options, currentTenant.Object);
    }

    private void SeedCategory(ApplicationDbContext ctx)
    {
        var category = new Category 
        { 
            Id = _categoryId,
            TenantId = _tenantId, 
            Name = "Shoes", 
            Slug = "shoes",
            SortOrder = 1,
            IsActive = true 
        };
        ctx.Categories.Add(category);
        ctx.SaveChanges();
    }
}
```

- [ ] **Step 2: Add test for SlugExistsAsync**

```csharp
[Fact]
public async Task SlugExistsAsync_ExistingSlug_ReturnsTrue()
{
    await using var ctx = CreateContext();
    SeedCategory(ctx);
    var product = new Product 
    { 
        TenantId = _tenantId, 
        Name = "Nike Air Max",
        Slug = "nike-air-max",
        Description = "Great shoes",
        CategoryId = _categoryId,
        Price = 99.99m,
        IsActive = true 
    };
    ctx.Products.Add(product);
    await ctx.SaveChangesAsync();

    var repo = new ProductRepository(ctx);
    var exists = await repo.SlugExistsAsync(_tenantId, "nike-air-max");

    exists.Should().BeTrue();
}

[Fact]
public async Task SlugExistsAsync_ExcludeId_IgnoresSpecificId()
{
    await using var ctx = CreateContext();
    SeedCategory(ctx);
    var product = new Product 
    { 
        TenantId = _tenantId, 
        Name = "Nike Air Max",
        Slug = "nike-air-max",
        Description = "Great shoes",
        CategoryId = _categoryId,
        Price = 99.99m,
        IsActive = true 
    };
    ctx.Products.Add(product);
    await ctx.SaveChangesAsync();

    var repo = new ProductRepository(ctx);
    var exists = await repo.SlugExistsAsync(_tenantId, "nike-air-max", product.Id);

    exists.Should().BeFalse();
}
```

- [ ] **Step 3: Add test for GetBySlugAsync**

```csharp
[Fact]
public async Task GetBySlugAsync_ExistingSlug_ReturnsProduct()
{
    await using var ctx = CreateContext();
    SeedCategory(ctx);
    var product = new Product 
    { 
        TenantId = _tenantId, 
        Name = "Nike Air Max",
        Slug = "nike-air-max",
        Description = "Great shoes",
        CategoryId = _categoryId,
        Price = 99.99m,
        IsActive = true 
    };
    ctx.Products.Add(product);
    await ctx.SaveChangesAsync();

    var repo = new ProductRepository(ctx);
    var result = await repo.GetBySlugAsync(_tenantId, "nike-air-max");

    result.Should().NotBeNull();
    result!.Name.Should().Be("Nike Air Max");
    result.Price.Should().Be(99.99m);
}

[Fact]
public async Task GetBySlugAsync_NonExistentSlug_ReturnsNull()
{
    await using var ctx = CreateContext();
    var repo = new ProductRepository(ctx);
    var result = await repo.GetBySlugAsync(_tenantId, "nonexistent");

    result.Should().BeNull();
}
```

- [ ] **Step 4: Add test for GetByCategoryAsync with pagination**

```csharp
[Fact]
public async Task GetByCategoryAsync_WithPagination_ReturnsPaginatedResults()
{
    await using var ctx = CreateContext();
    SeedCategory(ctx);
    
    for (int i = 1; i <= 5; i++)
    {
        var product = new Product 
        { 
            TenantId = _tenantId, 
            Name = $"Product {i}",
            Slug = $"product-{i}",
            Description = "Test product",
            CategoryId = _categoryId,
            Price = 50m + i,
            IsActive = true 
        };
        ctx.Products.Add(product);
    }
    await ctx.SaveChangesAsync();

    var repo = new ProductRepository(ctx);
    var result = await repo.GetByCategoryAsync(_tenantId, _categoryId, skip: 0, take: 2);

    result.Should().HaveCount(2);
}
```

- [ ] **Step 5: Add test for HasVariantsAsync**

```csharp
[Fact]
public async Task HasVariantsAsync_ProductWithVariants_ReturnsTrue()
{
    await using var ctx = CreateContext();
    SeedCategory(ctx);
    var product = new Product 
    { 
        TenantId = _tenantId, 
        Name = "Nike Air Max",
        Slug = "nike-air-max",
        Description = "Great shoes",
        CategoryId = _categoryId,
        Price = 99.99m,
        IsActive = true 
    };
    var variant = new ProductVariant 
    { 
        TenantId = _tenantId, 
        ProductId = product.Id, 
        Name = "Size 10",
        Sku = "NAM-10",
        IsActive = true 
    };
    ctx.Products.Add(product);
    ctx.ProductVariants.Add(variant);
    await ctx.SaveChangesAsync();

    var repo = new ProductRepository(ctx);
    var hasVariants = await repo.HasVariantsAsync(_tenantId, product.Id);

    hasVariants.Should().BeTrue();
}
```

- [ ] **Step 6: Run tests**

```bash
dotnet test tests/FashionSaaS.Infrastructure.Tests/FashionSaaS.Infrastructure.Tests.csproj --filter "ProductRepositoryTests" -v
```

Expected: All tests PASS

- [ ] **Step 7: Commit**

```bash
git add tests/FashionSaaS.Infrastructure.Tests/Repositories/ProductRepositoryTests.cs
git commit -m "test(infrastructure): add ProductRepository integration tests"
```

---

### Task 3: ProductVariantRepository Integration Tests

**Files:**
- Create: `tests/FashionSaaS.Infrastructure.Tests/Repositories/ProductVariantRepositoryTests.cs`
- Use: `src/FashionSaaS.Infrastructure/Persistence/Repositories/ProductVariantRepository.cs`

**Interfaces:**
- Consumes: `ApplicationDbContext`, `ProductVariantRepository`
- Produces: Test cases for CRUD, SKU uniqueness, product filtering, inventory checks

- [ ] **Step 1: Create test class**

```csharp
using FashionSaaS.Application.Interfaces;
using FashionSaaS.Domain.Entities;
using FashionSaaS.Infrastructure.Persistence;
using FashionSaaS.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace FashionSaaS.Infrastructure.Tests.Repositories;

public class ProductVariantRepositoryTests
{
    private Guid _tenantId = Guid.NewGuid();
    private Guid _productId = Guid.NewGuid();
    
    private ApplicationDbContext CreateContext()
    {
        var currentTenant = new Mock<ICurrentTenantService>();
        currentTenant.Setup(c => c.TenantId).Returns(_tenantId);

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options, currentTenant.Object);
    }

    private void SeedProduct(ApplicationDbContext ctx)
    {
        var category = new Category 
        { 
            TenantId = _tenantId, 
            Name = "Shoes", 
            Slug = "shoes",
            SortOrder = 1,
            IsActive = true 
        };
        var product = new Product 
        { 
            Id = _productId,
            TenantId = _tenantId, 
            Name = "Nike Air Max",
            Slug = "nike-air-max",
            Description = "Great shoes",
            CategoryId = category.Id,
            Price = 99.99m,
            IsActive = true 
        };
        ctx.Categories.Add(category);
        ctx.Products.Add(product);
        ctx.SaveChanges();
    }
}
```

- [ ] **Step 2: Add test for SkuExistsAsync**

```csharp
[Fact]
public async Task SkuExistsAsync_ExistingSku_ReturnsTrue()
{
    await using var ctx = CreateContext();
    SeedProduct(ctx);
    var variant = new ProductVariant 
    { 
        TenantId = _tenantId, 
        ProductId = _productId, 
        Name = "Size 10",
        Sku = "NAM-10",
        IsActive = true 
    };
    ctx.ProductVariants.Add(variant);
    await ctx.SaveChangesAsync();

    var repo = new ProductVariantRepository(ctx);
    var exists = await repo.SkuExistsAsync(_tenantId, "NAM-10");

    exists.Should().BeTrue();
}

[Fact]
public async Task SkuExistsAsync_ExcludeId_IgnoresSpecificId()
{
    await using var ctx = CreateContext();
    SeedProduct(ctx);
    var variant = new ProductVariant 
    { 
        TenantId = _tenantId, 
        ProductId = _productId, 
        Name = "Size 10",
        Sku = "NAM-10",
        IsActive = true 
    };
    ctx.ProductVariants.Add(variant);
    await ctx.SaveChangesAsync();

    var repo = new ProductVariantRepository(ctx);
    var exists = await repo.SkuExistsAsync(_tenantId, "NAM-10", variant.Id);

    exists.Should().BeFalse();
}
```

- [ ] **Step 3: Add test for GetByProductAsync**

```csharp
[Fact]
public async Task GetByProductAsync_ProductWithVariants_ReturnsAllVariants()
{
    await using var ctx = CreateContext();
    SeedProduct(ctx);
    var var1 = new ProductVariant 
    { 
        TenantId = _tenantId, 
        ProductId = _productId, 
        Name = "Size 10",
        Sku = "NAM-10",
        IsActive = true 
    };
    var var2 = new ProductVariant 
    { 
        TenantId = _tenantId, 
        ProductId = _productId, 
        Name = "Size 11",
        Sku = "NAM-11",
        IsActive = true 
    };
    ctx.ProductVariants.AddRange(var1, var2);
    await ctx.SaveChangesAsync();

    var repo = new ProductVariantRepository(ctx);
    var result = await repo.GetByProductAsync(_tenantId, _productId);

    result.Should().HaveCount(2);
}
```

- [ ] **Step 4: Run tests**

```bash
dotnet test tests/FashionSaaS.Infrastructure.Tests/FashionSaaS.Infrastructure.Tests.csproj --filter "ProductVariantRepositoryTests" -v
```

Expected: All tests PASS

- [ ] **Step 5: Commit**

```bash
git add tests/FashionSaaS.Infrastructure.Tests/Repositories/ProductVariantRepositoryTests.cs
git commit -m "test(infrastructure): add ProductVariantRepository integration tests"
```

---

### Task 4: Remaining Repository Tests (Batch)

**Files:**
- Create: `tests/FashionSaaS.Infrastructure.Tests/Repositories/ProductImageRepositoryTests.cs`
- Create: `tests/FashionSaaS.Infrastructure.Tests/Repositories/InventoryRepositoryTests.cs`
- Create: `tests/FashionSaaS.Infrastructure.Tests/Repositories/CustomerRepositoryTests.cs`
- Create: `tests/FashionSaaS.Infrastructure.Tests/Repositories/DiscountRepositoryTests.cs`
- Create: `tests/FashionSaaS.Infrastructure.Tests/Repositories/ReviewRepositoryTests.cs`
- Create: `tests/FashionSaaS.Infrastructure.Tests/Repositories/WishlistRepositoryTests.cs`

**Interfaces:**
- Consumes: Application DbContext, each specific Repository class
- Produces: CRUD + custom query tests per repository

- [ ] **Step 1: Create ProductImageRepositoryTests**

```csharp
using FashionSaaS.Application.Interfaces;
using FashionSaaS.Domain.Entities;
using FashionSaaS.Infrastructure.Persistence;
using FashionSaaS.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace FashionSaaS.Infrastructure.Tests.Repositories;

public class ProductImageRepositoryTests
{
    private Guid _tenantId = Guid.NewGuid();
    private Guid _variantId = Guid.NewGuid();
    
    private ApplicationDbContext CreateContext()
    {
        var currentTenant = new Mock<ICurrentTenantService>();
        currentTenant.Setup(c => c.TenantId).Returns(_tenantId);

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options, currentTenant.Object);
    }

    [Fact]
    public async Task GetByVariantAsync_VariantWithImages_ReturnsAllImages()
    {
        await using var ctx = CreateContext();
        var image1 = new ProductImage 
        { 
            TenantId = _tenantId, 
            ProductVariantId = _variantId, 
            Url = "https://cdn.example.com/img1.jpg",
            AltText = "Front view",
            SortOrder = 1 
        };
        var image2 = new ProductImage 
        { 
            TenantId = _tenantId, 
            ProductVariantId = _variantId, 
            Url = "https://cdn.example.com/img2.jpg",
            AltText = "Side view",
            SortOrder = 2 
        };
        ctx.ProductImages.AddRange(image1, image2);
        await ctx.SaveChangesAsync();

        var repo = new ProductImageRepository(ctx);
        var result = await repo.GetByVariantAsync(_tenantId, _variantId);

        result.Should().HaveCount(2);
        result.Should().BeInAscendingOrder(x => x.SortOrder);
    }
}
```

- [ ] **Step 2: Create InventoryRepositoryTests**

```csharp
using FashionSaaS.Application.Interfaces;
using FashionSaaS.Domain.Entities;
using FashionSaaS.Infrastructure.Persistence;
using FashionSaaS.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace FashionSaaS.Infrastructure.Tests.Repositories;

public class InventoryRepositoryTests
{
    private Guid _tenantId = Guid.NewGuid();
    private Guid _variantId = Guid.NewGuid();
    
    private ApplicationDbContext CreateContext()
    {
        var currentTenant = new Mock<ICurrentTenantService>();
        currentTenant.Setup(c => c.TenantId).Returns(_tenantId);

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options, currentTenant.Object);
    }

    [Fact]
    public async Task GetByVariantAsync_VariantWithStock_ReturnsStockRecord()
    {
        await using var ctx = CreateContext();
        var stock = new StockAdjustment 
        { 
            TenantId = _tenantId, 
            ProductVariantId = _variantId,
            QuantityAdjustment = 100,
            Reason = "Initial stock"
        };
        ctx.StockAdjustments.Add(stock);
        await ctx.SaveChangesAsync();

        var repo = new StockAdjustmentRepository(ctx);
        var result = await repo.GetByVariantAsync(_tenantId, _variantId);

        result.Should().HaveCount(1);
        result.First().QuantityAdjustment.Should().Be(100);
    }
}
```

- [ ] **Step 3: Create CustomerRepositoryTests**

```csharp
using FashionSaaS.Application.Interfaces;
using FashionSaaS.Domain.Entities;
using FashionSaaS.Infrastructure.Persistence;
using FashionSaaS.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace FashionSaaS.Infrastructure.Tests.Repositories;

public class CustomerRepositoryTests
{
    private Guid _tenantId = Guid.NewGuid();
    
    private ApplicationDbContext CreateContext()
    {
        var currentTenant = new Mock<ICurrentTenantService>();
        currentTenant.Setup(c => c.TenantId).Returns(_tenantId);

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options, currentTenant.Object);
    }

    [Fact]
    public async Task GetByEmailAsync_ExistingEmail_ReturnsCustomer()
    {
        await using var ctx = CreateContext();
        var customer = new Customer 
        { 
            TenantId = _tenantId, 
            FirstName = "John",
            LastName = "Doe",
            Email = "john@example.com",
            IsActive = true
        };
        ctx.Customers.Add(customer);
        await ctx.SaveChangesAsync();

        var repo = new CustomerRepository(ctx);
        var result = await repo.GetByEmailAsync(_tenantId, "john@example.com");

        result.Should().NotBeNull();
        result!.FirstName.Should().Be("John");
    }

    [Fact]
    public async Task EmailExistsAsync_ExistingEmail_ReturnsTrue()
    {
        await using var ctx = CreateContext();
        var customer = new Customer 
        { 
            TenantId = _tenantId, 
            FirstName = "John",
            LastName = "Doe",
            Email = "john@example.com",
            IsActive = true
        };
        ctx.Customers.Add(customer);
        await ctx.SaveChangesAsync();

        var repo = new CustomerRepository(ctx);
        var exists = await repo.EmailExistsAsync(_tenantId, "john@example.com");

        exists.Should().BeTrue();
    }
}
```

- [ ] **Step 4: Create DiscountRepositoryTests**

```csharp
using FashionSaaS.Application.Interfaces;
using FashionSaaS.Domain.Entities;
using FashionSaaS.Infrastructure.Persistence;
using FashionSaaS.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace FashionSaaS.Infrastructure.Tests.Repositories;

public class DiscountRepositoryTests
{
    private Guid _tenantId = Guid.NewGuid();
    
    private ApplicationDbContext CreateContext()
    {
        var currentTenant = new Mock<ICurrentTenantService>();
        currentTenant.Setup(c => c.TenantId).Returns(_tenantId);

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options, currentTenant.Object);
    }

    [Fact]
    public async Task GetActiveAsync_ActiveDiscounts_ReturnsOnlyActive()
    {
        await using var ctx = CreateContext();
        var active = new Discount 
        { 
            TenantId = _tenantId, 
            Code = "SUMMER20",
            DiscountType = "Percentage",
            DiscountValue = 20,
            IsActive = true,
            StartDate = DateTime.UtcNow.AddDays(-1),
            EndDate = DateTime.UtcNow.AddDays(30)
        };
        var inactive = new Discount 
        { 
            TenantId = _tenantId, 
            Code = "WINTER10",
            DiscountType = "Percentage",
            DiscountValue = 10,
            IsActive = false,
            StartDate = DateTime.UtcNow.AddDays(-1),
            EndDate = DateTime.UtcNow.AddDays(30)
        };
        ctx.Discounts.AddRange(active, inactive);
        await ctx.SaveChangesAsync();

        var repo = new DiscountRepository(ctx);
        var result = await repo.GetActiveAsync(_tenantId);

        result.Should().HaveCount(1);
        result.First().Code.Should().Be("SUMMER20");
    }

    [Fact]
    public async Task GetByCodeAsync_ExistingCode_ReturnsDiscount()
    {
        await using var ctx = CreateContext();
        var discount = new Discount 
        { 
            TenantId = _tenantId, 
            Code = "SUMMER20",
            DiscountType = "Percentage",
            DiscountValue = 20,
            IsActive = true,
            StartDate = DateTime.UtcNow.AddDays(-1),
            EndDate = DateTime.UtcNow.AddDays(30)
        };
        ctx.Discounts.Add(discount);
        await ctx.SaveChangesAsync();

        var repo = new DiscountRepository(ctx);
        var result = await repo.GetByCodeAsync(_tenantId, "SUMMER20");

        result.Should().NotBeNull();
        result!.DiscountValue.Should().Be(20);
    }
}
```

- [ ] **Step 5: Create ReviewRepositoryTests**

```csharp
using FashionSaaS.Application.Interfaces;
using FashionSaaS.Domain.Entities;
using FashionSaaS.Infrastructure.Persistence;
using FashionSaaS.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace FashionSaaS.Infrastructure.Tests.Repositories;

public class ReviewRepositoryTests
{
    private Guid _tenantId = Guid.NewGuid();
    private Guid _productId = Guid.NewGuid();
    
    private ApplicationDbContext CreateContext()
    {
        var currentTenant = new Mock<ICurrentTenantService>();
        currentTenant.Setup(c => c.TenantId).Returns(_tenantId);

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options, currentTenant.Object);
    }

    [Fact]
    public async Task GetByProductAsync_ProductWithReviews_ReturnsReviewsOrderedByDate()
    {
        await using var ctx = CreateContext();
        var customer = new Customer 
        { 
            TenantId = _tenantId, 
            FirstName = "John",
            LastName = "Doe",
            Email = "john@example.com",
            IsActive = true
        };
        var review1 = new Review 
        { 
            TenantId = _tenantId, 
            ProductId = _productId,
            CustomerId = customer.Id,
            Rating = 5,
            Comment = "Excellent product",
            IsApproved = true,
            CreatedAt = DateTime.UtcNow.AddDays(-5)
        };
        var review2 = new Review 
        { 
            TenantId = _tenantId, 
            ProductId = _productId,
            CustomerId = customer.Id,
            Rating = 4,
            Comment = "Good quality",
            IsApproved = true,
            CreatedAt = DateTime.UtcNow
        };
        ctx.Customers.Add(customer);
        ctx.Reviews.AddRange(review1, review2);
        await ctx.SaveChangesAsync();

        var repo = new ReviewRepository(ctx);
        var result = await repo.GetByProductAsync(_tenantId, _productId);

        result.Should().HaveCount(2);
        result.Should().BeInDescendingOrder(x => x.CreatedAt);
    }

    [Fact]
    public async Task GetAverageRatingAsync_ProductWithReviews_CalculatesCorrectAverage()
    {
        await using var ctx = CreateContext();
        var customer = new Customer 
        { 
            TenantId = _tenantId, 
            FirstName = "John",
            LastName = "Doe",
            Email = "john@example.com",
            IsActive = true
        };
        ctx.Customers.Add(customer);
        var review1 = new Review 
        { 
            TenantId = _tenantId, 
            ProductId = _productId,
            CustomerId = customer.Id,
            Rating = 5,
            Comment = "Excellent product",
            IsApproved = true
        };
        var review2 = new Review 
        { 
            TenantId = _tenantId, 
            ProductId = _productId,
            CustomerId = customer.Id,
            Rating = 3,
            Comment = "Average",
            IsApproved = true
        };
        ctx.Reviews.AddRange(review1, review2);
        await ctx.SaveChangesAsync();

        var repo = new ReviewRepository(ctx);
        var average = await repo.GetAverageRatingAsync(_tenantId, _productId);

        average.Should().Be(4m);
    }
}
```

- [ ] **Step 6: Create WishlistRepositoryTests**

```csharp
using FashionSaaS.Application.Interfaces;
using FashionSaaS.Domain.Entities;
using FashionSaaS.Infrastructure.Persistence;
using FashionSaaS.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace FashionSaaS.Infrastructure.Tests.Repositories;

public class WishlistRepositoryTests
{
    private Guid _tenantId = Guid.NewGuid();
    private Guid _customerId = Guid.NewGuid();
    
    private ApplicationDbContext CreateContext()
    {
        var currentTenant = new Mock<ICurrentTenantService>();
        currentTenant.Setup(c => c.TenantId).Returns(_tenantId);

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options, currentTenant.Object);
    }

    [Fact]
    public async Task GetByCustomerAsync_CustomerWithWishlist_ReturnsWishlistItems()
    {
        await using var ctx = CreateContext();
        var customer = new Customer 
        { 
            Id = _customerId,
            TenantId = _tenantId, 
            FirstName = "John",
            LastName = "Doe",
            Email = "john@example.com",
            IsActive = true
        };
        var wishlist = new Wishlist 
        { 
            TenantId = _tenantId, 
            CustomerId = _customerId,
            ProductId = Guid.NewGuid()
        };
        ctx.Customers.Add(customer);
        ctx.Wishlists.Add(wishlist);
        await ctx.SaveChangesAsync();

        var repo = new WishlistRepository(ctx);
        var result = await repo.GetByCustomerAsync(_tenantId, _customerId);

        result.Should().HaveCount(1);
    }

    [Fact]
    public async Task ItemExistsAsync_ExistingItem_ReturnsTrue()
    {
        await using var ctx = CreateContext();
        var customer = new Customer 
        { 
            Id = _customerId,
            TenantId = _tenantId, 
            FirstName = "John",
            LastName = "Doe",
            Email = "john@example.com",
            IsActive = true
        };
        var productId = Guid.NewGuid();
        var wishlist = new Wishlist 
        { 
            TenantId = _tenantId, 
            CustomerId = _customerId,
            ProductId = productId
        };
        ctx.Customers.Add(customer);
        ctx.Wishlists.Add(wishlist);
        await ctx.SaveChangesAsync();

        var repo = new WishlistRepository(ctx);
        var exists = await repo.ItemExistsAsync(_tenantId, _customerId, productId);

        exists.Should().BeTrue();
    }
}
```

- [ ] **Step 7: Run all repository tests**

```bash
dotnet test tests/FashionSaaS.Infrastructure.Tests/FashionSaaS.Infrastructure.Tests.csproj --filter "RepositoryTests" -v
```

Expected: All tests PASS

- [ ] **Step 8: Commit batch**

```bash
git add tests/FashionSaaS.Infrastructure.Tests/Repositories/
git commit -m "test(infrastructure): add complete repository integration test suite for Phase 2 catalog entities"
```

---

## Phase 2: Entity Type Configurations Review (Optional Hardening)

### Task 5: Review and Complete EF Entity Configurations

**Files:**
- Reference: `src/FashionSaaS.Infrastructure/Persistence/Configurations/`
- Check: All Phase 2 entity configurations are present and index-complete

**Interfaces:**
- Consumes: Phase 2 domain entities (Category, Product, ProductVariant, etc.)
- Produces: Complete IEntityTypeConfiguration<T> implementations with proper indexes

- [ ] **Step 1: Verify all Phase 2 configurations exist**

```bash
ls src/FashionSaaS.Infrastructure/Persistence/Configurations/ | grep -E "Category|Product|Discount|Review|Inventory|Customer|Wishlist"
```

Expected: All 8+ configuration files present

- [ ] **Step 2: For each configuration, verify indexes match repository queries**

Checklist per entity:
- CategoryConfiguration: TenantId+Slug (composite), TenantId+ParentCategoryId, TenantId+IsActive
- ProductConfiguration: TenantId+Slug, TenantId+CategoryId, TenantId+IsActive
- ProductVariantConfiguration: TenantId+Sku, TenantId+ProductId
- ProductImageConfiguration: TenantId+ProductVariantId, SortOrder
- DiscountConfiguration: TenantId+Code, TenantId+IsActive, StartDate+EndDate
- CustomerConfiguration: TenantId+Email, TenantId+IsActive
- ReviewConfiguration: TenantId+ProductId, TenantId+IsApproved, CreatedAt DESC
- WishlistConfiguration: TenantId+CustomerId, TenantId+CustomerId+ProductId (unique)

- [ ] **Step 3: Run migration check**

```bash
dotnet ef migrations add --startup-project src/FashionSaaS.API --dry-run ConfigurationValidation
```

Expected: Output says "No migrations are pending."

- [ ] **Step 4: Build and verify no warnings**

```bash
dotnet build --no-restore -c Release
```

Expected: Build succeeds, no EF or configuration warnings

- [ ] **Step 5: Commit (if any changes)**

```bash
git commit -am "refactor(infrastructure): complete and validate Phase 2 entity configurations"
```

---

## Phase 3: Integration / E2E Tests (Catalog Workflows)

### Task 6: Catalog Workflow Integration Tests

**Files:**
- Create: `tests/FashionSaaS.Infrastructure.Tests/Catalogs/CatalogWorkflowTests.cs`

**Interfaces:**
- Consumes: Full services + repositories, real EF queries
- Produces: End-to-end test cases for catalog CRUD, filtering, validation

- [ ] **Step 1: Create workflow test class with full setup**

```csharp
using FashionSaaS.Application.Categories;
using FashionSaaS.Application.Categories.DTOs;
using FashionSaaS.Application.Common;
using FashionSaaS.Application.Interfaces;
using FashionSaaS.Application.Products;
using FashionSaaS.Application.Products.DTOs;
using FashionSaaS.Domain.Entities;
using FashionSaaS.Infrastructure.Persistence;
using FashionSaaS.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace FashionSaaS.Infrastructure.Tests.Catalogs;

public class CatalogWorkflowTests
{
    private Guid _tenantId = Guid.NewGuid();
    private Guid _userId = Guid.NewGuid();
    private string _ipAddress = "127.0.0.1";
    private string _userAgent = "test-agent";

    private ApplicationDbContext CreateContext()
    {
        var currentTenant = new Mock<ICurrentTenantService>();
        currentTenant.Setup(c => c.TenantId).Returns(_tenantId);

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options, currentTenant.Object);
    }

    private (CategoryService, CategoryRepository, IUnitOfWork) GetCategoryDependencies(ApplicationDbContext ctx)
    {
        var auditLog = new Mock<IAuditLogService>();
        var currentTenant = new Mock<ICurrentTenantService>();
        currentTenant.Setup(c => c.TenantId).Returns(_tenantId);
        var logger = new Mock<ILogger<CategoryService>>();
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(async () => await ctx.SaveChangesAsync());

        var repo = new CategoryRepository(ctx);
        var service = new CategoryService(repo, unitOfWork.Object, auditLog.Object, currentTenant.Object, logger.Object);
        return (service, repo, unitOfWork.Object);
    }
}
```

- [ ] **Step 2: Add test for create category workflow**

```csharp
[Fact]
public async Task CreateCategory_ValidRequest_SavesAndReturnsSuccess()
{
    await using var ctx = CreateContext();
    var (service, _, _) = GetCategoryDependencies(ctx);

    var request = new CreateCategoryRequest
    {
        Name = "Apparel",
        Slug = "apparel",
        Description = "Clothing and accessories",
        SortOrder = 1
    };

    var response = await service.CreateAsync(request, _userId, _ipAddress, _userAgent);

    response.StatusCode.Should().Be(201);
    response.Success.Should().BeTrue();
    response.Data.Should().NotBeNull();
    response.Data.Name.Should().Be("Apparel");

    var saved = await ctx.Categories.FirstOrDefaultAsync(c => c.Slug == "apparel");
    saved.Should().NotBeNull();
}
```

- [ ] **Step 3: Add test for slug uniqueness validation**

```csharp
[Fact]
public async Task CreateCategory_DuplicateSlug_Returns409()
{
    await using var ctx = CreateContext();
    var (service, _, _) = GetCategoryDependencies(ctx);

    var request1 = new CreateCategoryRequest
    {
        Name = "Apparel",
        Slug = "apparel",
        SortOrder = 1
    };
    var response1 = await service.CreateAsync(request1, _userId, _ipAddress, _userAgent);
    response1.StatusCode.Should().Be(201);

    var request2 = new CreateCategoryRequest
    {
        Name = "Clothing",
        Slug = "apparel",
        SortOrder = 2
    };
    var response2 = await service.CreateAsync(request2, _userId, _ipAddress, _userAgent);

    response2.StatusCode.Should().Be(409);
    response2.Success.Should().BeFalse();
}
```

- [ ] **Step 4: Add test for hierarchical category workflow**

```csharp
[Fact]
public async Task CreateCategoryWithParent_ValidParent_SavesHierarchy()
{
    await using var ctx = CreateContext();
    var (service, _, _) = GetCategoryDependencies(ctx);

    var parentRequest = new CreateCategoryRequest
    {
        Name = "Apparel",
        Slug = "apparel",
        SortOrder = 1
    };
    var parentResponse = await service.CreateAsync(parentRequest, _userId, _ipAddress, _userAgent);
    parentResponse.StatusCode.Should().Be(201);
    var parentId = parentResponse.Data.Id;

    var childRequest = new CreateCategoryRequest
    {
        Name = "Shirts",
        Slug = "shirts",
        ParentCategoryId = parentId,
        SortOrder = 1
    };
    var childResponse = await service.CreateAsync(childRequest, _userId, _ipAddress, _userAgent);

    childResponse.StatusCode.Should().Be(201);
    var saved = await ctx.Categories.FirstOrDefaultAsync(c => c.Slug == "shirts");
    saved.Should().NotBeNull();
    saved!.ParentCategoryId.Should().Be(parentId);
}
```

- [ ] **Step 5: Run tests**

```bash
dotnet test tests/FashionSaaS.Infrastructure.Tests/FashionSaaS.Infrastructure.Tests.csproj --filter "CatalogWorkflowTests" -v
```

Expected: All tests PASS

- [ ] **Step 6: Commit**

```bash
git add tests/FashionSaaS.Infrastructure.Tests/Catalogs/CatalogWorkflowTests.cs
git commit -m "test(integration): add catalog workflow integration tests"
```

---

## Final Tasks

### Task 7: Run Full Test Suite and Verify Coverage

- [ ] **Step 1: Run all Phase 2 tests**

```bash
dotnet test tests/FashionSaaS.Infrastructure.Tests/FashionSaaS.Infrastructure.Tests.csproj -v --no-build
dotnet test tests/FashionSaaS.Application.Tests/FashionSaaS.Application.Tests.csproj -v --no-build
```

Expected: All tests PASS, no failures

- [ ] **Step 2: Build release configuration**

```bash
dotnet build -c Release --no-restore
```

Expected: Build succeeds with no errors

- [ ] **Step 3: Create final summary commit**

```bash
git log --oneline feature/phase2-product-catalog-backend --not main | head -20
```

Expected: Shows all Phase 2 commits

- [ ] **Step 4: Optional: Create detailed test report**

```bash
dotnet test --logger "trx" --results-directory ./test-results
```

---

## Execution Options

**Plan saved to `docs/superpowers/plans/2026-06-29-phase2-catalog-backend.md`**

### Choose Execution Method:

**Option 1: Subagent-Driven (Recommended)** — I dispatch a fresh subagent per task cluster, with review between clusters for quality gates. Fastest iteration, best for parallel work.

**Option 2: Inline Execution** — Execute tasks in this session using superpowers:executing-plans skill. Single-threaded, good for tight feedback loops or if you want to watch step-by-step.

**Which would you prefer?**
