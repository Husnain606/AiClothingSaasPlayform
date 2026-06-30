# Mappster Migration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add Mappster object-to-object mapping library to FashionSaaS, create mapping profiles for Phase 1 and Phase 2 entities, wire up dependency injection, and ensure all 354 tests pass.

**Architecture:** Mapster is configured via IMapper interface (similar to AutoMapper surface) with mapping configurations organized by feature folder. Each entity group gets a mapping profile class. Dependency injection via extension method in ServiceCollectionExtensions. Services use IMapper to map DTOs ↔ domain entities.

**Tech Stack:** Mappster (.NET 10), ASP.NET Core 10, Xunit, existing project structure

## Global Constraints

- .NET 10 minimum version (already in place)
- No breaking changes to existing service signatures
- All 354 tests must pass (174 Phase 1 + 180 Phase 2)
- No modifications to domain entities or database schema
- Mapping profiles follow feature-folder structure (one per feature)
- IMapper injected via dependency injection only (no static references)
- Maintain backward compatibility with existing response DTOs

---

## Phase 1: Mappster Infrastructure Setup

### Task 1: Add Mappster NuGet Package and Create Base Configuration

**Files:**
- Modify: `src/FashionSaaS.Application/FashionSaaS.Application.csproj`
- Create: `src/FashionSaaS.Application/Mapping/MappingConfiguration.cs`
- Modify: `src/FashionSaaS.API/Extensions/ServiceCollectionExtensions.cs`

**Interfaces:**
- Consumes: Existing Application project structure, ServiceCollectionExtensions
- Produces: `IMapper` available via DI, `MappingConfiguration` class for central configuration

- [ ] **Step 1: Add Mappster NuGet package**

Open `src/FashionSaaS.Application/FashionSaaS.Application.csproj` and add:

```xml
<ItemGroup>
  <PackageReference Include="Mapster" Version="13.2.0" />
  <PackageReference Include="Mapster.DependencyInjection" Version="1.1.0" />
</ItemGroup>
```

- [ ] **Step 2: Create mapping configuration file**

Create `src/FashionSaaS.Application/Mapping/MappingConfiguration.cs`:

```csharp
using Mapster;
using System.Reflection;

namespace FashionSaaS.Application.Mapping;

/// <summary>
/// Central Mappster configuration. Scans all mapping profiles in the Application assembly.
/// </summary>
public static class MappingConfiguration
{
    public static TypeAdapterConfig GetMappingConfig()
    {
        var config = TypeAdapterConfig.GlobalSettings;
        
        // Auto-register all IRegister implementations from this assembly
        config.Scan(Assembly.GetExecutingAssembly());
        
        return config;
    }
}
```

- [ ] **Step 3: Register Mappster in dependency injection**

Modify `src/FashionSaaS.API/Extensions/ServiceCollectionExtensions.cs` to add Mappster registration:

After the existing service registrations, add:

```csharp
// Mappster configuration
var mapperConfig = Application.Mapping.MappingConfiguration.GetMappingConfig();
services.AddMapster(mapperConfig);
```

- [ ] **Step 4: Verify project builds**

```bash
dotnet build src/FashionSaaS.Application/FashionSaaS.Application.csproj
```

Expected: Build succeeds, no errors

- [ ] **Step 5: Run tests to ensure nothing broke**

```bash
dotnet test tests/FashionSaaS.Application.Tests/FashionSaaS.Application.Tests.csproj -c Debug
```

Expected: All tests still pass (same count as before)

- [ ] **Step 6: Commit**

```bash
git add src/FashionSaaS.Application/FashionSaaS.Application.csproj
git add src/FashionSaaS.Application/Mapping/MappingConfiguration.cs
git add src/FashionSaaS.API/Extensions/ServiceCollectionExtensions.cs
git commit -m "chore: add Mappster NuGet packages and wire up DI configuration"
```

---

## Phase 2: Create Mapping Profiles for Phase 1 Entities

### Task 2: Create Tenant Mapping Profile

**Files:**
- Create: `src/FashionSaaS.Application/Tenants/Mapping/TenantMappingProfile.cs`

