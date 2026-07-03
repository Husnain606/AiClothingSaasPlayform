# Phase 4a: Orders + Reporting Backend Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Orders domain + tenant order management + reporting suite so the Phase 3 storefront checkout works end-to-end and the Phase 4b dashboard has data.

**Architecture:** Extends the existing Clean Architecture .NET solution with a new Orders vertical slice (Domain entity → Application service/DTOs/validators → Infrastructure repository/EF config → API controllers) and a read-only Reports slice. Customer-facing endpoints live in a new `api/store/*` area using the existing TenantResolutionMiddleware; tenant admin endpoints follow the Phase 2 `api/tenant/*` controller pattern exactly.

**Tech Stack:** .NET 10 / ASP.NET Core 10, EF Core 10 (SQL Server), FluentValidation, Mapster, Serilog, xUnit + FluentAssertions + Moq

**Spec:** `docs/superpowers/specs/2026-07-02-phase4-admin-dashboard-design.md` (sections 2, 3, 5, 6)

## Global Constraints

- Clean Architecture layering: Domain → Application → Infrastructure → API; controllers thin, business rules in services
- All controller actions return `StatusCode(response.StatusCode, response)` with `ResponseData<T>` (`FashionSaaS.Application.Common`) and carry `[ProducesResponseType]` for 200/400/500 (+404 where applicable)
- Routes ONLY via `ApiUrl` constants (`src/FashionSaaS.API/Constants/ApiUrl.cs`)
- Rate limiting: store endpoints `[EnableRateLimiting("AuthenticatedPolicy")]`, tenant endpoints `[EnableRateLimiting("AuthenticatedPolicy")]`
- Multi-tenancy: `ICurrentTenantService.TenantId` (nullable Guid) injected into services; EF global query filter on Order references the injected service (NOT a captured local — model caching breaks otherwise); repository tests mock `ICurrentTenantService`
- Audit: every mutation calls `IAuditLogService.LogAsync(Guid? userId, Guid? tenantId, string action, string entityName, Guid entityId, object? oldValues, object? newValues, string ipAddress, string userAgent)`
- Money: `decimal` with `HasPrecision(18, 2)`; all dates `DateTime.UtcNow` (UTC)
- Order DTO serializes `Status` as lowercase string (`pending|confirmed|shipped|delivered|cancelled`) to match the Phase 3 storefront contract
- Services registered in `src/FashionSaaS.API/Extensions/ServiceCollectionExtensions.cs` `AddApplicationServices()`; repositories in `src/FashionSaaS.Infrastructure/DependencyInjection.cs`
- Mapster: `IRegister` profile at `src/FashionSaaS.Application/Orders/Mappings/OrderMappings.cs` (assembly-scanned automatically)
- TDD per task; ALL 366 existing tests stay green: `dotnet test --configuration Release`
- Tax = 10% flat; ShippingCost = 0; prices read server-side (client prices ignored); CVV/full PAN never accepted or stored (CardLast4 = exactly 4 digits)

**Codebase facts discovered during planning (deviations from spec noted):**
- `Customer` has NO link to `User` (no UserId). The store area resolves the customer by the JWT email claim via a new `GetOrCreateByEmailAsync` (Task 2/4). This is the minimal link; a formal FK is future work.
- Stock lives on `ProductVariant.StockQuantity` (int). Variant-less order items (ProductVariantId = null) skip stock enforcement — `Product` has no product-level stock field.
- Stock changes write a `StockAdjustment` record, mirroring the existing InventoryService pattern.

---

### Task 1: Order Domain — Entities, Enum, EF Configuration, Migration

**Files:**
- Create: `src/FashionSaaS.Domain/Enums/OrderStatus.cs`
- Create: `src/FashionSaaS.Domain/Entities/Order.cs`
- Create: `src/FashionSaaS.Domain/Entities/OrderItem.cs`
- Modify: `src/FashionSaaS.Infrastructure/Persistence/ApplicationDbContext.cs` (add DbSets + query filter)
- Create: `src/FashionSaaS.Infrastructure/Persistence/Configurations/OrderConfiguration.cs`
- Create: `src/FashionSaaS.Infrastructure/Persistence/Configurations/OrderItemConfiguration.cs`
- Test: `tests/FashionSaaS.Domain.Tests/Entities/OrderTests.cs`

**Interfaces:**
- Consumes: `BaseEntity` (Id/CreatedAt/UpdatedAt), existing ApplicationDbContext query-filter pattern for TenantId
- Produces: `Order`, `OrderItem`, `OrderStatus`, and `Order.CanTransitionTo(OrderStatus)` used by OrderService (Task 3)

- [ ] **Step 1: Write the failing domain tests**

`tests/FashionSaaS.Domain.Tests/Entities/OrderTests.cs`:

```csharp
using FashionSaaS.Domain.Entities;
using FashionSaaS.Domain.Enums;
using FluentAssertions;

namespace FashionSaaS.Domain.Tests.Entities;

public class OrderTests
{
    [Theory]
    [InlineData(OrderStatus.Pending, OrderStatus.Confirmed, true)]
    [InlineData(OrderStatus.Confirmed, OrderStatus.Shipped, true)]
    [InlineData(OrderStatus.Shipped, OrderStatus.Delivered, true)]
    [InlineData(OrderStatus.Pending, OrderStatus.Cancelled, true)]
    [InlineData(OrderStatus.Confirmed, OrderStatus.Cancelled, true)]
    [InlineData(OrderStatus.Pending, OrderStatus.Shipped, false)]
    [InlineData(OrderStatus.Pending, OrderStatus.Delivered, false)]
    [InlineData(OrderStatus.Shipped, OrderStatus.Cancelled, false)]
    [InlineData(OrderStatus.Delivered, OrderStatus.Cancelled, false)]
    [InlineData(OrderStatus.Cancelled, OrderStatus.Confirmed, false)]
    [InlineData(OrderStatus.Delivered, OrderStatus.Shipped, false)]
    public void CanTransitionTo_EnforcesLifecycle(OrderStatus from, OrderStatus to, bool expected)
    {
        var order = new Order { Status = from };
        order.CanTransitionTo(to).Should().Be(expected);
    }

    [Fact]
    public void NewOrder_DefaultsToPending()
    {
        new Order().Status.Should().Be(OrderStatus.Pending);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/FashionSaaS.Domain.Tests --filter "FullyQualifiedName~OrderTests"`
Expected: FAIL — `Order`/`OrderStatus` do not exist (compile error).

- [ ] **Step 3: Implement domain types**

`src/FashionSaaS.Domain/Enums/OrderStatus.cs`:

```csharp
namespace FashionSaaS.Domain.Enums;

public enum OrderStatus
{
    Pending = 0,
    Confirmed = 1,
    Shipped = 2,
    Delivered = 3,
    Cancelled = 4
}
```

`src/FashionSaaS.Domain/Entities/Order.cs`:

```csharp
using FashionSaaS.Domain.Enums;

namespace FashionSaaS.Domain.Entities;

public class Order : BaseEntity
{
    public Guid TenantId { get; set; }
    public Guid CustomerId { get; set; }
    public string OrderNumber { get; set; } = string.Empty; // ORD-{yyyy}-{000001}, unique per tenant
    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public DateTime OrderDate { get; set; } = DateTime.UtcNow;

    // Flattened shipping snapshot — orders are immutable records
    public string ShippingFirstName { get; set; } = string.Empty;
    public string ShippingLastName { get; set; } = string.Empty;
    public string ShippingEmail { get; set; } = string.Empty;
    public string ShippingPhone { get; set; } = string.Empty;
    public string ShippingStreet { get; set; } = string.Empty;
    public string ShippingCity { get; set; } = string.Empty;
    public string ShippingState { get; set; } = string.Empty;
    public string ShippingZipCode { get; set; } = string.Empty;
    public string ShippingCountry { get; set; } = string.Empty;

    public string CardLast4 { get; set; } = string.Empty; // masked reference ONLY

    public decimal Subtotal { get; set; }
    public decimal Tax { get; set; }
    public decimal ShippingCost { get; set; }
    public decimal Total { get; set; }

    public string? TrackingNumber { get; set; }
    public string? CancelReason { get; set; }

    public Customer? Customer { get; set; }
    public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();

    private static readonly Dictionary<OrderStatus, OrderStatus[]> AllowedTransitions = new()
    {
        [OrderStatus.Pending] = [OrderStatus.Confirmed, OrderStatus.Cancelled],
        [OrderStatus.Confirmed] = [OrderStatus.Shipped, OrderStatus.Cancelled],
        [OrderStatus.Shipped] = [OrderStatus.Delivered],
        [OrderStatus.Delivered] = [],
        [OrderStatus.Cancelled] = []
    };

    public bool CanTransitionTo(OrderStatus target) =>
        AllowedTransitions[Status].Contains(target);
}
```

`src/FashionSaaS.Domain/Entities/OrderItem.cs`:

```csharp
namespace FashionSaaS.Domain.Entities;

public class OrderItem : BaseEntity
{
    public Guid OrderId { get; set; }
    public Guid ProductId { get; set; }
    public Guid? ProductVariantId { get; set; }

    // Snapshots — survive later product edits/deletes
    public string ProductName { get; set; } = string.Empty;
    public string Size { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }

    public Order? Order { get; set; }
}
```

- [ ] **Step 4: Run domain tests to verify pass**

Run: `dotnet test tests/FashionSaaS.Domain.Tests --filter "FullyQualifiedName~OrderTests"`
Expected: PASS (12 tests).

- [ ] **Step 5: EF configurations + DbContext registration**

`src/FashionSaaS.Infrastructure/Persistence/Configurations/OrderConfiguration.cs`:

```csharp
using FashionSaaS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FashionSaaS.Infrastructure.Persistence.Configurations;

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.HasKey(o => o.Id);
        builder.Property(o => o.OrderNumber).HasMaxLength(20).IsRequired();
        builder.Property(o => o.ShippingFirstName).HasMaxLength(100).IsRequired();
        builder.Property(o => o.ShippingLastName).HasMaxLength(100).IsRequired();
        builder.Property(o => o.ShippingEmail).HasMaxLength(256).IsRequired();
        builder.Property(o => o.ShippingPhone).HasMaxLength(30).IsRequired();
        builder.Property(o => o.ShippingStreet).HasMaxLength(200).IsRequired();
        builder.Property(o => o.ShippingCity).HasMaxLength(100).IsRequired();
        builder.Property(o => o.ShippingState).HasMaxLength(100).IsRequired();
        builder.Property(o => o.ShippingZipCode).HasMaxLength(20).IsRequired();
        builder.Property(o => o.ShippingCountry).HasMaxLength(2).IsRequired();
        builder.Property(o => o.CardLast4).HasMaxLength(4).IsRequired();
        builder.Property(o => o.TrackingNumber).HasMaxLength(100);
        builder.Property(o => o.CancelReason).HasMaxLength(500);
        builder.Property(o => o.Subtotal).HasPrecision(18, 2);
        builder.Property(o => o.Tax).HasPrecision(18, 2);
        builder.Property(o => o.ShippingCost).HasPrecision(18, 2);
        builder.Property(o => o.Total).HasPrecision(18, 2);

        builder.HasIndex(o => new { o.TenantId, o.OrderNumber }).IsUnique();
        builder.HasIndex(o => new { o.TenantId, o.OrderDate });
        builder.HasIndex(o => new { o.TenantId, o.Status });
        builder.HasIndex(o => new { o.TenantId, o.CustomerId });

        builder.HasOne(o => o.Customer).WithMany().HasForeignKey(o => o.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(o => o.Items).WithOne(i => i.Order).HasForeignKey(i => i.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
```

`src/FashionSaaS.Infrastructure/Persistence/Configurations/OrderItemConfiguration.cs`:

```csharp
using FashionSaaS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FashionSaaS.Infrastructure.Persistence.Configurations;

public class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.HasKey(i => i.Id);
        builder.Property(i => i.ProductName).HasMaxLength(200).IsRequired();
        builder.Property(i => i.Size).HasMaxLength(50);
        builder.Property(i => i.Color).HasMaxLength(50);
        builder.Property(i => i.UnitPrice).HasPrecision(18, 2);

        builder.HasIndex(i => i.OrderId);
        builder.HasIndex(i => i.ProductId);
    }
}
```

In `src/FashionSaaS.Infrastructure/Persistence/ApplicationDbContext.cs`, add DbSets next to the existing Phase 2 sets:

```csharp
public DbSet<Order> Orders => Set<Order>();
public DbSet<OrderItem> OrderItems => Set<OrderItem>();
```

and in `OnModelCreating`, next to the existing tenant query filters (copy the EXACT pattern used by `Product`/`Discount` — the filter must reference the injected `ICurrentTenantService` field, e.g. `_currentTenant`):

```csharp
modelBuilder.Entity<Order>()
    .HasQueryFilter(o => _currentTenant.TenantId == null || o.TenantId == _currentTenant.TenantId);
```

(Match the field name actually used in the file — open it and copy the neighboring filter line verbatim, changing only the entity. OrderItem needs no filter of its own; it is only reached through Order. If the existing pattern applies filters to child entities too, mirror it.)

- [ ] **Step 6: Build + create migration**

```bash
dotnet build --configuration Release
dotnet ef migrations add Phase4Orders --startup-project src/FashionSaaS.API --project src/FashionSaaS.Infrastructure
```

Expected: build success; migration `Phase4Orders` created under `src/FashionSaaS.Infrastructure/Persistence/Migrations/` containing `Orders` and `OrderItems` tables with the four Order indexes.

- [ ] **Step 7: Run full existing suite (regression gate)**

Run: `dotnet test --configuration Release`
Expected: 366 existing + 12 new = 378 passing, 0 failed.

- [ ] **Step 8: Commit**

```bash
git add src/FashionSaaS.Domain src/FashionSaaS.Infrastructure tests/FashionSaaS.Domain.Tests
git commit -m "feat(orders): Order/OrderItem domain, status lifecycle, EF config, Phase4Orders migration"
```

---

### Task 2: Order DTOs, Repository, Mapster Profile, Customer Linkage

**Files:**
- Create: `src/FashionSaaS.Application/Orders/DTOs/OrderDtos.cs`
- Create: `src/FashionSaaS.Application/Interfaces/IOrderRepository.cs`
- Modify: `src/FashionSaaS.Application/Interfaces/ICustomerRepository.cs` (add GetOrCreateByEmailAsync)
- Create: `src/FashionSaaS.Application/Orders/Mappings/OrderMappings.cs`
- Create: `src/FashionSaaS.Infrastructure/Persistence/Repositories/OrderRepository.cs`
- Modify: `src/FashionSaaS.Infrastructure/Persistence/Repositories/CustomerRepository.cs`
- Modify: `src/FashionSaaS.Infrastructure/DependencyInjection.cs` (register IOrderRepository)
- Test: `tests/FashionSaaS.Infrastructure.Tests/Repositories/OrderRepositoryTests.cs`