**Interfaces:**
- Consumes: Tenant entity, Tenant DTOs/responses
- Produces: Mappster mapping registration for Tenant ↔ DTO conversions

- [ ] **Step 1: Create tenant mapping profile**

Create `src/FashionSaaS.Application/Tenants/Mapping/TenantMappingProfile.cs`:

```csharp
using Mapster;
using FashionSaaS.Domain.Entities;
using FashionSaaS.Application.Tenants.DTOs;

namespace FashionSaaS.Application.Tenants.Mapping;

/// <summary>
/// Mappster configuration for Tenant entity and DTOs.
/// </summary>
public class TenantMappingProfile : IRegister
{
    public void Register(TypeAdapterBuilder config)
    {
        config.NewConfig<Tenant, TenantResponse>()
            .Map(dest => dest.Id, src => src.Id)
            .Map(dest => dest.Name, src => src.Name)
            .Map(dest => dest.Slug, src => src.Slug)
            .Map(dest => dest.Email, src => src.Email)
            .Map(dest => dest.Website, src => src.Website)
            .Map(dest => dest.LogoUrl, src => src.LogoUrl)
            .Map(dest => dest.IsActive, src => src.IsActive)
            .Map(dest => dest.CreatedAt, src => src.CreatedAt)
            .Map(dest => dest.UpdatedAt, src => src.UpdatedAt);

        config.NewConfig<CreateTenantRequest, Tenant>()
            .Map(dest => dest.Name, src => src.Name)
            .Map(dest => dest.Slug, src => src.Slug)
            .Map(dest => dest.Email, src => src.Email)
            .Ignore(dest => dest.Id)
            .Ignore(dest => dest.IsActive)
            .Ignore(dest => dest.CreatedAt)
            .Ignore(dest => dest.UpdatedAt);
    }
}
```

- [ ] **Step 2: Build and test**

```bash
dotnet build src/FashionSaaS.Application/FashionSaaS.Application.csproj
dotnet test tests/FashionSaaS.Application.Tests/FashionSaaS.Application.Tests.csproj --filter "TenantServiceTests" -c Debug
```

Expected: Build succeeds, Tenant tests pass

- [ ] **Step 3: Commit**

```bash
git add src/FashionSaaS.Application/Tenants/Mapping/TenantMappingProfile.cs
git commit -m "feat(mapping): add Tenant mapping profile for Mappster"
```

---

### Task 3: Create Phase 1 Audit, Auth, and User Mapping Profiles

**Files:**
- Create: `src/FashionSaaS.Application/AuditLogs/Mapping/AuditLogMappingProfile.cs`
- Create: `src/FashionSaaS.Application/Auth/Mapping/AuthMappingProfile.cs`
- Create: `src/FashionSaaS.Application/Users/Mapping/UserMappingProfile.cs`

**Interfaces:**
- Consumes: Phase 1 entities and DTOs (AuditLog, User, LoginResponse, etc.)
- Produces: Mappster profiles for Phase 1 auth/user/audit features

- [ ] **Step 1: Create AuditLog mapping profile**

Create `src/FashionSaaS.Application/AuditLogs/Mapping/AuditLogMappingProfile.cs`:

```csharp
using Mapster;
using FashionSaaS.Domain.Entities;
using FashionSaaS.Application.AuditLogs.DTOs;

namespace FashionSaaS.Application.AuditLogs.Mapping;

public class AuditLogMappingProfile : IRegister
{
    public void Register(TypeAdapterBuilder config)
    {
        config.NewConfig<AuditLog, AuditLogResponse>()
            .Map(dest => dest.Id, src => src.Id)
            .Map(dest => dest.UserId, src => src.UserId)
            .Map(dest => dest.TenantId, src => src.TenantId)
            .Map(dest => dest.Entity, src => src.Entity)
            .Map(dest => dest.Action, src => src.Action)
            .Map(dest => dest.EntityId, src => src.EntityId)
            .Map(dest => dest.OldValues, src => src.OldValues)
            .Map(dest => dest.NewValues, src => src.NewValues)
            .Map(dest => dest.IpAddress, src => src.IpAddress)
            .Map(dest => dest.UserAgent, src => src.UserAgent)
            .Map(dest => dest.Timestamp, src => src.Timestamp);
    }
}
```

- [ ] **Step 2: Create Auth mapping profile**

Create `src/FashionSaaS.Application/Auth/Mapping/AuthMappingProfile.cs`:

```csharp
using Mapster;
using FashionSaaS.Domain.Entities;
using FashionSaaS.Application.Auth.DTOs;

namespace FashionSaaS.Application.Auth.Mapping;

public class AuthMappingProfile : IRegister
{
    public void Register(TypeAdapterBuilder config)
    {
        config.NewConfig<User, LoginResponse>()
            .Map(dest => dest.UserId, src => src.Id)
            .Map(dest => dest.Email, src => src.Email)
            .Map(dest => dest.TenantSlug, src => src.Tenant == null ? "" : src.Tenant.Slug)
            .Ignore(dest => dest.AccessToken)
            .Ignore(dest => dest.RefreshToken)
            .Ignore(dest => dest.ExpiresIn);
    }
}
```

- [ ] **Step 3: Create User mapping profile**

Create `src/FashionSaaS.Application/Users/Mapping/UserMappingProfile.cs`:

```csharp
using Mapster;
using FashionSaaS.Domain.Entities;
using FashionSaaS.Application.Users.DTOs;

namespace FashionSaaS.Application.Users.Mapping;

public class UserMappingProfile : IRegister
{
    public void Register(TypeAdapterBuilder config)
    {
        config.NewConfig<User, UserResponse>()
            .Map(dest => dest.Id, src => src.Id)
            .Map(dest => dest.Email, src => src.Email)
            .Map(dest => dest.FirstName, src => src.FirstName)
            .Map(dest => dest.LastName, src => src.LastName)
            .Map(dest => dest.Role, src => src.Role)
            .Map(dest => dest.IsActive, src => src.IsActive)
            .Map(dest => dest.TwoFactorEnabled, src => src.TwoFactorEnabled)
            .Map(dest => dest.CreatedAt, src => src.CreatedAt);

        config.NewConfig<CreateUserRequest, User>()
            .Map(dest => dest.Email, src => src.Email)
            .Map(dest => dest.FirstName, src => src.FirstName)
            .Map(dest => dest.LastName, src => src.LastName)
            .Map(dest => dest.Role, src => src.Role)
            .Ignore(dest => dest.Id)
            .Ignore(dest => dest.TenantId)
            .Ignore(dest => dest.PasswordHash)
            .Ignore(dest => dest.IsActive)
            .Ignore(dest => dest.CreatedAt)
            .Ignore(dest => dest.UpdatedAt);
    }
}
```

- [ ] **Step 4: Build and test Phase 1**

```bash
dotnet build src/FashionSaaS.Application/FashionSaaS.Application.csproj
dotnet test tests/FashionSaaS.Application.Tests/FashionSaaS.Application.Tests.csproj --filter "AuditLogServiceTests|AuthServiceTests|UserServiceTests" -c Debug
```

Expected: Build succeeds, all Phase 1 user/auth/audit tests pass

- [ ] **Step 5: Commit**

```bash
git add src/FashionSaaS.Application/AuditLogs/Mapping/AuditLogMappingProfile.cs
git add src/FashionSaaS.Application/Auth/Mapping/AuthMappingProfile.cs
git add src/FashionSaaS.Application/Users/Mapping/UserMappingProfile.cs
git commit -m "feat(mapping): add Phase 1 audit, auth, and user mapping profiles"
```

---

### Task 4: Create Phase 1 Remaining Mapping Profiles (Subscription, BankAccount, LoginAttempt, MFA, Payments)

**Files:**
- Create: `src/FashionSaaS.Application/Subscriptions/Mapping/SubscriptionMappingProfile.cs`
- Create: `src/FashionSaaS.Application/BankAccounts/Mapping/BankAccountMappingProfile.cs`
- Create: `src/FashionSaaS.Application/LoginAttempts/Mapping/LoginAttemptMappingProfile.cs`
- Create: `src/FashionSaaS.Application/Mfa/Mapping/MfaMappingProfile.cs`
- Create: `src/FashionSaaS.Application/Payments/Mapping/PaymentMappingProfile.cs`
- Create: `src/FashionSaaS.Application/SubscriptionPlans/Mapping/SubscriptionPlanMappingProfile.cs`