**Interfaces:**
- Consumes: `Order`, `OrderItem`, `OrderStatus` (Task 1); existing `GenericRepository` base if present (mirror `DiscountRepository`'s base usage exactly)
- Produces (used by Tasks 3-7 verbatim):

```csharp
// DTOs (namespace FashionSaaS.Application.Orders.DTOs)
public class ShippingAddressDto
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
}

public class CreateOrderItemRequest
{
    public Guid ProductId { get; set; }
    public int Quantity { get; set; }
    public OrderVariantDto? Variant { get; set; }
}

public class OrderVariantDto
{
    public string? Size { get; set; }
    public string? Color { get; set; }
}

public class CreateOrderPaymentDto
{
    public string CardholderName { get; set; } = string.Empty;
    public string CardNumber { get; set; } = string.Empty; // masked "****1111" from storefront; validator enforces
}

public class CreateOrderRequest
{
    public ShippingAddressDto ShippingAddress { get; set; } = new();
    public CreateOrderPaymentDto PaymentInfo { get; set; } = new();
    public List<CreateOrderItemRequest> Items { get; set; } = [];
}

public class OrderItemDto
{
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int Quantity { get; set; }
    public OrderVariantDto? Variant { get; set; }
}

public class OrderDto
{
    public string OrderId { get; set; } = string.Empty;        // OrderNumber, e.g. ORD-2026-000001 (storefront contract)
    public Guid Id { get; set; }                                 // internal Guid for admin detail routes
    public Guid CustomerId { get; set; }
    public DateTime OrderDate { get; set; }
    public string Status { get; set; } = string.Empty;           // lowercase: pending|confirmed|shipped|delivered|cancelled
    public List<OrderItemDto> Items { get; set; } = [];
    public ShippingAddressDto ShippingAddress { get; set; } = new();
    public decimal Subtotal { get; set; }
    public decimal Tax { get; set; }
    public decimal ShippingCost { get; set; }
    public decimal Total { get; set; }
    public string? TrackingNumber { get; set; }
}

public class OrderFilter
{
    public Guid? TenantId { get; set; }
    public FashionSaaS.Domain.Enums.OrderStatus? Status { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public Guid? CustomerId { get; set; }
    public string? Search { get; set; }   // matches OrderNumber contains
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

// Repository (namespace FashionSaaS.Application.Interfaces)
public interface IOrderRepository
{
    Task AddAsync(Order order);
    Task<Order?> GetByIdWithItemsAsync(Guid id, CancellationToken ct = default);
    Task<(IReadOnlyList<Order> Items, int TotalCount)> GetPagedAsync(OrderFilter filter, CancellationToken ct = default);
    Task<int> CountForYearAsync(Guid tenantId, int year, CancellationToken ct = default); // for order number sequence
}

// ICustomerRepository addition
Task<Customer> GetOrCreateByEmailAsync(Guid tenantId, string email, string firstName, string lastName, string? phone, CancellationToken ct = default);
```

- [ ] **Step 1: Write failing repository tests**

`tests/FashionSaaS.Infrastructure.Tests/Repositories/OrderRepositoryTests.cs` — mirror `CategoryRepositoryTests` setup exactly (in-memory DbContext + mocked `ICurrentTenantService`):

```csharp
using FashionSaaS.Application.Interfaces;
using FashionSaaS.Application.Orders.DTOs;
using FashionSaaS.Domain.Entities;
using FashionSaaS.Domain.Enums;
using FashionSaaS.Infrastructure.Persistence;
using FashionSaaS.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace FashionSaaS.Infrastructure.Tests.Repositories;

public class OrderRepositoryTests
{
    private readonly Guid _tenantId = Guid.NewGuid();

    private ApplicationDbContext CreateContext()
    {
        var currentTenant = new Mock<ICurrentTenantService>();
        currentTenant.Setup(c => c.TenantId).Returns(_tenantId);
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new ApplicationDbContext(options, currentTenant.Object);
    }

    private Order MakeOrder(Guid tenantId, string number = "ORD-2026-000001",
        OrderStatus status = OrderStatus.Pending, DateTime? date = null) => new()
    {
        TenantId = tenantId,
        CustomerId = Guid.NewGuid(),
        OrderNumber = number,
        Status = status,
        OrderDate = date ?? DateTime.UtcNow,
        ShippingFirstName = "A", ShippingLastName = "B", ShippingEmail = "a@b.c",
        ShippingPhone = "1", ShippingStreet = "s", ShippingCity = "c",
        ShippingState = "st", ShippingZipCode = "z", ShippingCountry = "US",
        CardLast4 = "1111", Subtotal = 100m, Tax = 10m, ShippingCost = 0m, Total = 110m,
        Items = { new OrderItem { ProductId = Guid.NewGuid(), ProductName = "P", UnitPrice = 100m, Quantity = 1 } }
    };

    [Fact]
    public async Task GetByIdWithItemsAsync_ReturnsOrderWithItems()
    {
        await using var ctx = CreateContext();
        var order = MakeOrder(_tenantId);
        ctx.Orders.Add(order);
        await ctx.SaveChangesAsync();

        var repo = new OrderRepository(ctx);
        var found = await repo.GetByIdWithItemsAsync(order.Id);

        found.Should().NotBeNull();
        found!.Items.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetPagedAsync_FiltersByStatusAndDateAndSearch()
    {
        await using var ctx = CreateContext();
        ctx.Orders.AddRange(
            MakeOrder(_tenantId, "ORD-2026-000001", OrderStatus.Pending, new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc)),
            MakeOrder(_tenantId, "ORD-2026-000002", OrderStatus.Shipped, new DateTime(2026, 2, 10, 0, 0, 0, DateTimeKind.Utc)),
            MakeOrder(_tenantId, "ORD-2026-000003", OrderStatus.Shipped, new DateTime(2026, 3, 10, 0, 0, 0, DateTimeKind.Utc)));
        await ctx.SaveChangesAsync();

        var repo = new OrderRepository(ctx);

        var byStatus = await repo.GetPagedAsync(new OrderFilter { TenantId = _tenantId, Status = OrderStatus.Shipped });
        byStatus.TotalCount.Should().Be(2);

        var byDate = await repo.GetPagedAsync(new OrderFilter
        { TenantId = _tenantId, From = new DateTime(2026, 2, 1), To = new DateTime(2026, 2, 28) });
        byDate.TotalCount.Should().Be(1);

        var bySearch = await repo.GetPagedAsync(new OrderFilter { TenantId = _tenantId, Search = "000003" });
        bySearch.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task GetPagedAsync_TenantIsolation_ExcludesOtherTenants()
    {
        await using var ctx = CreateContext();
        ctx.Orders.Add(MakeOrder(Guid.NewGuid())); // other tenant
        await ctx.SaveChangesAsync();

        var repo = new OrderRepository(ctx);
        var result = await repo.GetPagedAsync(new OrderFilter { TenantId = _tenantId });

        result.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task CountForYearAsync_CountsOnlyTenantAndYear()
    {
        await using var ctx = CreateContext();
        ctx.Orders.AddRange(
            MakeOrder(_tenantId, "ORD-2026-000001", date: new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc)),
            MakeOrder(_tenantId, "ORD-2025-000001", date: new DateTime(2025, 5, 1, 0, 0, 0, DateTimeKind.Utc)));
        await ctx.SaveChangesAsync();

        var repo = new OrderRepository(ctx);
        (await repo.CountForYearAsync(_tenantId, 2026)).Should().Be(1);
    }
}
```

Also add to a new region in `tests/FashionSaaS.Infrastructure.Tests/Repositories/CustomerRepositoryTests.cs` (or create the test class if absent, same CreateContext pattern):

```csharp
[Fact]
public async Task GetOrCreateByEmailAsync_CreatesThenReuses()
{
    await using var ctx = CreateContext();
    var repo = new CustomerRepository(ctx);

    var first = await repo.GetOrCreateByEmailAsync(_tenantId, "jane@x.com", "Jane", "Doe", null);
    await ctx.SaveChangesAsync();
    var second = await repo.GetOrCreateByEmailAsync(_tenantId, "jane@x.com", "Jane", "Doe", null);

    second.Id.Should().Be(first.Id);
    ctx.Customers.Count(c => c.Email == "jane@x.com").Should().Be(1);
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/FashionSaaS.Infrastructure.Tests --filter "FullyQualifiedName~OrderRepositoryTests"`
Expected: FAIL (compile — `IOrderRepository`, `OrderRepository`, `OrderFilter` missing).

- [ ] **Step 3: Implement DTOs, interfaces, repository, mapping**

Create `src/FashionSaaS.Application/Orders/DTOs/OrderDtos.cs` with EXACTLY the DTO/filter definitions from **Interfaces: Produces** above (one file, all order DTOs).

Create `src/FashionSaaS.Application/Interfaces/IOrderRepository.cs` with the interface from **Produces**.

Add to `src/FashionSaaS.Application/Interfaces/ICustomerRepository.cs` the `GetOrCreateByEmailAsync` signature from **Produces**.

`src/FashionSaaS.Infrastructure/Persistence/Repositories/OrderRepository.cs`:

```csharp
using FashionSaaS.Application.Interfaces;
using FashionSaaS.Application.Orders.DTOs;
using FashionSaaS.Domain.Entities;
using FashionSaaS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FashionSaaS.Infrastructure.Persistence.Repositories;

public class OrderRepository(ApplicationDbContext context) : IOrderRepository
{
    public async Task AddAsync(Order order) => await context.Orders.AddAsync(order);

    public Task<Order?> GetByIdWithItemsAsync(Guid id, CancellationToken ct = default) =>
        context.Orders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == id, ct);

    public async Task<(IReadOnlyList<Order> Items, int TotalCount)> GetPagedAsync(
        OrderFilter filter, CancellationToken ct = default)
    {
        var query = context.Orders.AsNoTracking().Include(o => o.Items).AsQueryable();

        if (filter.TenantId is { } tenantId) query = query.Where(o => o.TenantId == tenantId);
        if (filter.Status is { } status) query = query.Where(o => o.Status == status);
        if (filter.From is { } from) query = query.Where(o => o.OrderDate >= from);
        if (filter.To is { } to) query = query.Where(o => o.OrderDate <= to);
        if (filter.CustomerId is { } customerId) query = query.Where(o => o.CustomerId == customerId);
        if (!string.IsNullOrWhiteSpace(filter.Search))
            query = query.Where(o => o.OrderNumber.Contains(filter.Search));

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(o => o.OrderDate)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    public Task<int> CountForYearAsync(Guid tenantId, int year, CancellationToken ct = default) =>
        context.Orders.CountAsync(o => o.TenantId == tenantId && o.OrderDate.Year == year, ct);
}
```

In `src/FashionSaaS.Infrastructure/Persistence/Repositories/CustomerRepository.cs` add:

```csharp
public async Task<Customer> GetOrCreateByEmailAsync(Guid tenantId, string email,
    string firstName, string lastName, string? phone, CancellationToken ct = default)
{
    var existing = await context.Customers
        .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.Email == email, ct);
    if (existing is not null) return existing;

    var customer = new Customer
    {
        TenantId = tenantId, Email = email,
        FirstName = firstName, LastName = lastName, Phone = phone, IsActive = true
    };
    await context.Customers.AddAsync(customer, ct);
    return customer;
}
```

(Adapt the context field name to what `CustomerRepository` actually uses — open the file first.)

`src/FashionSaaS.Application/Orders/Mappings/OrderMappings.cs`:

```csharp
using FashionSaaS.Application.Orders.DTOs;
using FashionSaaS.Domain.Entities;
using Mapster;

namespace FashionSaaS.Application.Orders.Mappings;

public class OrderMappings : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<Order, OrderDto>()
            .Map(d => d.OrderId, s => s.OrderNumber)
            .Map(d => d.Id, s => s.Id)
            .Map(d => d.Status, s => s.Status.ToString().ToLowerInvariant())
            .Map(d => d.ShippingAddress, s => new ShippingAddressDto
            {
                FirstName = s.ShippingFirstName, LastName = s.ShippingLastName,
                Email = s.ShippingEmail, Phone = s.ShippingPhone, Street = s.ShippingStreet,
                City = s.ShippingCity, State = s.ShippingState,
                ZipCode = s.ShippingZipCode, Country = s.ShippingCountry
            });

        config.NewConfig<OrderItem, OrderItemDto>()
            .Map(d => d.Price, s => s.UnitPrice)
            .Map(d => d.Variant, s => (s.ProductVariantId == null && s.Size == "" && s.Color == "")
                ? null
                : new OrderVariantDto { Size = s.Size == "" ? null : s.Size, Color = s.Color == "" ? null : s.Color });
    }
}
```

Register in `src/FashionSaaS.Infrastructure/DependencyInjection.cs` next to the other repositories:

```csharp
services.AddScoped<IOrderRepository, OrderRepository>();
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test tests/FashionSaaS.Infrastructure.Tests --filter "FullyQualifiedName~OrderRepositoryTests|FullyQualifiedName~CustomerRepositoryTests"`
Expected: PASS (5+ tests).

- [ ] **Step 5: Commit**

```bash
git add src/FashionSaaS.Application src/FashionSaaS.Infrastructure tests/FashionSaaS.Infrastructure.Tests
git commit -m "feat(orders): DTOs, OrderRepository with paged filtering, customer email linkage, Mapster profile"
```

---

### Task 3: OrderService — Creation, Pricing, Stock, Transitions

**Files:**
- Create: `src/FashionSaaS.Application/Orders/OrderService.cs`
- Create: `src/FashionSaaS.Application/Orders/Validators/CreateOrderRequestValidator.cs`
- Modify: `src/FashionSaaS.API/Extensions/ServiceCollectionExtensions.cs` (register OrderService)
- Test: `tests/FashionSaaS.Application.Tests/Orders/OrderServiceTests.cs`

**Interfaces:**
- Consumes: `IOrderRepository`, `ICustomerRepository.GetOrCreateByEmailAsync`, `IProductRepository.GetByIdAsync(Guid)`, `IProductVariantRepository` (open the interface and use its actual member for fetching a product's variants — Phase 2 exposes one; adapt name), `IStockAdjustmentRepository` (mirror InventoryService's usage for adjustment records), `IUnitOfWork.SaveChangesAsync`, `IAuditLogService.LogAsync`, `ICurrentTenantService.TenantId`, `ResponseData<T>`, Mapster `Adapt<OrderDto>()`
- Produces (used by Tasks 4-5 verbatim):

```csharp
public class OrderService(
    IOrderRepository orderRepository,
    ICustomerRepository customerRepository,
    IProductRepository productRepository,
    IProductVariantRepository variantRepository,
    IStockAdjustmentRepository stockAdjustmentRepository,
    IUnitOfWork unitOfWork,
    IAuditLogService auditLogService,
    ICurrentTenantService currentTenant,
    ILogger<OrderService> logger)
{
    Task<ResponseData<OrderDto>> CreateAsync(string customerEmail, string customerFirstName, string customerLastName, string? customerPhone, CreateOrderRequest request, Guid actingUserId, string ipAddress, string userAgent, CancellationToken ct = default);
    Task<ResponseData<PagedResult<OrderDto>>> GetAllAsync(OrderFilter filter, CancellationToken ct = default);
    Task<ResponseData<OrderDto>> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<ResponseData<PagedResult<OrderDto>>> GetForCustomerAsync(string customerEmail, int page, int pageSize, CancellationToken ct = default);
    Task<ResponseData<OrderDto>> GetByIdForCustomerAsync(Guid id, string customerEmail, CancellationToken ct = default); // 404 if not owner
    Task<ResponseData<OrderDto>> ConfirmAsync(Guid id, Guid actingUserId, string ipAddress, string userAgent, CancellationToken ct = default);
    Task<ResponseData<OrderDto>> ShipAsync(Guid id, string? trackingNumber, Guid actingUserId, string ipAddress, string userAgent, CancellationToken ct = default);
    Task<ResponseData<OrderDto>> DeliverAsync(Guid id, Guid actingUserId, string ipAddress, string userAgent, CancellationToken ct = default);
    Task<ResponseData<OrderDto>> CancelAsync(Guid id, string reason, bool asCustomer, string? customerEmail, Guid actingUserId, string ipAddress, string userAgent, CancellationToken ct = default);
}
```

(`PagedResult<T>` — reuse the codebase's existing paged DTO used by DiscountService's `GetAllAsync` — open `DiscountService` and use the same type; do NOT invent a new one.)

**Business rules (each is a test):**
1. Create: resolves tenant (400 if unresolved), resolves customer by email (GetOrCreate), loads each product server-side — unknown/inactive product → 400; price = variant `PriceOverride ?? product.BasePrice`; variant resolved by product + Size/Color match — variant requested but not found → 400; stock: requested variant with `StockQuantity < quantity` → 400 listing the item; decrements `StockQuantity` and writes a `StockAdjustment` (mirror InventoryService's construction of that entity; reason "OrderPlaced"); computes Subtotal = Σ(price×qty), Tax = round(Subtotal×0.10m, 2), ShippingCost = 0m, Total = Subtotal+Tax; OrderNumber = `$"ORD-{DateTime.UtcNow.Year}-{(await orderRepository.CountForYearAsync(tenantId, DateTime.UtcNow.Year, ct)) + 1:D6}"`; CardLast4 = last 4 chars of `request.PaymentInfo.CardNumber`; audits "OrderCreated"; returns 201.
2. Confirm/Ship/Deliver: loads via `GetByIdWithItemsAsync` (404 if missing); invalid transition → 400 with message `$"Cannot {action} an order in status {order.Status}"`; Ship sets TrackingNumber; each audits ("OrderConfirmed"/"OrderShipped"/"OrderDelivered"); returns 200.
3. Cancel: allowed from Pending/Confirmed only (400 otherwise); sets CancelReason; restores each item's variant `StockQuantity` (+ StockAdjustment "OrderCancelled"); when `asCustomer` is true, order's ShippingEmail must equal `customerEmail` else 404; audits "OrderCancelled".
4. GetByIdForCustomerAsync: order whose ShippingEmail ≠ customerEmail → 404 (no 403 — don't leak existence).

**Validator** `CreateOrderRequestValidator : AbstractValidator<CreateOrderRequest>`:
- Items: NotEmpty; each Quantity ≥ 1
- ShippingAddress: all 9 fields NotEmpty; Email is EmailAddress; Country length 2
- PaymentInfo.CardholderName NotEmpty; PaymentInfo.CardNumber must match regex `^[*]+\d{4}$` OR be exactly 4 digits — reject anything containing 13+ consecutive digits (full PAN) with message "Full card numbers must not be sent; provide the masked form."; no CVV property exists on the DTO by design.

- [ ] **Step 1: Write failing service tests**

`tests/FashionSaaS.Application.Tests/Orders/OrderServiceTests.cs` — mirror `DiscountServiceTests` structure (Moq mocks per dependency, `NullLogger<OrderService>.Instance`, `_tenant.SetupGet(t => t.TenantId).Returns(_tenantId)`). Cover AT MINIMUM:

- `CreateAsync_ValidRequest_Returns201_WithComputedTotals` (2 items → Subtotal/Tax/Total asserted exactly; repository AddAsync verified; stock decremented on the mocked variant object; order number `ORD-2026-000001` when CountForYearAsync returns 0)
- `CreateAsync_UnknownProduct_Returns400`
- `CreateAsync_InsufficientStock_Returns400_AndDoesNotSave` (`_uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never)`)
- `CreateAsync_VariantPriceOverride_UsedOverBasePrice`
- `CreateAsync_ClientCannotTamperPrices` (request has no price fields — assert totals derive from repo-provided BasePrice)
- `ConfirmAsync_FromPending_Succeeds` / `ConfirmAsync_FromShipped_Returns400`
- `ShipAsync_SetsTrackingNumber` / `ShipAsync_FromPending_Returns400`
- `DeliverAsync_FromShipped_Succeeds` / `DeliverAsync_FromPending_Returns400`
- `CancelAsync_FromPending_RestoresStock` (variant StockQuantity back to original; StockAdjustment repo verified)
- `CancelAsync_FromShipped_Returns400`
- `CancelAsync_AsCustomer_WrongEmail_Returns404`
- `GetByIdForCustomerAsync_NotOwner_Returns404`
- Validator tests (separate class `CreateOrderRequestValidatorTests`): masked card accepted (`****1111`), 16-digit PAN rejected, empty items rejected, bad email rejected.

Write every test with real Moq setups following this template (repeat the pattern for each; do not abbreviate in the actual test file):

```csharp
[Fact]
public async Task CreateAsync_InsufficientStock_Returns400_AndDoesNotSave()
{
    var product = new Product { Id = Guid.NewGuid(), TenantId = _tenantId, Name = "Tee", BasePrice = 20m, Status = ProductStatus.Published };
    var variant = new ProductVariant { Id = Guid.NewGuid(), ProductId = product.Id, Size = "M", Color = "Red", StockQuantity = 1 };
    _products.Setup(r => r.GetByIdAsync(product.Id)).ReturnsAsync(product);
    _variants.Setup(r => r.GetByProductIdAsync(product.Id, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<ProductVariant> { variant });
    _orders.Setup(r => r.CountForYearAsync(_tenantId, It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(0);

    var request = ValidRequestFor(product.Id, quantity: 5, size: "M", color: "Red");
    var result = await CreateService().CreateAsync("c@x.com", "C", "X", null, request, Guid.NewGuid(), "127.0.0.1", "ua");

    result.StatusCode.Should().Be(400);
    result.Message.Should().Contain("stock");
    _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
}
```

(`Product.Status`/`ProductStatus` and `IProductVariantRepository.GetByProductIdAsync` names: open the Phase 2 files and use the ACTUAL member names; if they differ, adjust test + service consistently and note it in the task report.)

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/FashionSaaS.Application.Tests --filter "FullyQualifiedName~OrderServiceTests"`
Expected: FAIL (OrderService missing).

- [ ] **Step 3: Implement OrderService + validator** per the **Produces** signatures and business rules above. Register in `AddApplicationServices()`:

```csharp
services.AddScoped<OrderService>();
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test tests/FashionSaaS.Application.Tests --filter "FullyQualifiedName~Order"`
Expected: PASS (~20 tests).

- [ ] **Step 5: Full suite regression**

Run: `dotnet test --configuration Release` — everything green.

- [ ] **Step 6: Commit**

```bash
git add src/FashionSaaS.Application src/FashionSaaS.API tests/FashionSaaS.Application.Tests
git commit -m "feat(orders): OrderService with server-side pricing, stock coupling, and status lifecycle"
```

---

### Task 4: Customer Store Endpoints (api/store/orders)

**Files:**
- Modify: `src/FashionSaaS.API/Constants/ApiUrl.cs` (add StoreOrders)
- Create: `src/FashionSaaS.API/Controllers/Store/StoreOrdersController.cs`
- Verify/Modify: role seeding for `Customer` (find where `Role` rows are seeded — RoleConfiguration or a seeder; if "Customer" is absent, add it there following the existing rows' pattern)
- Test: covered at service level (Task 3); this task's gate is build + manual route inspection + full suite green

**Interfaces:**
- Consumes: `OrderService` (Task 3 signatures verbatim), existing claim conventions (`ClaimTypes.NameIdentifier` = user id, `ClaimTypes.Email` or `"email"` claim — open JwtService to confirm the email claim name and use it)
- Produces: routes consumed by the Phase 3 storefront

- [ ] **Step 1: Add ApiUrl constants**

```csharp
public static class StoreOrders
{
    public const string Create = "api/store/orders";
    public const string GetMine = "api/store/orders";
    public const string GetById = "api/store/orders/{id}";
    public const string Cancel = "api/store/orders/{id}/cancel";
}
```

- [ ] **Step 2: Implement controller**

`src/FashionSaaS.API/Controllers/Store/StoreOrdersController.cs`:

```csharp
using System.Security.Claims;
using FashionSaaS.API.Constants;
using FashionSaaS.Application.Common;
using FashionSaaS.Application.Orders;
using FashionSaaS.Application.Orders.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FashionSaaS.API.Controllers.Store;

[ApiController]
[Authorize(Roles = "Customer")]
[EnableRateLimiting("AuthenticatedPolicy")]
public class StoreOrdersController(OrderService orderService) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string Email => User.FindFirstValue(ClaimTypes.Email)!; // confirm claim name against JwtService
    private string Ip => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    private string Ua => Request.Headers.UserAgent.ToString();

    [HttpPost(ApiUrl.StoreOrders.Create)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Create([FromBody] CreateOrderRequest request)
    {
        var firstName = request.ShippingAddress.FirstName;
        var lastName = request.ShippingAddress.LastName;
        var response = await orderService.CreateAsync(Email, firstName, lastName,
            request.ShippingAddress.Phone, request, UserId, Ip, Ua);
        return StatusCode(response.StatusCode, response);
    }

    [HttpGet(ApiUrl.StoreOrders.GetMine)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetMine([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var response = await orderService.GetForCustomerAsync(Email, page, pageSize);
        return StatusCode(response.StatusCode, response);
    }

    [HttpGet(ApiUrl.StoreOrders.GetById)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetById(Guid id)
    {
        var response = await orderService.GetByIdForCustomerAsync(id, Email);
        return StatusCode(response.StatusCode, response);
    }

    [HttpPut(ApiUrl.StoreOrders.Cancel)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Cancel(Guid id, [FromBody] CancelOrderRequest body)
    {
        var response = await orderService.CancelAsync(id, body.Reason, asCustomer: true, Email, UserId, Ip, Ua);
        return StatusCode(response.StatusCode, response);
    }
}

public record CancelOrderRequest(string Reason);
```

- [ ] **Step 3: Verify Customer role seeding** — grep for how existing roles (e.g. "StoreManager") are seeded; if "Customer" is missing, add a row in the same place (same Guid style, same migration mechanism — if seeding is via `RoleConfiguration.HasData`, a new migration `Phase4CustomerRole` is required: `dotnet ef migrations add Phase4CustomerRole --startup-project src/FashionSaaS.API --project src/FashionSaaS.Infrastructure`). If "Customer" already exists, skip.

- [ ] **Step 4: Build + full suite**

```bash
dotnet build --configuration Release   # zero warnings
dotnet test --configuration Release    # all green
```

- [ ] **Step 5: Commit**

```bash
git add src/FashionSaaS.API src/FashionSaaS.Infrastructure
git commit -m "feat(orders): customer-facing store order endpoints with own-order enforcement"
```

---

### Task 5: Tenant Order Management Endpoints (api/tenant/orders)

**Files:**
- Modify: `src/FashionSaaS.API/Constants/ApiUrl.cs` (add TenantOrders)
- Create: `src/FashionSaaS.API/Controllers/Tenant/OrdersController.cs`

**Interfaces:**
- Consumes: `OrderService.GetAllAsync/GetByIdAsync/ConfirmAsync/ShipAsync/DeliverAsync/CancelAsync` (Task 3 signatures verbatim)

- [ ] **Step 1: ApiUrl constants**

```csharp
public static class TenantOrders
{
    public const string GetAll = "api/tenant/orders";
    public const string GetById = "api/tenant/orders/{id}";
    public const string Confirm = "api/tenant/orders/{id}/confirm";
    public const string Ship = "api/tenant/orders/{id}/ship";
    public const string Deliver = "api/tenant/orders/{id}/deliver";
    public const string Cancel = "api/tenant/orders/{id}/cancel";
}
```

- [ ] **Step 2: Implement controller**

`src/FashionSaaS.API/Controllers/Tenant/OrdersController.cs` — same skeleton as `ProductsController` (primary ctor, UserId/Ip/Ua props):

```csharp
[ApiController]
[Authorize(Roles = "AdminOwner,OrderManager,StoreManager")]
[EnableRateLimiting("AuthenticatedPolicy")]
public class OrdersController(OrderService orderService) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string Ip => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    private string Ua => Request.Headers.UserAgent.ToString();

    [HttpGet(ApiUrl.TenantOrders.GetAll)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetAll([FromQuery] OrderFilter filter)
    {
        var response = await orderService.GetAllAsync(filter);
        return StatusCode(response.StatusCode, response);
    }

    [HttpGet(ApiUrl.TenantOrders.GetById)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetById(Guid id)
    {
        var response = await orderService.GetByIdAsync(id);
        return StatusCode(response.StatusCode, response);
    }

    [HttpPut(ApiUrl.TenantOrders.Confirm)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Confirm(Guid id)
    {
        var response = await orderService.ConfirmAsync(id, UserId, Ip, Ua);
        return StatusCode(response.StatusCode, response);
    }

    [HttpPut(ApiUrl.TenantOrders.Ship)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Ship(Guid id, [FromBody] ShipOrderRequest body)
    {
        var response = await orderService.ShipAsync(id, body.TrackingNumber, UserId, Ip, Ua);
        return StatusCode(response.StatusCode, response);
    }

    [HttpPut(ApiUrl.TenantOrders.Deliver)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Deliver(Guid id)
    {
        var response = await orderService.DeliverAsync(id, UserId, Ip, Ua);
        return StatusCode(response.StatusCode, response);
    }

    [HttpPut(ApiUrl.TenantOrders.Cancel)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Cancel(Guid id, [FromBody] CancelOrderRequest body)
    {
        var response = await orderService.CancelAsync(id, body.Reason, asCustomer: false, null, UserId, Ip, Ua);
        return StatusCode(response.StatusCode, response);
    }
}

public record ShipOrderRequest(string? TrackingNumber);
```

(Include the usings block matching ProductsController's. `CancelOrderRequest` is defined in Task 4 — move both request records to `src/FashionSaaS.Application/Orders/DTOs/OrderDtos.cs` if the reviewer prefers DTO consolidation; either location is acceptable, but define each type ONCE.)

- [ ] **Step 3: Build + full suite**

```bash
dotnet build --configuration Release && dotnet test --configuration Release
```
Expected: green.

- [ ] **Step 4: Commit**

```bash
git add src/FashionSaaS.API
git commit -m "feat(orders): tenant order management endpoints (confirm/ship/deliver/cancel)"
```

---

### Task 6: ReportService — 7 Aggregate Queries

**Files:**
- Create: `src/FashionSaaS.Application/Reports/DTOs/ReportDtos.cs`
- Create: `src/FashionSaaS.Application/Interfaces/IReportRepository.cs`
- Create: `src/FashionSaaS.Infrastructure/Persistence/Repositories/ReportRepository.cs`
- Create: `src/FashionSaaS.Application/Reports/ReportService.cs`
- Create: `src/FashionSaaS.Application/Reports/Validators/ReportRangeValidator.cs`
- Modify: DI registrations (both files, as before)
- Test: `tests/FashionSaaS.Application.Tests/Reports/ReportServiceTests.cs`

**Interfaces:**
- Consumes: `ApplicationDbContext` sets (Orders, OrderItems, Customers, Reviews, ProductVariants, Products, Categories, StockAdjustments), `ICurrentTenantService`
- Produces (Task 7 consumes verbatim):

```csharp
// DTOs (namespace FashionSaaS.Application.Reports.DTOs)
public class ReportRange { public DateTime From { get; set; } public DateTime To { get; set; } }
public class SummaryReportDto { public decimal Revenue { get; set; } public int OrderCount { get; set; } public decimal AvgOrderValue { get; set; } public int NewCustomers { get; set; } public int PendingReviews { get; set; } public int LowStockCount { get; set; } }
public class SalesPointDto { public DateTime PeriodStart { get; set; } public decimal Revenue { get; set; } public int OrderCount { get; set; } }
public class TopProductDto { public Guid ProductId { get; set; } public string ProductName { get; set; } = string.Empty; public decimal Revenue { get; set; } public int Units { get; set; } }
public class StatusBreakdownDto { public string Status { get; set; } = string.Empty; public int Count { get; set; } public decimal Revenue { get; set; } }
public class CustomerAnalyticsDto { public List<SalesPointDto> NewCustomersOverTime { get; set; } = []; public double RepeatPurchaseRate { get; set; } public List<TopCustomerDto> TopCustomers { get; set; } = []; }
public class TopCustomerDto { public Guid CustomerId { get; set; } public string Email { get; set; } = string.Empty; public decimal TotalSpend { get; set; } public int OrderCount { get; set; } }
public class InventoryTrendsDto { public List<SalesPointDto> AdjustmentsOverTime { get; set; } = []; public List<LowStockItemDto> LowStock { get; set; } = []; }
public class LowStockItemDto { public Guid VariantId { get; set; } public string ProductName { get; set; } = string.Empty; public string Sku { get; set; } = string.Empty; public int StockQuantity { get; set; } }
public class CategorySalesDto { public Guid CategoryId { get; set; } public string CategoryName { get; set; } = string.Empty; public decimal Revenue { get; set; } public int Units { get; set; } }
public enum ReportInterval { Day, Week, Month }

// Service (namespace FashionSaaS.Application.Reports)
public class ReportService(IReportRepository reportRepository, ICurrentTenantService currentTenant, ILogger<ReportService> logger)
{
    Task<ResponseData<SummaryReportDto>> GetSummaryAsync(DateTime from, DateTime to, CancellationToken ct = default);
    Task<ResponseData<List<SalesPointDto>>> GetSalesOverTimeAsync(DateTime from, DateTime to, ReportInterval interval, CancellationToken ct = default);
    Task<ResponseData<List<TopProductDto>>> GetTopProductsAsync(DateTime from, DateTime to, int take, string by, CancellationToken ct = default); // by: "revenue"|"units"
    Task<ResponseData<List<StatusBreakdownDto>>> GetStatusBreakdownAsync(DateTime from, DateTime to, CancellationToken ct = default);
    Task<ResponseData<CustomerAnalyticsDto>> GetCustomerAnalyticsAsync(DateTime from, DateTime to, ReportInterval interval, CancellationToken ct = default);
    Task<ResponseData<InventoryTrendsDto>> GetInventoryTrendsAsync(DateTime from, DateTime to, CancellationToken ct = default);
    Task<ResponseData<List<CategorySalesDto>>> GetCategorySalesAsync(DateTime from, DateTime to, Guid? categoryId, CancellationToken ct = default);
}
```

`IReportRepository` mirrors these with raw aggregate methods taking `(Guid tenantId, DateTime from, DateTime to, ...)` and returning the DTO lists — the service adds tenant resolution (400 if null), range validation (from ≤ to, span ≤ 366 days → 400), and `by` validation.

**Metric definitions (single source of truth — each is a test):**
- Revenue = Σ `Order.Total` where `Status != Cancelled` and `OrderDate` in [from, to]
- AvgOrderValue = Revenue / OrderCount (0 when no orders — no divide-by-zero)
- NewCustomers = Customers with `CreatedAt` in range
- PendingReviews = Reviews not yet approved/rejected in current tenant (reuse the existing Review status/approval field — open `Review.cs` and use its actual member)
- LowStockCount = active variants with `StockQuantity <= 5`
- Bucketing: Day = calendar date; Week = Monday-start ISO week; Month = first of month (all UTC)
- RepeatPurchaseRate = customers with ≥2 non-cancelled orders in range ÷ customers with ≥1 (0 when denominator 0)
- Category drill-down: `categoryId == null` → top-level categories (aggregate including descendants' products is NOT required — direct products per category only, note in DTO docs); else → the children of that category

- [ ] **Step 1: Write failing tests** — `ReportServiceTests` uses the REAL repository against an in-memory `ApplicationDbContext` (seeded orders across statuses/dates/tenants) rather than mocking `IReportRepository`, because the value under test is the aggregate math. Structure: one seeding helper building a fixed dataset — 2 tenants; tenant A: 3 orders Jan (1 cancelled), 2 orders Feb, customers with 1 vs 2 orders, variants at stock 3 and 50, pending + approved reviews. Then one test per metric asserting exact numbers, including:
  - `Summary_ExcludesCancelledRevenue`
  - `Summary_AvgOrderValue_ZeroWhenNoOrders`
  - `SalesOverTime_MonthBuckets_CorrectTotals`
  - `SalesOverTime_WeekBuckets_MondayStart`
  - `TopProducts_ByUnits_OrdersCorrectly`
  - `StatusBreakdown_CountsPerStatus`
  - `CustomerAnalytics_RepeatRate_Exact` (1 of 2 customers repeat → 0.5)
  - `InventoryTrends_LowStockThreshold5`
  - `CategorySales_RollsUpDirectProducts`
  - `TenantIsolation_OtherTenantExcluded`
  - `Range_Over366Days_Returns400` / `Range_FromAfterTo_Returns400`

- [ ] **Step 2: Run to verify failure** — `dotnet test tests/FashionSaaS.Application.Tests --filter "FullyQualifiedName~ReportServiceTests"` → compile FAIL.

- [ ] **Step 3: Implement** repository (LINQ GroupBy aggregates, `AsNoTracking()`), service (validation + delegation), validators. Register `IReportRepository, ReportRepository` (Infrastructure DI) and `services.AddScoped<ReportService>()` (API DI).

- [ ] **Step 4: Run to verify pass** — same filter, all green.

- [ ] **Step 5: Commit**

```bash
git add src/FashionSaaS.Application src/FashionSaaS.Infrastructure src/FashionSaaS.API tests/FashionSaaS.Application.Tests
git commit -m "feat(reports): report service with 7 tenant aggregate queries and range validation"
```

---

### Task 7: Reports Controller + CSV Export

**Files:**
- Modify: `src/FashionSaaS.API/Constants/ApiUrl.cs` (add TenantReports)
- Create: `src/FashionSaaS.Application/Reports/CsvSerializer.cs`
- Create: `src/FashionSaaS.API/Controllers/Tenant/ReportsController.cs`
- Test: `tests/FashionSaaS.Application.Tests/Reports/CsvSerializerTests.cs`

**Interfaces:**
- Consumes: `ReportService` (Task 6 signatures verbatim)
- Produces: `CsvSerializer.Serialize<T>(IEnumerable<T> rows): string` — reflection over public properties, invariant culture, header row from property names, RFC-4180 quoting

- [ ] **Step 1: Failing CSV tests**

```csharp
using FashionSaaS.Application.Reports;
using FluentAssertions;

namespace FashionSaaS.Application.Tests.Reports;

public class CsvSerializerTests
{
    private record Row(string Name, decimal Amount, DateTime When);

    [Fact]
    public void Serialize_WritesHeaderAndRows_InvariantCulture()
    {
        var rows = new[] { new Row("Tee", 1234.5m, new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc)) };
        var csv = CsvSerializer.Serialize(rows);
        var lines = csv.TrimEnd().Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        lines[0].Should().Be("Name,Amount,When");
        lines[1].Should().StartWith("Tee,1234.5,2026-01-02");
    }

    [Fact]
    public void Serialize_QuotesFieldsWithCommasAndQuotes()
    {
        var rows = new[] { new Row("Tee, \"Large\"", 1m, DateTime.UtcNow) };
        var csv = CsvSerializer.Serialize(rows);
        csv.Should().Contain("\"Tee, \"\"Large\"\"\"");
    }

    [Fact]
    public void Serialize_EmptyList_HeaderOnly()
    {
        CsvSerializer.Serialize(Array.Empty<Row>()).TrimEnd().Should().Be("Name,Amount,When");
    }
}
```

- [ ] **Step 2: Verify failure**, then **Step 3: implement**

```csharp
using System.Globalization;
using System.Reflection;
using System.Text;

namespace FashionSaaS.Application.Reports;

public static class CsvSerializer
{
    public static string Serialize<T>(IEnumerable<T> rows)
    {
        var props = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(',', props.Select(p => Escape(p.Name))));
        foreach (var row in rows)
            sb.AppendLine(string.Join(',', props.Select(p => Escape(Format(p.GetValue(row))))));
        return sb.ToString();
    }

    private static string Format(object? value) => value switch
    {
        null => string.Empty,
        DateTime dt => dt.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty
    };

    private static string Escape(string field) =>
        field.Contains(',') || field.Contains('"') || field.Contains('\n')
            ? $"\"{field.Replace("\"", "\"\"")}\""
            : field;
}
```

- [ ] **Step 4: ApiUrl + controller**

```csharp
public static class TenantReports
{
    public const string Summary = "api/tenant/reports/summary";
    public const string SalesOverTime = "api/tenant/reports/sales-over-time";
    public const string TopProducts = "api/tenant/reports/top-products";
    public const string StatusBreakdown = "api/tenant/reports/order-status-breakdown";
    public const string CustomerAnalytics = "api/tenant/reports/customer-analytics";
    public const string InventoryTrends = "api/tenant/reports/inventory-trends";
    public const string CategorySales = "api/tenant/reports/category-sales";
}
```

`ReportsController`: `[Authorize(Roles = "AdminOwner,StoreManager")]`, `[EnableRateLimiting("AuthenticatedPolicy")]`, one GET per report taking `from`, `to` (+ `interval`/`take`/`by`/`categoryId` where applicable) and `format`:

```csharp
[HttpGet(ApiUrl.TenantReports.SalesOverTime)]
[ProducesResponseType(StatusCodes.Status200OK)]
[ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
public async Task<IActionResult> SalesOverTime([FromQuery] DateTime from, [FromQuery] DateTime to,
    [FromQuery] ReportInterval interval = ReportInterval.Day, [FromQuery] string? format = null)
{
    var response = await reportService.GetSalesOverTimeAsync(from, to, interval);
    if (format == "csv" && response.IsSuccess && response.Data is not null)
        return File(Encoding.UTF8.GetBytes(CsvSerializer.Serialize(response.Data)),
            "text/csv; charset=utf-8", $"sales-over-time-{from:yyyyMMdd}-{to:yyyyMMdd}.csv");
    return StatusCode(response.StatusCode, response);
}
```

Repeat the same pattern for all 7 endpoints (summary wraps its single DTO in a one-element array for CSV: `CsvSerializer.Serialize(new[] { response.Data })`; customer-analytics and inventory-trends export their primary list — `TopCustomers` and `LowStock` respectively — and note that in the filename, e.g. `top-customers-....csv`).

- [ ] **Step 5: Build + full suite green; commit**

```bash
git add src/FashionSaaS.API src/FashionSaaS.Application tests/FashionSaaS.Application.Tests
git commit -m "feat(reports): reports controller with CSV export via shared serializer"
```

---

### Task 8: Integration Tests, E2E Workflow, Docs

**Files:**
- Test: `tests/FashionSaaS.Application.Tests/Orders/OrderWorkflowE2ETests.cs`
- Modify: `docs/PROJECT_PROGRESS.md` (Phase 4a section), `README.md` (status table row)

**Interfaces:** Consumes everything above; produces the merge-gate evidence.

- [ ] **Step 1: E2E workflow test** (real repositories over one shared in-memory context, real OrderService — only IAuditLogService mocked, IUnitOfWork real via the actual UnitOfWork over the context):

```csharp
[Fact]
public async Task FullLifecycle_CreateConfirmShipDeliver_TransitionsAndStock()
{
    // seed product (BasePrice 40) + variant (M/Red, stock 10)
    // customer creates order for 2 → assert: Pending, Subtotal 80, Tax 8, Total 88, stock 8, number ORD-2026-000001
    // Confirm → Confirmed; Ship("TRK1") → Shipped + tracking; Deliver → Delivered
    // second order → number ORD-2026-000002
}

[Fact]
public async Task CancelPath_RestoresStock()
{
    // create order for 3 (stock 10 → 7), cancel with reason → Cancelled, stock back to 10,
    // a StockAdjustment row exists for the restoration
}

[Fact]
public async Task Reports_ReflectOrders()
{
    // after the lifecycle test dataset: summary revenue equals non-cancelled totals;
    // status breakdown shows delivered=1, cancelled=1
}
```

(Write these as full, runnable tests — the comments above are the scenario; the test bodies must contain the actual seeding and assertions in the style of Task 6's tests.)

- [ ] **Step 2: Full suite twice + Release build**

```bash
dotnet test --configuration Release   # run TWICE — identical green results (flake gate)
dotnet build --configuration Release  # zero warnings
```
Expected: 366 pre-existing + ~110-130 new, 0 failed, both runs.

- [ ] **Step 3: Update docs** — add Phase 4a section to `docs/PROJECT_PROGRESS.md` (test counts, endpoints added) and flip README's Phase 4 row to "4a backend COMPLETE / 4b dashboard IN PROGRESS".

- [ ] **Step 4: Commit**

```bash
git add tests docs README.md
git commit -m "test(orders): E2E order lifecycle + reporting integration; docs for Phase 4a"
```

---

## Execution Notes for the Controller

- Backend tasks use Roslyn Navigator MCP tools (`find_symbol`, `get_diagnostics`, `find_references`) during implementation and review, per project convention.
- Tasks 1→8 are strictly sequential (each consumes the previous task's Produces).
- Phase 4b (admin area) is a separate plan, written after 4a merges.