**Interfaces:**
- Consumes: Phase 1 subscription, payment, and MFA entities and DTOs
- Produces: Mappster profiles for remaining Phase 1 features

Due to length constraints, I'll provide a summary pattern. Each profile follows this structure:

```csharp
config.NewConfig<EntityType, ResponseType>()
  .Map(dest => dest.Property, src => src.Property)
  // ... all properties mapped
  .Ignore(...) // for unmappable properties

config.NewConfig<RequestType, EntityType>()
  .Map(dest => dest.Property, src => src.Property)
  .Ignore(dest => dest.Id) // IDs set by app
  .Ignore(dest => dest.CreatedAt) // Timestamps set by app
```

Create each profile with complete property mappings matching existing DTO structures.

- [ ] **Step 1-5: Create all 6 remaining Phase 1 profiles** (following the pattern above)

For each profile:
- Map all properties from entity → DTO
- Map request DTOs → entity
- Ignore generated/audit fields (Id, CreatedAt, UpdatedAt, etc.)

- [ ] **Step 6: Build and test all Phase 1**

```bash
dotnet build src/FashionSaaS.Application/FashionSaaS.Application.csproj
dotnet test tests/FashionSaaS.Application.Tests/FashionSaaS.Application.Tests.csproj -c Debug | grep "Passed"
```

Expected: All ~174 Phase 1 tests pass

- [ ] **Step 7: Commit all Phase 1 profiles**

```bash
git add src/FashionSaaS.Application/*/Mapping/*MappingProfile.cs
git commit -m "feat(mapping): add all Phase 1 mapping profiles (subscription, bank account, MFA, payment, login attempts)"
```

---

## Phase 3: Create Mapping Profiles for Phase 2 Entities

### Task 5: Create Phase 2 Catalog Mapping Profiles (Category, Product, ProductVariant, ProductImage, Inventory)

**Files:**
- Create: `src/FashionSaaS.Application/Categories/Mapping/CategoryMappingProfile.cs`
- Create: `src/FashionSaaS.Application/Products/Mapping/ProductMappingProfile.cs`
- Create: `src/FashionSaaS.Application/ProductVariants/Mapping/ProductVariantMappingProfile.cs`
- Create: `src/FashionSaaS.Application/ProductImages/Mapping/ProductImageMappingProfile.cs`
- Create: `src/FashionSaaS.Application/Inventory/Mapping/InventoryMappingProfile.cs`

**Interfaces:**
- Consumes: Phase 2 catalog entities and DTOs
- Produces: Mappster profiles for catalog features

- [ ] **Step 1-5: Create all 5 Phase 2 catalog mapping profiles**

Each profile maps:
- Entity → Response DTO (for API responses)
- Request DTO → Entity (for API inputs)
- Special handling: ProductVariants include nested images/stock, Products include variants

Example:

```csharp
config.NewConfig<Category, CategoryResponse>()
    .Map(dest => dest.Id, src => src.Id)
    .Map(dest => dest.Name, src => src.Name)
    .Map(dest => dest.Slug, src => src.Slug)
    .Map(dest => dest.Description, src => src.Description)
    .Map(dest => dest.ParentCategoryId, src => src.ParentCategoryId)
    .Map(dest => dest.SortOrder, src => src.SortOrder)
    .Map(dest => dest.IsActive, src => src.IsActive)
    .Map(dest => dest.CreatedAt, src => src.CreatedAt)
    .Map(dest => dest.UpdatedAt, src => src.UpdatedAt);

config.NewConfig<CreateCategoryRequest, Category>()
    .Map(dest => dest.Name, src => src.Name)
    .Map(dest => dest.Slug, src => src.Slug)
    .Map(dest => dest.Description, src => src.Description)
    .Map(dest => dest.ParentCategoryId, src => src.ParentCategoryId)
    .Map(dest => dest.SortOrder, src => src.SortOrder)
    .Ignore(dest => dest.Id)
    .Ignore(dest => dest.TenantId)
    .Ignore(dest => dest.IsActive)
    .Ignore(dest => dest.CreatedAt)
    .Ignore(dest => dest.UpdatedAt);
```

- [ ] **Step 6: Build and test catalog features**

```bash
dotnet build src/FashionSaaS.Application/FashionSaaS.Application.csproj
dotnet test tests/FashionSaaS.Application.Tests/FashionSaaS.Application.Tests.csproj --filter "CategoryServiceTests|ProductServiceTests|ProductVariantServiceTests|ProductImageServiceTests|InventoryServiceTests" -c Debug
```

Expected: All catalog tests pass

- [ ] **Step 7: Commit**

```bash
git add src/FashionSaaS.Application/Categories/Mapping/CategoryMappingProfile.cs
git add src/FashionSaaS.Application/Products/Mapping/ProductMappingProfile.cs
git add src/FashionSaaS.Application/ProductVariants/Mapping/ProductVariantMappingProfile.cs
git add src/FashionSaaS.Application/ProductImages/Mapping/ProductImageMappingProfile.cs
git add src/FashionSaaS.Application/Inventory/Mapping/InventoryMappingProfile.cs
git commit -m "feat(mapping): add Phase 2 catalog entity mapping profiles"
```

---

### Task 6: Create Phase 2 Customer-Related Mapping Profiles (Customer, Discount, Review, Wishlist)

**Files:**
- Create: `src/FashionSaaS.Application/Customers/Mapping/CustomerMappingProfile.cs`
- Create: `src/FashionSaaS.Application/Discounts/Mapping/DiscountMappingProfile.cs`
- Create: `src/FashionSaaS.Application/Reviews/Mapping/ReviewMappingProfile.cs`
- Create: `src/FashionSaaS.Application/Wishlists/Mapping/WishlistMappingProfile.cs`

**Interfaces:**
- Consumes: Phase 2 customer-related entities and DTOs
- Produces: Mappster profiles for customer features

- [ ] **Step 1-4: Create all 4 customer-related mapping profiles**

Following the same pattern as Task 5, map all properties between entities and DTOs.

- [ ] **Step 5: Build and test customer features**

```bash
dotnet build src/FashionSaaS.Application/FashionSaaS.Application.csproj
dotnet test tests/FashionSaaS.Application.Tests/FashionSaaS.Application.Tests.csproj --filter "CustomerServiceTests|DiscountServiceTests|ReviewServiceTests|WishlistServiceTests" -c Debug
```

Expected: All customer feature tests pass

- [ ] **Step 6: Commit**

```bash
git add src/FashionSaaS.Application/Customers/Mapping/CustomerMappingProfile.cs
git add src/FashionSaaS.Application/Discounts/Mapping/DiscountMappingProfile.cs
git add src/FashionSaaS.Application/Reviews/Mapping/ReviewMappingProfile.cs
git add src/FashionSaaS.Application/Wishlists/Mapping/WishlistMappingProfile.cs
git commit -m "feat(mapping): add Phase 2 customer-related entity mapping profiles"
```

---

## Phase 4: Verification and Full Test Run

### Task 7: Run Full Test Suite and Verify All 354 Tests Pass

**Files:**
- No files created/modified (verification only)

**Interfaces:**
- Consumes: All mapping profiles created in Tasks 1-6
- Produces: Verification that all tests pass with Mappster in place

- [ ] **Step 1: Run all Application tests**

```bash
dotnet test tests/FashionSaaS.Application.Tests/FashionSaaS.Application.Tests.csproj -c Debug
```

Expected: All tests PASS (should show "Passed: X, Failed: 0")

- [ ] **Step 2: Run all Infrastructure tests**

```bash
dotnet test tests/FashionSaaS.Infrastructure.Tests/FashionSaaS.Infrastructure.Tests.csproj -c Debug
```

Expected: All 80 infrastructure tests PASS

- [ ] **Step 3: Build in Release mode**

```bash
dotnet build src/FashionSaaS.API/FashionSaaS.API.csproj -c Release
```

Expected: Release build succeeds (NU1701 warnings OK)

- [ ] **Step 4: Document final test results**

Record summary:
- Application Tests: X/X passing
- Infrastructure Tests: 80/80 passing
- Total: 354/354 passing
- Release Build: SUCCESS

- [ ] **Step 5: No commit needed** (verification only)

---

## Phase 5: QA Testing Plan

### Task 8: Prepare QA Test Plan and Execute Critical Path Testing

**Files:**
- Create: `docs/superpowers/qa/2026-06-30-mappster-qa-test-plan.md`
- Reference: All mapping profiles created in Tasks 1-6

**Interfaces:**
- Consumes: Mappster implementation and mapping profiles
- Produces: Comprehensive QA test report

### Critical Testing Areas:

1. **Mapping Correctness:**
   - Entity → DTO mappings produce correct output
   - Request DTO → Entity mappings set correct fields
   - Nested mappings (e.g., Product → ProductResponse with variants) work correctly
   - Null handling works as expected

2. **Data Integrity:**
   - No data loss in bidirectional mappings
   - Unmapped fields remain unchanged (audit fields, timestamps)
   - Relationships are preserved (foreign keys, navigation properties)

3. **Edge Cases:**
   - Empty collections map correctly
   - Null values in DTOs don't corrupt entities
   - Enum mappings are correct
   - Complex types (objects, lists) map properly

4. **Performance:**
   - Mapping performance is acceptable
   - No N+1 query issues introduced
   - Memory usage is reasonable

5. **Regression Testing:**
   - All Phase 1 features still work (Users, Auth, Tenants, Subscriptions, etc.)
   - All Phase 2 features still work (Catalog, Customers, Discounts, etc.)
   - API responses are unchanged
   - Database queries unchanged

- [ ] **Step 1: Create QA test plan document**

Create `docs/superpowers/qa/2026-06-30-mappster-qa-test-plan.md` with:
- Test objectives
- Test scope
- Critical test scenarios
- Expected results

- [ ] **Step 2: Execute manual smoke tests**

Test 3 critical flows:
1. Create Tenant → Verify response DTO has all fields
2. Create Product → Verify response includes variants, images
3. Create Review → Verify customer relationship preserved

For each flow:
- Inspect API response in Postman or curl
- Verify all expected fields present
- Verify data types correct
- Verify no extra/unexpected fields

- [ ] **Step 3: Run automated test suite**

```bash
dotnet test tests/ -c Debug
```

Expected: 354/354 tests pass, no regressions

- [ ] **Step 4: Verify database state consistency**

Sample checks:
- Category mappings preserve parent-child relationships
- Product images correctly associated with variants
- Discount calculations still work correctly
- Review ratings aggregated properly

- [ ] **Step 5: Document QA results**

Create test results document:
- All tests passed: Y/N
- All smoke tests passed: Y/N
- No regressions detected: Y/N
- Data integrity verified: Y/N
- Ready for deployment: Y/N

- [ ] **Step 6: Final commit**

```bash
git add docs/superpowers/qa/2026-06-30-mappster-qa-test-plan.md
git add docs/superpowers/qa/2026-06-30-mappster-qa-test-results.md
git commit -m "docs(qa): add Mappster implementation QA test plan and results"
```

---

## Summary

**Total Commits:** ~8 commits across all tasks
- Mappster infrastructure + Phase 1 mappings: ~4 commits
- Phase 2 catalog mappings: 1 commit
- Phase 2 customer mappings: 1 commit
- QA testing and docs: 1 commit
- Memory/docs updates: 1 commit

**Tests:** All 354 tests must pass before completion
- Phase 1: ~174 tests
- Phase 2: ~180 tests

**Success Criteria:**
- ✅ Mappster NuGet packages added and configured
- ✅ All mapping profiles created (Phase 1 + Phase 2)
- ✅ Dependency injection wired up
- ✅ All 354 tests passing
- ✅ Release build successful
- ✅ QA testing completed
- ✅ No regressions detected

---

## Execution Options

Plan complete and saved to `docs/superpowers/plans/2026-06-30-mappster-migration.md`.

**Two execution options:**

**1. Subagent-Driven (recommended)** - I dispatch fresh subagents per task batch, review between batches, fast iteration

**2. Inline Execution** - Execute tasks in this session using executing-plans, batch execution with checkpoints

**Which approach?** (Type 1 or 2)
