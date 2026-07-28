# Phase 9a — Manual Payment Proof for Customer Orders — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the mock card-payment step at checkout with a mandatory payment-proof upload (image/PDF) that the tenant manually reviews before the order proceeds — the WooCommerce "Direct Bank Transfer" pattern, with no payment gateway.

**Architecture:** Proof files are written through a new `IPaymentProofStorageService` abstraction (local filesystem now, Azure Blob later via a one-line DI swap) and served back by streaming through backend endpoints, so the DB stores only an opaque storage key — never a URL. The order is created as `OrderStatus.Pending` (= "on hold, awaiting proof review"); the tenant's existing `confirm` endpoint becomes the approve action (now guarded to require a proof) and the existing `cancel` endpoint is the reject action. No new order statuses.

**Tech Stack:** .NET 10, ASP.NET Core, EF Core 10 (SQL Server), FluentValidation, Mapster, xUnit 2.9.3 + FluentAssertions 8.10.0 + Moq 4.20.72, Angular 21 storefront.

**Spec:** `docs/superpowers/specs/2026-07-25-phase9a-order-payment-proof-design.md`

## Global Constraints

- **No new third-party packages.** Every task uses the BCL or packages already referenced. If something appears to need a new NuGet/npm package, STOP and ask.
- **All `.cs` edits go through Serena MCP tools**, never native Edit/Write — a `PreToolUse` hook blocks native writes on `.cs`. TypeScript/HTML/SCSS in `fashionsaas-storefront` uses native tools.
- **Verification gate for every `.cs` change:** `dotnet build` (warnings-as-errors) **and** `mcp__serena__get_diagnostics_for_file` (`min_severity: 2`) on every touched file. `dotnet build` alone does not catch IDE naming rules such as `IDE1006`.
- **Allowed proof content types (allowlist, never a blocklist):** `image/jpeg`, `image/png`, `image/webp`, `application/pdf`.
- **Max proof size: 10 MB** = `10485760` bytes. Same value in `PaymentProofStorageSettings.MaxFileSizeBytes`, the `[RequestSizeLimit]`, and the storefront's client-side check.
- **Server-generated filenames only.** The client's filename is stored as a display string and must never contribute to a storage path.
- **Cross-tenant / cross-customer proof reads return `404`, never `403`** — a `403` leaks that the order exists.
- **Primary constructors ARE this codebase's convention** for services/controllers (see `ProductImageService`, `ProductImagesController`). Match the surrounding code, not the generic template in the root `CLAUDE.md`.
- **Repository `AddAsync`/`UpdateAsync`/`DeleteAsync` take NO `CancellationToken`** (see `IGenericRepository<T>`). Query methods do.
- **Never commit** unless the human explicitly asks. Steps below end with a `git commit` command; run it only when the human has authorised committing for this run.

---

## File Structure

**New files**

| Path | Responsibility |
|---|---|
| `src/FashionSaaS.Application/Interfaces/IPaymentProofStorageService.cs` | Storage abstraction (the Azure swap point) |
| `src/FashionSaaS.Application/Configuration/PaymentProofStorageSettings.cs` | Options POCO: root path + max size |
| `src/FashionSaaS.Application/Orders/PaymentProofContentTypes.cs` | Pure allowlist + extension map + magic-number check |
| `src/FashionSaaS.Infrastructure/Services/LocalFilePaymentProofStorageService.cs` | Local-disk implementation |
| `src/FashionSaaS.Domain/Entities/OrderPaymentProof.cs` | The proof row |
| `src/FashionSaaS.Application/Interfaces/IOrderPaymentProofRepository.cs` | Repository contract |
| `src/FashionSaaS.Infrastructure/Persistence/Repositories/OrderPaymentProofRepository.cs` | EF implementation |
| `src/FashionSaaS.Infrastructure/Persistence/Configurations/OrderPaymentProofConfiguration.cs` | EF mapping |
| `src/FashionSaaS.Application/Orders/DTOs/PaymentProofFileDto.cs` | Streamed-file result carrier |
| `src/FashionSaaS.Application/Tenants/Validators/UpdateTenantRequestValidator.cs` | First validator for tenant update (none exists today) |
| `src/FashionSaaS.API/Controllers/Public/PublicPaymentInstructionsController.cs` | Public `GET /api/{slug}/payment-instructions` |
| `tests/FashionSaaS.Application.Tests/Orders/PaymentProofContentTypesTests.cs` | Task 1 tests |
| `tests/FashionSaaS.Infrastructure.Tests/Services/LocalFilePaymentProofStorageServiceTests.cs` | Task 2 tests |
| `tests/FashionSaaS.Application.Tests/Orders/OrderPaymentProofTests.cs` | Tasks 4–5 service tests |

**Modified files**

| Path | Change |
|---|---|
| `src/FashionSaaS.Domain/Entities/Order.cs` | Drop `CardLast4`; add `PaymentProof` nav |
| `src/FashionSaaS.Domain/Entities/Tenant.cs` | Add `PaymentInstructions` |
| `src/FashionSaaS.Infrastructure/Persistence/Configurations/OrderConfiguration.cs` | Drop `CardLast4` mapping |
| `src/FashionSaaS.Infrastructure/Persistence/Configurations/TenantConfiguration.cs` | Map `PaymentInstructions` |
| `src/FashionSaaS.Infrastructure/DependencyInjection.cs` | Bind settings; register storage + repository |
| `src/FashionSaaS.Application/Orders/DTOs/OrderDtos.cs` | Delete `CreateOrderPaymentDto`; drop `PaymentInfo` |
| `src/FashionSaaS.Application/Orders/Validators/CreateOrderRequestValidator.cs` | Delete card rules + regex helpers |
| `src/FashionSaaS.Application/Orders/OrderService.cs` | Proof persistence, confirm guard, proof reads |
| `src/FashionSaaS.API/Controllers/Store/StoreOrdersController.cs` | Multipart create; proof download |
| `src/FashionSaaS.API/Controllers/Tenant/OrdersController.cs` | Proof download |
| `src/FashionSaaS.API/Constants/ApiUrl.cs` | 3 new routes |
| `src/FashionSaaS.Application/Tenants/DTOs/{UpdateTenantRequest,TenantResponse}.cs` | Add `PaymentInstructions` |
| `src/FashionSaaS.Application/Tenants/TenantService.cs` | Persist + return `PaymentInstructions` |
| `tests/FashionSaaS.Application.Tests/Orders/{CreateOrderRequestValidatorTests,OrderServiceTests,OrderWorkflowE2ETests}.cs` | Remove card fixtures |
| `tests/FashionSaaS.Infrastructure.Tests/Repositories/OrderRepositoryTests.cs` | Remove `CardLast4 = "1111"` |
| `fashionsaas-storefront/src/app/features/checkout/**` | Upload step replaces card form |
| `fashionsaas-storefront/src/app/admin/orders/order-detail/**` | Render proof beside Confirm/Cancel |
| `fashionsaas-storefront/src/app/admin/shared/services/order-admin.service.ts` | `getPaymentProof` (blob) |
| `fashionsaas-storefront/src/app/features/account/services/account.service.ts` | `getOrderPaymentProof` (blob) |
| `fashionsaas-storefront/src/app/features/account/components/order-history/**` | "View payment proof" action |

---

## Task 1: Content-type allowlist and magic-number verification

A pure, dependency-free helper. Everything else depends on it, and it is the security boundary that stops a renamed executable from reaching storage.

**Files:**
- Create: `src/FashionSaaS.Application/Orders/PaymentProofContentTypes.cs`
- Test: `tests/FashionSaaS.Application.Tests/Orders/PaymentProofContentTypesTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `public static class PaymentProofContentTypes` with
  `const long MaxFileSizeBytes = 10485760;`,
  `static bool IsAllowed(string? contentType)`,
  `static string ExtensionFor(string contentType)`,
  `static bool HeaderMatches(ReadOnlySpan<byte> header, string contentType)`.

- [ ] **Step 1: Write the failing tests**

Create `tests/FashionSaaS.Application.Tests/Orders/PaymentProofContentTypesTests.cs`:

```csharp
using System.Text;
using FashionSaaS.Application.Orders;
using FluentAssertions;

namespace FashionSaaS.Application.Tests.Orders;

public class PaymentProofContentTypesTests
{
    private static readonly byte[] JpegHeader = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10];
    private static readonly byte[] PngHeader = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] PdfHeader = Encoding.ASCII.GetBytes("%PDF-1.7");

    private static byte[] WebpHeader()
    {
        var header = new byte[12];
        Encoding.ASCII.GetBytes("RIFF").CopyTo(header, 0);
        Encoding.ASCII.GetBytes("WEBP").CopyTo(header, 8);
        return header;
    }

    [Theory]
    [InlineData("image/jpeg")]
    [InlineData("image/png")]
    [InlineData("image/webp")]
    [InlineData("application/pdf")]
    [InlineData("IMAGE/JPEG")]
    public void IsAllowed_AllowlistedType_ReturnsTrue(string contentType)
        => PaymentProofContentTypes.IsAllowed(contentType).Should().BeTrue();

    [Theory]
    [InlineData("application/octet-stream")]
    [InlineData("text/html")]
    [InlineData("image/svg+xml")]
    [InlineData("")]
    [InlineData(null)]
    public void IsAllowed_NonAllowlistedType_ReturnsFalse(string? contentType)
        => PaymentProofContentTypes.IsAllowed(contentType).Should().BeFalse();

    [Theory]
    [InlineData("image/jpeg", ".jpg")]
    [InlineData("image/png", ".png")]
    [InlineData("image/webp", ".webp")]
    [InlineData("application/pdf", ".pdf")]
    public void ExtensionFor_AllowlistedType_ReturnsExpectedExtension(string contentType, string expected)
        => PaymentProofContentTypes.ExtensionFor(contentType).Should().Be(expected);

    [Fact]
    public void ExtensionFor_NonAllowlistedType_Throws()
    {
        Action act = () => PaymentProofContentTypes.ExtensionFor("text/html");
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void HeaderMatches_JpegHeaderWithJpegType_ReturnsTrue()
        => PaymentProofContentTypes.HeaderMatches(JpegHeader, "image/jpeg").Should().BeTrue();

    [Fact]
    public void HeaderMatches_PngHeaderWithPngType_ReturnsTrue()
        => PaymentProofContentTypes.HeaderMatches(PngHeader, "image/png").Should().BeTrue();

    [Fact]
    public void HeaderMatches_WebpHeaderWithWebpType_ReturnsTrue()
        => PaymentProofContentTypes.HeaderMatches(WebpHeader(), "image/webp").Should().BeTrue();

    [Fact]
    public void HeaderMatches_PdfHeaderWithPdfType_ReturnsTrue()
        => PaymentProofContentTypes.HeaderMatches(PdfHeader, "application/pdf").Should().BeTrue();

    [Fact]
    public void HeaderMatches_ExecutableRenamedAsPdf_ReturnsFalse()
    {
        // "MZ" — a Windows PE executable claiming to be a PDF. This is the attack the check exists for.
        byte[] mzHeader = [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00];
        PaymentProofContentTypes.HeaderMatches(mzHeader, "application/pdf").Should().BeFalse();
    }

    [Fact]
    public void HeaderMatches_PngBytesClaimingJpeg_ReturnsFalse()
        => PaymentProofContentTypes.HeaderMatches(PngHeader, "image/jpeg").Should().BeFalse();

    [Fact]
    public void HeaderMatches_RiffWithoutWebpMarker_ReturnsFalse()
    {
        var riffOnly = new byte[12];
        Encoding.ASCII.GetBytes("RIFF").CopyTo(riffOnly, 0);
        Encoding.ASCII.GetBytes("AVI ").CopyTo(riffOnly, 8);
        PaymentProofContentTypes.HeaderMatches(riffOnly, "image/webp").Should().BeFalse();
    }

    [Fact]
    public void HeaderMatches_HeaderTooShort_ReturnsFalse()
        => PaymentProofContentTypes.HeaderMatches([0xFF], "image/jpeg").Should().BeFalse();

    [Fact]
    public void HeaderMatches_EmptyHeader_ReturnsFalse()
        => PaymentProofContentTypes.HeaderMatches([], "application/pdf").Should().BeFalse();

    [Fact]
    public void MaxFileSizeBytes_IsTenMegabytes()
        => PaymentProofContentTypes.MaxFileSizeBytes.Should().Be(10485760);
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/FashionSaaS.Application.Tests --filter PaymentProofContentTypesTests`
Expected: FAIL — build error, `PaymentProofContentTypes` does not exist.

- [ ] **Step 3: Implement the helper**

Create `src/FashionSaaS.Application/Orders/PaymentProofContentTypes.cs` **via Serena `create_text_file`**:

```csharp
using System.Text;

namespace FashionSaaS.Application.Orders;

/// <summary>
/// The allowlist of accepted payment-proof file types, plus magic-number verification.
/// A client-declared Content-Type is not trusted on its own: <see cref="HeaderMatches"/>
/// confirms the file's leading bytes actually match the declared type, so a renamed
/// executable can never reach storage. Allowlist, never a blocklist.
/// </summary>
public static class PaymentProofContentTypes
{
    /// <summary>Maximum accepted proof size (10 MB).</summary>
    public const long MaxFileSizeBytes = 10485760;

    public const string Jpeg = "image/jpeg";
    public const string Png = "image/png";
    public const string Webp = "image/webp";
    public const string Pdf = "application/pdf";

    private static readonly Dictionary<string, string> ExtensionsByContentType =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [Jpeg] = ".jpg",
            [Png] = ".png",
            [Webp] = ".webp",
            [Pdf] = ".pdf"
        };

    public static bool IsAllowed(string? contentType)
        => contentType is not null && ExtensionsByContentType.ContainsKey(contentType);

    public static string ExtensionFor(string contentType)
        => ExtensionsByContentType.TryGetValue(contentType, out var extension)
            ? extension
            : throw new ArgumentOutOfRangeException(nameof(contentType), "Unsupported payment proof content type.");

    /// <summary>
    /// True when <paramref name="header"/> (the file's leading bytes) carries the signature
    /// expected for <paramref name="contentType"/>. Pass at least the first 12 bytes.
    /// </summary>
    public static bool HeaderMatches(ReadOnlySpan<byte> header, string contentType)
    {
        if (!IsAllowed(contentType))
            return false;

        if (string.Equals(contentType, Jpeg, StringComparison.OrdinalIgnoreCase))
            return header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF;

        if (string.Equals(contentType, Png, StringComparison.OrdinalIgnoreCase))
            return header.Length >= 8
                   && header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47
                   && header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A;

        if (string.Equals(contentType, Webp, StringComparison.OrdinalIgnoreCase))
            return header.Length >= 12
                   && StartsWithAscii(header, "RIFF")
                   && StartsWithAscii(header[8..], "WEBP");

        // Pdf
        return header.Length >= 4 && StartsWithAscii(header, "%PDF");
    }

    private static bool StartsWithAscii(ReadOnlySpan<byte> bytes, string ascii)
    {
        if (bytes.Length < ascii.Length)
            return false;

        Span<byte> expected = stackalloc byte[ascii.Length];
        Encoding.ASCII.GetBytes(ascii, expected);
        return bytes[..ascii.Length].SequenceEqual(expected);
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/FashionSaaS.Application.Tests --filter PaymentProofContentTypesTests`
Expected: PASS — 20 passed, 0 failed.

- [ ] **Step 5: Run the full verification gate**

Run: `dotnet build FashionSaaS.sln`
Expected: `0 Warning(s) 0 Error(s)`.

Then run `mcp__serena__get_diagnostics_for_file` (`min_severity: 2`) on `src/FashionSaaS.Application/Orders/PaymentProofContentTypes.cs`.
Expected: no diagnostics.

- [ ] **Step 6: Commit**

```bash
git add src/FashionSaaS.Application/Orders/PaymentProofContentTypes.cs tests/FashionSaaS.Application.Tests/Orders/PaymentProofContentTypesTests.cs
git commit -m "feat(orders): add payment-proof content-type allowlist and magic-number check"
```

---

## Task 2: Storage abstraction, settings, and local implementation

The Azure swap point. After this task, switching to Blob storage is one new class plus one changed DI line.

**Files:**
- Create: `src/FashionSaaS.Application/Interfaces/IPaymentProofStorageService.cs`
- Create: `src/FashionSaaS.Application/Configuration/PaymentProofStorageSettings.cs`
- Create: `src/FashionSaaS.Infrastructure/Services/LocalFilePaymentProofStorageService.cs`
- Modify: `src/FashionSaaS.Infrastructure/DependencyInjection.cs` (options block ends line 31; storage registrations around line 52)
- Modify: `src/FashionSaaS.API/appsettings.json`
- Test: `tests/FashionSaaS.Infrastructure.Tests/Services/LocalFilePaymentProofStorageServiceTests.cs`

**Interfaces:**
- Consumes: `PaymentProofContentTypes.MaxFileSizeBytes` (Task 1).
- Produces:
  - `IPaymentProofStorageService.SaveAsync(Stream content, string storageKey, CancellationToken ct = default) : Task`
  - `IPaymentProofStorageService.OpenReadAsync(string storageKey, CancellationToken ct = default) : Task<Stream>`
  - `IPaymentProofStorageService.DeleteAsync(string storageKey, CancellationToken ct = default) : Task`
  - `PaymentProofStorageSettings { const string SectionName = "PaymentProofStorage"; string RootPath; long MaxFileSizeBytes; }`

> **Design note for the implementer:** the *caller* builds the storage key (Task 4 composes `{tenantId}/{orderId}/{guid}{ext}`), and the storage service only validates and honours it. That keeps the interface identical for Blob, where the key is the blob name.

- [ ] **Step 1: Write the failing tests**

Create `tests/FashionSaaS.Infrastructure.Tests/Services/LocalFilePaymentProofStorageServiceTests.cs`:

```csharp
using System.Text;
using FashionSaaS.Application.Configuration;
using FashionSaaS.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FashionSaaS.Infrastructure.Tests.Services;

public sealed class LocalFilePaymentProofStorageServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "proof-tests-" + Guid.NewGuid().ToString("N"));

    private LocalFilePaymentProofStorageService CreateService()
        => new(
            Options.Create(new PaymentProofStorageSettings { RootPath = _root, MaxFileSizeBytes = 10485760 }),
            NullLogger<LocalFilePaymentProofStorageService>.Instance);

    private static MemoryStream Content(string text) => new(Encoding.UTF8.GetBytes(text));

    [Fact]
    public async Task SaveAsync_ThenOpenReadAsync_RoundTripsBytesUnchanged()
    {
        LocalFilePaymentProofStorageService service = CreateService();
        var key = $"{Guid.NewGuid()}/{Guid.NewGuid()}/{Guid.NewGuid():N}.pdf";

        await service.SaveAsync(Content("proof-bytes"), key);

        await using Stream read = await service.OpenReadAsync(key);
        using var reader = new StreamReader(read);
        (await reader.ReadToEndAsync()).Should().Be("proof-bytes");
    }

    [Fact]
    public async Task SaveAsync_CreatesNestedDirectories()
    {
        LocalFilePaymentProofStorageService service = CreateService();
        var key = $"{Guid.NewGuid()}/{Guid.NewGuid()}/{Guid.NewGuid():N}.png";

        await service.SaveAsync(Content("x"), key);

        File.Exists(Path.Combine(_root, key.Replace('/', Path.DirectorySeparatorChar))).Should().BeTrue();
    }

    [Theory]
    [InlineData("../escaped.pdf")]
    [InlineData("a/../../escaped.pdf")]
    [InlineData("/absolute/escaped.pdf")]
    public async Task SaveAsync_KeyEscapingRoot_Throws(string key)
    {
        LocalFilePaymentProofStorageService service = CreateService();

        Func<Task> act = () => service.SaveAsync(Content("x"), key);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData("../escaped.pdf")]
    [InlineData("a/../../escaped.pdf")]
    public async Task OpenReadAsync_KeyEscapingRoot_Throws(string key)
    {
        LocalFilePaymentProofStorageService service = CreateService();

        Func<Task> act = () => service.OpenReadAsync(key);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task OpenReadAsync_MissingFile_ThrowsFileNotFound()
    {
        LocalFilePaymentProofStorageService service = CreateService();

        Func<Task> act = () => service.OpenReadAsync($"{Guid.NewGuid()}/{Guid.NewGuid()}/missing.pdf");

        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public async Task DeleteAsync_RemovesTheFile()
    {
        LocalFilePaymentProofStorageService service = CreateService();
        var key = $"{Guid.NewGuid()}/{Guid.NewGuid()}/{Guid.NewGuid():N}.jpg";
        await service.SaveAsync(Content("x"), key);

        await service.DeleteAsync(key);

        File.Exists(Path.Combine(_root, key.Replace('/', Path.DirectorySeparatorChar))).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_MissingFile_DoesNotThrow()
    {
        LocalFilePaymentProofStorageService service = CreateService();

        Func<Task> act = () => service.DeleteAsync($"{Guid.NewGuid()}/{Guid.NewGuid()}/missing.pdf");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DeleteAsync_KeyEscapingRoot_DoesNotThrowAndDeletesNothing()
    {
        LocalFilePaymentProofStorageService service = CreateService();
        var outside = Path.Combine(Path.GetTempPath(), "must-survive-" + Guid.NewGuid().ToString("N") + ".txt");
        await File.WriteAllTextAsync(outside, "keep me");

        try
        {
            Func<Task> act = () => service.DeleteAsync("../" + Path.GetFileName(outside));

            // Delete is best-effort and must never throw, but must also never delete outside the root.
            await act.Should().NotThrowAsync();
            File.Exists(outside).Should().BeTrue();
        }
        finally
        {
            File.Delete(outside);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/FashionSaaS.Infrastructure.Tests --filter LocalFilePaymentProofStorageServiceTests`
Expected: FAIL — build error, `LocalFilePaymentProofStorageService` / `PaymentProofStorageSettings` do not exist.

- [ ] **Step 3: Create the settings POCO**

Create `src/FashionSaaS.Application/Configuration/PaymentProofStorageSettings.cs` **via Serena `create_text_file`**:

```csharp
using System.ComponentModel.DataAnnotations;

namespace FashionSaaS.Application.Configuration;

public class PaymentProofStorageSettings
{
    public const string SectionName = "PaymentProofStorage";

    /// <summary>
    /// Root directory for locally stored payment proofs. Relative paths resolve against the
    /// content root. Ignored once an Azure Blob implementation replaces the local one.
    /// </summary>
    [Required]
    public string RootPath { get; init; } = string.Empty;

    [Range(1, 104857600)]
    public long MaxFileSizeBytes { get; init; } = 10485760;
}
```

- [ ] **Step 4: Create the storage interface**

Create `src/FashionSaaS.Application/Interfaces/IPaymentProofStorageService.cs` **via Serena `create_text_file`**:

```csharp
namespace FashionSaaS.Application.Interfaces;

/// <summary>
/// Binary storage for customer payment proofs. The single swap point between local-disk
/// storage (development) and Azure Blob Storage (deployed): implement this interface and
/// change the one registration in Infrastructure's DependencyInjection — no calling code changes.
/// <para>
/// The caller owns the storage key, so keys stay meaningful across providers (a relative path
/// locally, a blob name in Azure). Implementations must reject any key that escapes their root.
/// </para>
/// </summary>
public interface IPaymentProofStorageService
{
    Task SaveAsync(Stream content, string storageKey, CancellationToken ct = default);

    /// <summary>Opens the stored proof for reading. Throws <see cref="FileNotFoundException"/> if absent.</summary>
    Task<Stream> OpenReadAsync(string storageKey, CancellationToken ct = default);

    /// <summary>Best-effort removal used for orphan cleanup. Must never throw.</summary>
    Task DeleteAsync(string storageKey, CancellationToken ct = default);
}
```

- [ ] **Step 5: Implement local storage**

Create `src/FashionSaaS.Infrastructure/Services/LocalFilePaymentProofStorageService.cs` **via Serena `create_text_file`**:

```csharp
using FashionSaaS.Application.Configuration;
using FashionSaaS.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FashionSaaS.Infrastructure.Services;

/// <summary>
/// Local-filesystem payment-proof storage for development. Every key is resolved against the
/// configured root and rejected if it escapes it, so a crafted key can never read or write
/// outside the proof directory. Replaced wholesale by an Azure Blob implementation at deploy
/// time — see <see cref="IPaymentProofStorageService"/>.
/// </summary>
public class LocalFilePaymentProofStorageService : IPaymentProofStorageService
{
    private readonly string _root;
    private readonly ILogger<LocalFilePaymentProofStorageService> _logger;

    public LocalFilePaymentProofStorageService(
        IOptions<PaymentProofStorageSettings> options,
        ILogger<LocalFilePaymentProofStorageService> logger)
    {
        _root = Path.GetFullPath(options.Value.RootPath);
        _logger = logger;
    }

    public async Task SaveAsync(Stream content, string storageKey, CancellationToken ct = default)
    {
        var path = ResolveWithinRoot(storageKey);

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        await using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        await content.CopyToAsync(file, ct);
    }

    public Task<Stream> OpenReadAsync(string storageKey, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var path = ResolveWithinRoot(storageKey);
        if (!File.Exists(path))
            throw new FileNotFoundException("Payment proof not found.", storageKey);

        Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string storageKey, CancellationToken ct = default)
    {
        // Best-effort by contract: orphan cleanup must never surface an error to the caller,
        // whose database work has already been decided. CA1031 suppressed deliberately —
        // every exception type is swallowed here by design, not just specific ones.
#pragma warning disable CA1031
        try
        {
            var path = ResolveWithinRoot(storageKey);
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Payment proof delete failed for key {StorageKey}", storageKey);
        }
#pragma warning restore CA1031

        return Task.CompletedTask;
    }

    /// <summary>
    /// Resolves a storage key under the configured root, rejecting absolute paths and any
    /// key that traverses outside it.
    /// </summary>
    private string ResolveWithinRoot(string storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey) || Path.IsPathRooted(storageKey))
            throw new InvalidOperationException("Invalid payment proof storage key.");

        var candidate = Path.GetFullPath(Path.Combine(_root, storageKey));

        var rootWithSeparator = _root.EndsWith(Path.DirectorySeparatorChar)
            ? _root
            : _root + Path.DirectorySeparatorChar;

        if (!candidate.StartsWith(rootWithSeparator, StringComparison.Ordinal))
            throw new InvalidOperationException("Invalid payment proof storage key.");

        return candidate;
    }
}
```

- [ ] **Step 6: Bind the settings and register the service**

In `src/FashionSaaS.Infrastructure/DependencyInjection.cs`, **via Serena `replace_regex`**, insert the options binding immediately after the `CloudinarySettings` block that currently ends at line 31 — so the new block follows `.ValidateOnStart();`:

```csharp
        services.AddOptions<PaymentProofStorageSettings>()
            .Bind(configuration.GetSection(PaymentProofStorageSettings.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
```

Then, immediately after the existing line 53 `services.AddScoped<IImageStorageService, CloudinaryImageStorageService>();`, add:

```csharp
        // Payment proof storage — THE Azure swap point. To move to Azure Blob Storage, implement
        // IPaymentProofStorageService as AzureBlobPaymentProofStorageService and change only this line.
        services.AddScoped<IPaymentProofStorageService, LocalFilePaymentProofStorageService>();
```

- [ ] **Step 7: Add the configuration section**

In `src/FashionSaaS.API/appsettings.json`, add this top-level section (native Write is fine — this is JSON, not `.cs`):

```json
  "PaymentProofStorage": {
    "RootPath": "App_Data/payment-proofs",
    "MaxFileSizeBytes": 10485760
  },
```

Then append to `.gitignore`:

```
# Locally stored customer payment proofs (never committed)
App_Data/
**/App_Data/
```

- [ ] **Step 8: Run the tests to verify they pass**

Run: `dotnet test tests/FashionSaaS.Infrastructure.Tests --filter LocalFilePaymentProofStorageServiceTests`
Expected: PASS — 11 passed, 0 failed.

- [ ] **Step 9: Run the full verification gate**

Run: `dotnet build FashionSaaS.sln`
Expected: `0 Warning(s) 0 Error(s)`.

Then run `mcp__serena__get_diagnostics_for_file` (`min_severity: 2`) on the three new/modified `.cs` files.
Expected: no diagnostics.

- [ ] **Step 10: Commit**

```bash
git add src/FashionSaaS.Application/Interfaces/IPaymentProofStorageService.cs src/FashionSaaS.Application/Configuration/PaymentProofStorageSettings.cs src/FashionSaaS.Infrastructure/Services/LocalFilePaymentProofStorageService.cs src/FashionSaaS.Infrastructure/DependencyInjection.cs src/FashionSaaS.API/appsettings.json .gitignore tests/FashionSaaS.Infrastructure.Tests/Services/LocalFilePaymentProofStorageServiceTests.cs
git commit -m "feat(storage): add swappable payment-proof storage with local implementation"
```

---

## Task 3: Schema — proof entity, tenant instructions, card-field removal

One migration covers all schema change, so the database is never in a half-migrated state.

**Files:**
- Create: `src/FashionSaaS.Domain/Entities/OrderPaymentProof.cs`
- Create: `src/FashionSaaS.Application/Interfaces/IOrderPaymentProofRepository.cs`
- Create: `src/FashionSaaS.Infrastructure/Persistence/Repositories/OrderPaymentProofRepository.cs`
- Create: `src/FashionSaaS.Infrastructure/Persistence/Configurations/OrderPaymentProofConfiguration.cs`
- Modify: `src/FashionSaaS.Domain/Entities/Order.cs:24` (remove `CardLast4`), add nav property
- Modify: `src/FashionSaaS.Domain/Entities/Tenant.cs` (add `PaymentInstructions`)
- Modify: `src/FashionSaaS.Infrastructure/Persistence/Configurations/OrderConfiguration.cs:22`
- Modify: `src/FashionSaaS.Infrastructure/Persistence/Configurations/TenantConfiguration.cs`
- Modify: `src/FashionSaaS.Infrastructure/DependencyInjection.cs` (repo registration, after line 85)
- Modify: `tests/FashionSaaS.Infrastructure.Tests/Repositories/OrderRepositoryTests.cs:43`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces:
  - `OrderPaymentProof : BaseEntity` with `Guid TenantId`, `Guid OrderId`, `string StorageKey`, `string ContentType`, `string OriginalFileName`, `long SizeBytes`, `DateTime UploadedAt`, `Order Order`.
  - `Order.PaymentProof` (`OrderPaymentProof?`).
  - `Tenant.PaymentInstructions` (`string?`).
  - `IOrderPaymentProofRepository : IGenericRepository<OrderPaymentProof>` with `Task<OrderPaymentProof?> GetByOrderIdAsync(Guid orderId, CancellationToken ct = default)`.

- [ ] **Step 1: Create the entity**

Create `src/FashionSaaS.Domain/Entities/OrderPaymentProof.cs` **via Serena `create_text_file`**:

```csharp
namespace FashionSaaS.Domain.Entities;

/// <summary>
/// The customer's uploaded proof of an out-of-band payment (bank transfer, wallet, etc.).
/// Exactly one per order — enforced by a unique index on <see cref="OrderId"/>. The binary
/// lives in payment-proof storage; only the opaque <see cref="StorageKey"/> is persisted here,
/// never a URL, so the storage provider can change without touching this row.
/// </summary>
public class OrderPaymentProof : BaseEntity
{
    public Guid TenantId { get; set; }
    public Guid OrderId { get; set; }

    /// <summary>Opaque key understood only by the storage provider. Never a URL.</summary>
    public string StorageKey { get; set; } = string.Empty;

    /// <summary>Validated against the allowlist at upload; used as the download Content-Type.</summary>
    public string ContentType { get; set; } = string.Empty;

    /// <summary>The client's filename, kept for display only — never used to build a path.</summary>
    public string OriginalFileName { get; set; } = string.Empty;

    public long SizeBytes { get; set; }
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    public Order Order { get; set; } = null!;
}
```

- [ ] **Step 2: Update `Order` and `Tenant`**

In `src/FashionSaaS.Domain/Entities/Order.cs` **via Serena `replace_regex`**, delete line 24 entirely:

```csharp
    public string CardLast4 { get; set; } = string.Empty; // masked reference ONLY
```

and add this nav property immediately after the existing `public ICollection<OrderItem> Items ...` line:

```csharp

    /// <summary>
    /// The customer's payment proof. Required before the order can be confirmed —
    /// see OrderService.TransitionAsync.
    /// </summary>
    public OrderPaymentProof? PaymentProof { get; set; }
```

In `src/FashionSaaS.Domain/Entities/Tenant.cs` **via Serena `replace_regex`**, add after the `CoverImageUrl` property:

```csharp

    /// <summary>
    /// Free-text instructions telling customers where to send payment (bank details, wallet
    /// reference, etc.). Authored by the tenant and shown publicly at checkout, which is why
    /// the encrypted BankAccount record is never exposed to customers.
    /// </summary>
    public string? PaymentInstructions { get; set; }
```

- [ ] **Step 3: Create the repository contract and implementation**

Create `src/FashionSaaS.Application/Interfaces/IOrderPaymentProofRepository.cs` **via Serena `create_text_file`**:

```csharp
using FashionSaaS.Domain.Entities;

namespace FashionSaaS.Application.Interfaces;

public interface IOrderPaymentProofRepository : IGenericRepository<OrderPaymentProof>
{
    Task<OrderPaymentProof?> GetByOrderIdAsync(Guid orderId, CancellationToken ct = default);
}
```

Create `src/FashionSaaS.Infrastructure/Persistence/Repositories/OrderPaymentProofRepository.cs` **via Serena `create_text_file`**:

```csharp
using FashionSaaS.Application.Interfaces;
using FashionSaaS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FashionSaaS.Infrastructure.Persistence.Repositories;

public class OrderPaymentProofRepository(ApplicationDbContext context)
    : GenericRepository<OrderPaymentProof>(context), IOrderPaymentProofRepository
{
    public async Task<OrderPaymentProof?> GetByOrderIdAsync(Guid orderId, CancellationToken ct = default)
        => await DbSet
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.OrderId == orderId, ct);
}
```

- [ ] **Step 4: Create and update EF configurations**

Create `src/FashionSaaS.Infrastructure/Persistence/Configurations/OrderPaymentProofConfiguration.cs` **via Serena `create_text_file`**:

```csharp
using FashionSaaS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FashionSaaS.Infrastructure.Persistence.Configurations;

public class OrderPaymentProofConfiguration : IEntityTypeConfiguration<OrderPaymentProof>
{
    public void Configure(EntityTypeBuilder<OrderPaymentProof> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.StorageKey).HasMaxLength(500).IsRequired();
        builder.Property(p => p.ContentType).HasMaxLength(100).IsRequired();
        builder.Property(p => p.OriginalFileName).HasMaxLength(260).IsRequired();

        builder.HasIndex(p => p.TenantId);

        // One proof per order.
        builder.HasIndex(p => p.OrderId).IsUnique();

        builder.HasOne(p => p.Order).WithOne(o => o.PaymentProof)
            .HasForeignKey<OrderPaymentProof>(p => p.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
```

In `src/FashionSaaS.Infrastructure/Persistence/Configurations/OrderConfiguration.cs` **via Serena `replace_regex`**, delete line 22:

```csharp
        builder.Property(o => o.CardLast4).HasMaxLength(4).IsRequired();
```

In `src/FashionSaaS.Infrastructure/Persistence/Configurations/TenantConfiguration.cs` **via Serena `replace_regex`**, add after the `CoverImageUrl` line:

```csharp
        builder.Property(t => t.PaymentInstructions).HasMaxLength(2000);
```

- [ ] **Step 5: Register the repository**

In `src/FashionSaaS.Infrastructure/DependencyInjection.cs` **via Serena `replace_regex`**, add immediately after the existing `services.AddScoped<IOrderRepository, OrderRepository>();` line:

```csharp
        services.AddScoped<IOrderPaymentProofRepository, OrderPaymentProofRepository>();
```

- [ ] **Step 6: Fix the one test that sets the removed field**

In `tests/FashionSaaS.Infrastructure.Tests/Repositories/OrderRepositoryTests.cs`, delete line 43 **via Serena `replace_regex`**:

```csharp
            CardLast4 = "1111",
```

> **Re-sequencing note (added after Task 3's first implementation attempt):** the original plan had this task generate and apply the EF migration here, ending in a deliberately-broken build. That doesn't work: `dotnet ef migrations add` performs a design-time build of the whole startup-project dependency chain (API → Infrastructure → Application), and that build already fails at this point because `OrderService.cs` still references the just-removed `Order.CardLast4`. EF tooling cannot run against a solution that doesn't compile at all — a "deliberately broken build" and "generate a migration" cannot coexist in the same task. Migration generation/application therefore moves to **Task 4, Step 4b** (after the `CardLast4`/`PaymentInfo` usages are removed and the solution builds again). This task now ends after the schema/code edits, with an *uncompilable* (not just logically-incomplete) solution — expected and fine to commit as-is.

- [ ] **Step 7: Verify only the expected build errors exist, then commit**

Run `dotnet build FashionSaaS.sln`.
Expected: build FAILS. Confirm every error is a `CardLast4` or `request.PaymentInfo` reference in `OrderService.cs` or the Orders test files — nothing else. Any other error means a mistake in this task's own edits; fix it (without touching `OrderService.cs`/DTOs/validators/other Orders tests) before committing.

```bash
git add src/FashionSaaS.Domain/Entities/OrderPaymentProof.cs src/FashionSaaS.Domain/Entities/Order.cs src/FashionSaaS.Domain/Entities/Tenant.cs src/FashionSaaS.Application/Interfaces/IOrderPaymentProofRepository.cs src/FashionSaaS.Infrastructure/Persistence/Repositories/OrderPaymentProofRepository.cs src/FashionSaaS.Infrastructure/Persistence/Configurations/ src/FashionSaaS.Infrastructure/DependencyInjection.cs tests/FashionSaaS.Infrastructure.Tests/Repositories/OrderRepositoryTests.cs
git commit -m "feat(orders): add OrderPaymentProof schema, tenant payment instructions, drop card fields"
```

Note: no `Migrations/` path is staged here — the migration is generated in Task 4 once the solution builds again.

---

## Task 4: Order creation requires a payment proof

Restores the build. Removes card capture entirely, persists the proof in the same transaction as the order, and guards `confirm`.

**Files:**
- Modify: `src/FashionSaaS.Application/Orders/DTOs/OrderDtos.cs:29-41`
- Modify: `src/FashionSaaS.Application/Orders/Validators/CreateOrderRequestValidator.cs`
- Modify: `src/FashionSaaS.Application/Orders/OrderService.cs`
- Modify: `src/FashionSaaS.API/Controllers/Store/StoreOrdersController.cs`
- Modify: `tests/FashionSaaS.Application.Tests/Orders/{CreateOrderRequestValidatorTests,OrderServiceTests,OrderWorkflowE2ETests}.cs`
- Test: `tests/FashionSaaS.Application.Tests/Orders/OrderPaymentProofTests.cs`

**Interfaces:**
- Consumes: `PaymentProofContentTypes` (Task 1); `IPaymentProofStorageService` (Task 2); `OrderPaymentProof`, `IOrderPaymentProofRepository` (Task 3).
- Produces:
  - `OrderService.CreateAsync(string customerEmail, string customerFirstName, string customerLastName, string? customerPhone, CreateOrderRequest request, Guid actingUserId, string ipAddress, string userAgent, Stream proofContent, string proofFileName, string proofContentType, long proofSizeBytes, CancellationToken ct = default)`
  - `CreateOrderRequest` no longer has `PaymentInfo`; `CreateOrderPaymentDto` is deleted.

- [ ] **Step 1: Write the failing tests**

Create `tests/FashionSaaS.Application.Tests/Orders/OrderPaymentProofTests.cs`:

```csharp
using System.Text;
using FashionSaaS.Application.Common;
using FashionSaaS.Application.Interfaces;
using FashionSaaS.Application.Orders;
using FashionSaaS.Application.Orders.DTOs;
using FashionSaaS.Domain.Entities;
using FashionSaaS.Domain.Enums;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FashionSaaS.Application.Tests.Orders;

/// <summary>
/// Phase 9a: the order cannot exist without its payment proof, and cannot be confirmed without one.
/// </summary>
public class OrderPaymentProofTests
{
    private readonly Mock<IOrderRepository> _orders = new();
    private readonly Mock<IOrderPaymentProofRepository> _proofs = new();
    private readonly Mock<IPaymentProofStorageService> _storage = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Guid _tenantId = Guid.NewGuid();

    private static MemoryStream Pdf() => new(Encoding.ASCII.GetBytes("%PDF-1.7 body"));

    [Fact]
    public void HeaderMatches_GuardsTheServiceBoundary()
    {
        // The service must reject a declared type the bytes do not support.
        PaymentProofContentTypes.HeaderMatches(Encoding.ASCII.GetBytes("%PDF-1.7"), "application/pdf")
            .Should().BeTrue();
        PaymentProofContentTypes.HeaderMatches(Encoding.ASCII.GetBytes("%PDF-1.7"), "image/png")
            .Should().BeFalse();
    }

    [Fact]
    public async Task ConfirmAsync_OrderWithoutProof_Returns400_AndDoesNotChangeStatus()
    {
        var order = new Order
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            OrderNumber = "ORD-2026-000001",
            Status = OrderStatus.Pending,
            PaymentProof = null
        };
        _orders.Setup(r => r.GetByIdWithItemsAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);

        ResponseData<OrderDto> result = await CreateService().ConfirmAsync(
            order.Id, Guid.NewGuid(), "127.0.0.1", "ua");

        result.StatusCode.Should().Be(400);
        result.Message.Should().Contain("Payment proof");
        order.Status.Should().Be(OrderStatus.Pending);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ConfirmAsync_OrderWithProof_Succeeds()
    {
        var order = new Order
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            OrderNumber = "ORD-2026-000002",
            Status = OrderStatus.Pending
        };
        order.PaymentProof = new OrderPaymentProof
        {
            OrderId = order.Id,
            TenantId = _tenantId,
            StorageKey = "k",
            ContentType = "application/pdf",
            OriginalFileName = "receipt.pdf",
            SizeBytes = 10
        };
        _orders.Setup(r => r.GetByIdWithItemsAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);

        ResponseData<OrderDto> result = await CreateService().ConfirmAsync(
            order.Id, Guid.NewGuid(), "127.0.0.1", "ua");

        result.IsSuccess.Should().BeTrue();
        order.Status.Should().Be(OrderStatus.Confirmed);
    }

    [Fact]
    public async Task GetProofForCustomerAsync_OtherCustomersOrder_Returns404_NotForbidden()
    {
        var order = new Order
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            ShippingEmail = "owner@example.com"
        };
        _orders.Setup(r => r.GetByIdWithItemsAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);

        ResponseData<PaymentProofFileDto> result = await CreateService()
            .GetProofForCustomerAsync(order.Id, "someone.else@example.com");

        // 404, never 403 — a 403 would confirm the order exists.
        result.StatusCode.Should().Be(404);
        _storage.Verify(s => s.OpenReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetProofForCustomerAsync_OwnOrder_StreamsTheFile()
    {
        var order = new Order
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            ShippingEmail = "owner@example.com"
        };
        _orders.Setup(r => r.GetByIdWithItemsAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);
        _proofs.Setup(r => r.GetByOrderIdAsync(order.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrderPaymentProof
            {
                OrderId = order.Id,
                TenantId = _tenantId,
                StorageKey = "key-1",
                ContentType = "application/pdf",
                OriginalFileName = "receipt.pdf"
            });
        _storage.Setup(s => s.OpenReadAsync("key-1", It.IsAny<CancellationToken>())).ReturnsAsync(Pdf());

        ResponseData<PaymentProofFileDto> result = await CreateService()
            .GetProofForCustomerAsync(order.Id, "OWNER@example.com");

        result.IsSuccess.Should().BeTrue();
        result.Data!.ContentType.Should().Be("application/pdf");
        result.Data.FileName.Should().Be("receipt.pdf");
    }

    [Fact]
    public async Task GetProofForCustomerAsync_OrderHasNoProof_Returns404()
    {
        var order = new Order { Id = Guid.NewGuid(), TenantId = _tenantId, ShippingEmail = "owner@example.com" };
        _orders.Setup(r => r.GetByIdWithItemsAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);
        _proofs.Setup(r => r.GetByOrderIdAsync(order.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrderPaymentProof?)null);

        ResponseData<PaymentProofFileDto> result = await CreateService()
            .GetProofForCustomerAsync(order.Id, "owner@example.com");

        result.StatusCode.Should().Be(404);
    }

    private OrderService CreateService()
    {
        // Only the collaborators these tests exercise are configured; the rest are loose mocks.
        return new OrderService(
            _orders.Object,
            Mock.Of<IProductRepository>(),
            Mock.Of<IProductVariantRepository>(),
            Mock.Of<IStockAdjustmentRepository>(),
            Mock.Of<ICustomerRepository>(),
            Mock.Of<IDiscountRepository>(),
            _proofs.Object,
            _storage.Object,
            _uow.Object,
            Mock.Of<IAuditLogService>(),
            Mock.Of<ICurrentTenantService>(),
            NullLogger<OrderService>.Instance);
    }
}
```

> **Implementer note:** `CreateService()` above lists constructor arguments in the order this task establishes. Read the real `OrderService` primary constructor first and match its exact parameter order — add `_proofs` and `_storage` in the positions you actually choose, and keep this test in sync.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/FashionSaaS.Application.Tests --filter OrderPaymentProofTests`
Expected: FAIL — build errors (`PaymentProofFileDto`, `GetProofForCustomerAsync`, new constructor parameters do not exist).

- [ ] **Step 3: Remove the payment DTO and its validation rules**

In `src/FashionSaaS.Application/Orders/DTOs/OrderDtos.cs` **via Serena**, delete the whole `CreateOrderPaymentDto` class (lines 29-33) and delete this line from `CreateOrderRequest`:

```csharp
    public CreateOrderPaymentDto PaymentInfo { get; set; } = new();
```

Create `src/FashionSaaS.Application/Orders/DTOs/PaymentProofFileDto.cs` **via Serena `create_text_file`**:

```csharp
namespace FashionSaaS.Application.Orders.DTOs;

/// <summary>
/// A payment proof opened for download. <see cref="Content"/> is an open stream the caller
/// must dispose (the controller hands it to <c>File(...)</c>, which disposes it).
/// </summary>
public class PaymentProofFileDto
{
    public required Stream Content { get; init; }
    public required string ContentType { get; init; }
    public required string FileName { get; init; }
}
```

In `src/FashionSaaS.Application/Orders/Validators/CreateOrderRequestValidator.cs` **via Serena**, delete the two `PaymentInfo` rules (lines 34-40), both private helper methods `NotBeAFullPan` and `BeMaskedOrLastFour`, both `[GeneratedRegex]` members with their `#pragma` block, and the now-unused `using System.Text.RegularExpressions;`. Change the class declaration from `public partial class` to `public class` (no source-generated regex remains).

- [ ] **Step 4: Update `OrderService`**

Add `IOrderPaymentProofRepository paymentProofRepository` and `IPaymentProofStorageService proofStorage` to the `OrderService` primary constructor parameter list.

Replace the `CreateAsync` signature so it accepts the proof, and inside it:

1. Delete these two lines (currently 127-128):

```csharp
        var cardNumber = request.PaymentInfo.CardNumber ?? string.Empty;
        var cardLast4 = cardNumber.Length >= 4 ? cardNumber[^4..] : cardNumber;
```

2. Delete `CardLast4 = cardLast4,` from the `new Order { ... }` initialiser (line 146).

3. Immediately after the existing guard clauses at the top of the method (before any stock work), add proof validation:

```csharp
        if (proofSizeBytes <= 0 || proofSizeBytes > PaymentProofContentTypes.MaxFileSizeBytes)
            return ResponseData<OrderDto>.Failure("Payment proof must be between 1 byte and 10 MB.", 400);

        if (!PaymentProofContentTypes.IsAllowed(proofContentType))
            return ResponseData<OrderDto>.Failure("Payment proof must be a JPEG, PNG, WebP or PDF file.", 400);

        // Never trust the declared content type: confirm the bytes match it, so a renamed
        // executable cannot reach storage.
        var header = new byte[12];
        var headerLength = await proofContent.ReadAsync(header, ct);
        if (!PaymentProofContentTypes.HeaderMatches(header.AsSpan(0, headerLength), proofContentType))
            return ResponseData<OrderDto>.Failure("Payment proof file contents do not match its type.", 400);

        proofContent.Position = 0;
```

4. Replace the persistence block (currently lines 187-188, `await orderRepository.AddAsync(order); await unitOfWork.SaveChangesAsync(ct);`) with a save-file-then-commit-with-cleanup sequence:

```csharp
        await orderRepository.AddAsync(order);

        // Write the binary first so a storage failure aborts before anything is committed.
        var storageKey = $"{tenantId}/{order.Id}/{Guid.NewGuid():N}{PaymentProofContentTypes.ExtensionFor(proofContentType)}";
        try
        {
            await proofStorage.SaveAsync(proofContent, storageKey, ct);
        }
        catch (IOException ex)
        {
            logger.LogError(ex, "Payment proof storage failed for tenant {TenantId}", tenantId);
            return ResponseData<OrderDto>.Failure("We couldn't save your payment proof. Please try again.", 502);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogError(ex, "Payment proof storage denied for tenant {TenantId}", tenantId);
            return ResponseData<OrderDto>.Failure("We couldn't save your payment proof. Please try again.", 502);
        }

        await paymentProofRepository.AddAsync(new OrderPaymentProof
        {
            TenantId = tenantId,
            OrderId = order.Id,
            StorageKey = storageKey,
            ContentType = proofContentType,
            OriginalFileName = proofFileName,
            SizeBytes = proofSizeBytes,
            UploadedAt = DateTime.UtcNow
        });

        try
        {
            // The order, its proof row and the stock decrements commit together — an order can
            // never be persisted without its proof.
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // The committed state is the source of truth; an orphaned file is harmless, an
            // order without a proof is not. Cleanup is best-effort and never throws.
            await proofStorage.DeleteAsync(storageKey, ct);
            throw;
        }
```

Add `using Microsoft.EntityFrameworkCore;` for `DbUpdateException` and `using FashionSaaS.Domain.Entities;` if not already present.

5. Add the confirm guard inside `TransitionAsync`, immediately after the existing `CanTransitionTo` check:

```csharp
        if (target == OrderStatus.Confirmed && order.PaymentProof is null)
        {
            return ResponseData<OrderDto>.Failure(
                "Payment proof is required before confirming this order.", 400);
        }
```

6. Add the two read methods (place them next to `GetByIdForCustomerAsync`):

```csharp
    /// <summary>Streams a proof for the owning tenant. Cross-tenant reads return 404, never 403.</summary>
    public async Task<ResponseData<PaymentProofFileDto>> GetProofForTenantAsync(Guid orderId,
        CancellationToken ct = default)
    {
        if (currentTenant.TenantId is not { } tenantId)
            return ResponseData<PaymentProofFileDto>.Failure("Tenant could not be resolved.", 400);

        Order? order = await orderRepository.GetByIdWithItemsAsync(orderId, ct);
        if (order is null || order.TenantId != tenantId)
            return ResponseData<PaymentProofFileDto>.Failure("Payment proof not found.", 404);

        return await OpenProofAsync(orderId, ct);
    }

    /// <summary>
    /// Streams a proof for the customer who placed the order. A non-owner gets the same 404 as a
    /// missing order — a 403 would confirm the order exists.
    /// </summary>
    public async Task<ResponseData<PaymentProofFileDto>> GetProofForCustomerAsync(Guid orderId,
        string customerEmail, CancellationToken ct = default)
    {
        Order? order = await orderRepository.GetByIdWithItemsAsync(orderId, ct);
        if (order is null || !string.Equals(order.ShippingEmail, customerEmail, StringComparison.OrdinalIgnoreCase))
            return ResponseData<PaymentProofFileDto>.Failure("Payment proof not found.", 404);

        return await OpenProofAsync(orderId, ct);
    }

    private async Task<ResponseData<PaymentProofFileDto>> OpenProofAsync(Guid orderId, CancellationToken ct)
    {
        OrderPaymentProof? proof = await paymentProofRepository.GetByOrderIdAsync(orderId, ct);
        if (proof is null)
            return ResponseData<PaymentProofFileDto>.Failure("Payment proof not found.", 404);

        try
        {
            Stream content = await proofStorage.OpenReadAsync(proof.StorageKey, ct);
            return ResponseData<PaymentProofFileDto>.Success(new PaymentProofFileDto
            {
                Content = content,
                ContentType = proof.ContentType,
                FileName = proof.OriginalFileName
            });
        }
        catch (FileNotFoundException ex)
        {
            // Row exists but the binary is gone — a storage inconsistency, not a client error.
            logger.LogError(ex, "Payment proof binary missing for order {OrderId}", orderId);
            return ResponseData<PaymentProofFileDto>.Failure("Payment proof is unavailable.", 502);
        }
    }
```

- [ ] **Step 4b: Generate and apply the deferred EF migration**

Task 3 could not generate the migration because `dotnet ef migrations add` needs the whole solution to build, and it didn't (deliberately, at that point). It builds again now that the `CardLast4`/`PaymentInfo` usages above are gone. Generate it here:

```bash
dotnet ef migrations add AddOrderPaymentProof --project src/FashionSaaS.Infrastructure --startup-project src/FashionSaaS.API
```

Expected: a new file under `src/FashionSaaS.Infrastructure/Persistence/Migrations/`. Open it and confirm it contains all three changes: `CreateTable("OrderPaymentProofs", ...)`, `DropColumn(name: "CardLast4", table: "Orders")`, and `AddColumn<string>(name: "PaymentInstructions", table: "Tenants", maxLength: 2000, nullable: true)`. If any is missing, Task 3's entity/configuration edits didn't take effect as expected — fix them and regenerate before proceeding.

Apply it:

```bash
dotnet ef database update --project src/FashionSaaS.Infrastructure --startup-project src/FashionSaaS.API
```

Expected: `Done.`

Verify the schema:

```bash
sqlcmd -S localhost -U sa -P 12345678 -C -d AiClothing -Q "SET NOCOUNT ON; SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='OrderPaymentProofs' ORDER BY COLUMN_NAME" -W
```

Expected columns: `ContentType, CreatedAt, Id, OrderId, OriginalFileName, SizeBytes, StorageKey, TenantId, UpdatedAt, UploadedAt`.

- [ ] **Step 5: Update the store controller to accept multipart**

In `src/FashionSaaS.API/Controllers/Store/StoreOrdersController.cs` **via Serena**, replace the `Create` action with:

```csharp
    /// <summary>Maximum accepted payment-proof size (10 MB).</summary>
    private const long MaxProofBytes = 10485760;

    [HttpPost(ApiUrl.StoreOrders.Create)]
    [RequestSizeLimit(MaxProofBytes)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Create([FromForm] CreateOrderRequest request, IFormFile? paymentProof,
        CancellationToken ct)
    {
        if (paymentProof is null || paymentProof.Length == 0)
            return StatusCode(400, ResponseData<string>.Failure("A payment proof file is required.", 400));

        if (paymentProof.Length > MaxProofBytes)
            return StatusCode(400, ResponseData<string>.Failure("Payment proof must be 10 MB or smaller.", 400));

        var firstName = request.ShippingAddress.FirstName;
        var lastName = request.ShippingAddress.LastName;

        // Buffer to memory so the service can read the magic-number header and then re-read from
        // the start; the 10 MB cap above bounds this. IFormFile streams are not reliably seekable.
        using var buffered = new MemoryStream();
        await using (Stream upload = paymentProof.OpenReadStream())
        {
            await upload.CopyToAsync(buffered, ct);
        }

        buffered.Position = 0;

        ResponseData<OrderDto> response = await orderService.CreateAsync(Email, firstName, lastName,
            request.ShippingAddress.Phone, request, UserId, Ip, Ua,
            buffered, paymentProof.FileName, paymentProof.ContentType, paymentProof.Length, ct);

        return StatusCode(response.StatusCode, response);
    }
```

- [ ] **Step 6: Fix the existing tests that reference the removed fields**

- `tests/FashionSaaS.Application.Tests/Orders/CreateOrderRequestValidatorTests.cs` — delete the `PaymentInfo = new CreateOrderPaymentDto { ... }` initialiser (line 26 area) and delete the four card tests that set `request.PaymentInfo.*` (lines 38, 49, 60, 127 areas). Every remaining test must still compile and pass.
- `tests/FashionSaaS.Application.Tests/Orders/OrderServiceTests.cs` — delete the `PaymentInfo = ValidPayment(),` lines (69, 111) and the now-unused `ValidPayment()` helper; update every `CreateAsync(...)` call to pass the four new proof arguments, e.g. `..., new MemoryStream(Encoding.ASCII.GetBytes("%PDF-1.7")), "receipt.pdf", "application/pdf", 13L`. Update the constructor call to include the two new mocks.
- `tests/FashionSaaS.Application.Tests/Orders/OrderWorkflowE2ETests.cs` — same two changes (line 150 area).

- [ ] **Step 7: Run the tests**

Run: `dotnet test tests/FashionSaaS.Application.Tests --filter "OrderPaymentProofTests|OrderServiceTests|CreateOrderRequestValidatorTests|OrderWorkflowE2ETests"`
Expected: PASS — 0 failed.

- [ ] **Step 8: Run the full verification gate**

Run: `dotnet build FashionSaaS.sln` — Expected: `0 Warning(s) 0 Error(s)`.
Run: `dotnet test FashionSaaS.sln` — Expected: 0 failed.
Then run `mcp__serena__get_diagnostics_for_file` (`min_severity: 2`) on every `.cs` file touched in this task. Expected: no diagnostics.

- [ ] **Step 9: Commit**

```bash
git add src/FashionSaaS.Application/Orders/ src/FashionSaaS.API/Controllers/Store/StoreOrdersController.cs src/FashionSaaS.Infrastructure/Persistence/Migrations/ tests/FashionSaaS.Application.Tests/Orders/
git commit -m "feat(orders): require payment proof at checkout and guard order confirmation"
```

Note: this commit includes the EF migration generated in Step 4b — Task 3's commit deliberately excluded it since the solution didn't build yet at that point.

---

## Task 5: Proof download endpoints

**Files:**
- Modify: `src/FashionSaaS.API/Constants/ApiUrl.cs` (`StoreOrders`, `TenantOrders`)
- Modify: `src/FashionSaaS.API/Controllers/Store/StoreOrdersController.cs`
- Modify: `src/FashionSaaS.API/Controllers/Tenant/OrdersController.cs`

**Interfaces:**
- Consumes: `OrderService.GetProofForTenantAsync` / `GetProofForCustomerAsync`, `PaymentProofFileDto` (Task 4).
- Produces: `GET api/store/orders/{id}/payment-proof`, `GET api/tenant/orders/{id}/payment-proof`.

- [ ] **Step 1: Add the route constants**

In `src/FashionSaaS.API/Constants/ApiUrl.cs` **via Serena**, add to `StoreOrders`:

```csharp
        public const string GetPaymentProof = "api/store/orders/{id}/payment-proof";
```

and to `TenantOrders`:

```csharp
        public const string GetPaymentProof = "api/tenant/orders/{id}/payment-proof";
```

- [ ] **Step 2: Add the customer endpoint**

Append to `StoreOrdersController` **via Serena `insert_after_symbol`** (after the `Cancel` action):

```csharp
    /// <summary>
    /// Streams the caller's own payment proof. A non-owner receives 404 rather than 403 so the
    /// existence of another customer's order is never disclosed.
    /// </summary>
    [HttpGet(ApiUrl.StoreOrders.GetPaymentProof)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetPaymentProof(Guid id, CancellationToken ct)
    {
        ResponseData<PaymentProofFileDto> response = await orderService.GetProofForCustomerAsync(id, Email, ct);
        if (!response.IsSuccess || response.Data is null)
            return StatusCode(response.StatusCode, ResponseData<string>.Failure(response.Message, response.StatusCode));

        return File(response.Data.Content, response.Data.ContentType, response.Data.FileName);
    }
```

- [ ] **Step 3: Add the tenant endpoint**

Append to `src/FashionSaaS.API/Controllers/Tenant/OrdersController.cs` **via Serena `insert_after_symbol`** (after the `Cancel` action):

```csharp
    /// <summary>
    /// Streams the payment proof for one of this tenant's orders, for manual review before
    /// confirming. Another tenant's order returns 404.
    /// </summary>
    [HttpGet(ApiUrl.TenantOrders.GetPaymentProof)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetPaymentProof(Guid id, CancellationToken ct)
    {
        ResponseData<PaymentProofFileDto> response = await orderService.GetProofForTenantAsync(id, ct);
        if (!response.IsSuccess || response.Data is null)
            return StatusCode(response.StatusCode, ResponseData<string>.Failure(response.Message, response.StatusCode));

        return File(response.Data.Content, response.Data.ContentType, response.Data.FileName);
    }
```

Add `using FashionSaaS.Application.Orders.DTOs;` to both controllers if not already imported.

- [ ] **Step 4: Verify manually end-to-end**

Start the API (`dotnet run --project src/FashionSaaS.API`), then:

```bash
curl -s -o /dev/null -w "%{http_code} %{content_type}\n" -H "Authorization: Bearer $TOKEN" http://localhost:5129/api/tenant/orders/$ORDER_ID/payment-proof
```

Expected: `200 application/pdf` (or the uploaded type) for an order with a proof; `404 application/json` for another tenant's order.

- [ ] **Step 5: Run the verification gate**

Run: `dotnet build FashionSaaS.sln` — Expected: `0 Warning(s) 0 Error(s)`.
Run: `dotnet test FashionSaaS.sln` — Expected: 0 failed.
Then `mcp__serena__get_diagnostics_for_file` (`min_severity: 2`) on the three touched `.cs` files. Expected: no diagnostics.

- [ ] **Step 6: Commit**

```bash
git add src/FashionSaaS.API/Constants/ApiUrl.cs src/FashionSaaS.API/Controllers/Store/StoreOrdersController.cs src/FashionSaaS.API/Controllers/Tenant/OrdersController.cs
git commit -m "feat(orders): add tenant and customer payment-proof download endpoints"
```

---

## Task 6: Tenant payment instructions

**Files:**
- Modify: `src/FashionSaaS.Application/Tenants/DTOs/UpdateTenantRequest.cs`
- Modify: `src/FashionSaaS.Application/Tenants/DTOs/TenantResponse.cs`
- Modify: `src/FashionSaaS.Application/Tenants/TenantService.cs:55-75`
- Create: `src/FashionSaaS.Application/Tenants/Validators/UpdateTenantRequestValidator.cs`
- Create: `src/FashionSaaS.API/Controllers/Public/PublicPaymentInstructionsController.cs`
- Modify: `src/FashionSaaS.API/Constants/ApiUrl.cs` (`PublicCatalog`)
- Test: `tests/FashionSaaS.Application.Tests/Tenants/TenantServiceTests.cs`

**Interfaces:**
- Consumes: `Tenant.PaymentInstructions` (Task 3).
- Produces: `GET api/{slug}/payment-instructions` returning `ResponseData<string>` whose `Data` is the instructions (empty string when unset).

- [ ] **Step 1: Write the failing tests**

Append to `tests/FashionSaaS.Application.Tests/Tenants/TenantServiceTests.cs` (match the file's existing mock fields and `CreateService()` helper — read it first):

```csharp
    [Fact]
    public async Task UpdateAsync_PersistsPaymentInstructions()
    {
        var tenant = new Tenant { Id = Guid.NewGuid(), Name = "Chic", Slug = "chic", Email = "t@example.com" };
        _tenants.Setup(r => r.GetByIdAsync(tenant.Id)).ReturnsAsync(tenant);

        ResponseData<TenantResponse> result = await CreateService().UpdateAsync(tenant.Id,
            new UpdateTenantRequest { Name = "Chic", PaymentInstructions = "Transfer to HBL 1234-5678" },
            Guid.NewGuid(), "127.0.0.1", "ua");

        result.IsSuccess.Should().BeTrue();
        tenant.PaymentInstructions.Should().Be("Transfer to HBL 1234-5678");
        result.Data!.PaymentInstructions.Should().Be("Transfer to HBL 1234-5678");
    }

    [Fact]
    public void UpdateTenantRequestValidator_InstructionsOverMaxLength_Fails()
    {
        var validator = new UpdateTenantRequestValidator();

        FluentValidation.Results.ValidationResult result = validator.Validate(
            new UpdateTenantRequest { Name = "Chic", PaymentInstructions = new string('x', 2001) });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(UpdateTenantRequest.PaymentInstructions));
    }

    [Fact]
    public void UpdateTenantRequestValidator_InstructionsAtMaxLength_Passes()
    {
        var validator = new UpdateTenantRequestValidator();

        FluentValidation.Results.ValidationResult result = validator.Validate(
            new UpdateTenantRequest { Name = "Chic", PaymentInstructions = new string('x', 2000) });

        result.IsValid.Should().BeTrue();
    }
```

Add `using FashionSaaS.Application.Tenants.Validators;` to the file.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/FashionSaaS.Application.Tests --filter TenantServiceTests`
Expected: FAIL — `PaymentInstructions` and `UpdateTenantRequestValidator` do not exist.

- [ ] **Step 3: Add the field to both DTOs**

In `src/FashionSaaS.Application/Tenants/DTOs/UpdateTenantRequest.cs` **via Serena**, add:

```csharp
    public string? PaymentInstructions { get; set; }
```

In `src/FashionSaaS.Application/Tenants/DTOs/TenantResponse.cs` **via Serena**, add:

```csharp
    public string? PaymentInstructions { get; set; }
```

- [ ] **Step 4: Persist and return it**

In `src/FashionSaaS.Application/Tenants/TenantService.cs` **via Serena**, inside `UpdateAsync` add after `tenant.CoverImageUrl = request.CoverImageUrl;`:

```csharp
        tenant.PaymentInstructions = request.PaymentInstructions;
```

Then locate the private `MapToResponse` helper in the same file and add `PaymentInstructions = tenant.PaymentInstructions,` to the object initialiser.

- [ ] **Step 5: Add the validator**

Create `src/FashionSaaS.Application/Tenants/Validators/UpdateTenantRequestValidator.cs` **via Serena `create_text_file`**. It is auto-registered by `AddValidatorsFromAssembly` (`Program.cs:67`) — no DI edit needed.

```csharp
using FashionSaaS.Application.Tenants.DTOs;
using FluentValidation;

namespace FashionSaaS.Application.Tenants.Validators;

public class UpdateTenantRequestValidator : AbstractValidator<UpdateTenantRequest>
{
    public UpdateTenantRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required.")
            .MaximumLength(200).WithMessage("Name must not exceed 200 characters.");

        RuleFor(x => x.PaymentInstructions)
            .MaximumLength(2000).WithMessage("PaymentInstructions must not exceed 2000 characters.")
            .When(x => x.PaymentInstructions is not null);
    }
}
```

- [ ] **Step 6: Add the public endpoint**

In `src/FashionSaaS.API/Constants/ApiUrl.cs` **via Serena**, add to `PublicCatalog`:

```csharp
        public const string GetPaymentInstructions = "api/{slug}/payment-instructions";
```

Create `src/FashionSaaS.API/Controllers/Public/PublicPaymentInstructionsController.cs` **via Serena `create_text_file`**:

```csharp
using FashionSaaS.API.Constants;
using FashionSaaS.Application.Common;
using FashionSaaS.Application.Interfaces;
using FashionSaaS.Application.Tenants;
using FashionSaaS.Application.Tenants.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FashionSaaS.API.Controllers.Public;

/// <summary>
/// Public, unauthenticated payment instructions for a storefront. Tenant scoping comes from the
/// {slug} route segment resolved by TenantResolutionMiddleware.
/// <para>
/// Deliberately returns only the tenant-authored free-text instructions. The tenant's
/// BankAccount record is AES-256-GCM encrypted and gated behind AdminOwner/SuperAdmin, and is
/// never exposed here — the tenant decides exactly what payment detail customers see.
/// </para>
/// </summary>
[ApiController]
[AllowAnonymous]
[EnableRateLimiting("PublicPolicy")]
public class PublicPaymentInstructionsController(
    TenantService tenantService,
    ICurrentTenantService currentTenant) : ControllerBase
{
    [HttpGet(ApiUrl.PublicCatalog.GetPaymentInstructions)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get()
    {
        ResponseData<TenantResponse> response = await tenantService.GetByIdAsync(currentTenant.TenantId!.Value);
        if (!response.IsSuccess || response.Data is null)
            return StatusCode(404, ResponseData<string>.Failure("Store not found.", 404));

        // Unset instructions are a normal state, not an error — the storefront shows a fallback.
        return StatusCode(200, ResponseData<string>.Success(response.Data.PaymentInstructions ?? string.Empty));
    }
}
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test tests/FashionSaaS.Application.Tests --filter TenantServiceTests`
Expected: PASS — 0 failed.

- [ ] **Step 8: Verify the public endpoint**

```bash
curl -s http://localhost:5129/api/chic-boutique/payment-instructions
```

Expected: `{"isSuccess":true,"statusCode":200,...,"data":""}` before any instructions are set, and the saved text after a `PUT /api/tenant/profile`. Confirm the response body contains **no** bank fields.

- [ ] **Step 9: Run the verification gate**

Run: `dotnet build FashionSaaS.sln` — Expected: `0 Warning(s) 0 Error(s)`.
Run: `dotnet test FashionSaaS.sln` — Expected: 0 failed.
Then `mcp__serena__get_diagnostics_for_file` (`min_severity: 2`) on every touched `.cs` file. Expected: no diagnostics.

- [ ] **Step 10: Commit**

```bash
git add src/FashionSaaS.Application/Tenants/ src/FashionSaaS.API/Controllers/Public/PublicPaymentInstructionsController.cs src/FashionSaaS.API/Constants/ApiUrl.cs tests/FashionSaaS.Application.Tests/Tenants/TenantServiceTests.cs
git commit -m "feat(tenants): add tenant-authored payment instructions with public endpoint"
```

---

## Task 7: Storefront — proof upload replaces the card form

**Files:**
- Modify: `fashionsaas-storefront/src/app/features/checkout/models/checkout.model.ts`
- Modify: `fashionsaas-storefront/src/app/features/checkout/services/checkout.service.ts`
- Modify: `fashionsaas-storefront/src/app/features/checkout/services/order.service.ts`
- Modify: `fashionsaas-storefront/src/app/features/checkout/components/payment-form/payment-form.component.{ts,html}`
- Modify: `fashionsaas-storefront/src/app/features/checkout/components/checkout/checkout.component.ts`
- Modify: `fashionsaas-storefront/src/app/features/checkout/components/checkout-review/checkout-review.component.html`
- Test: the co-located `*.spec.ts` files

**Interfaces:**
- Consumes: `POST /api/store/orders` as multipart (Task 4); `GET /api/{slug}/payment-instructions` (Task 6).
- Produces: `PaymentProof { file: File; fileName: string }` replacing `PaymentInfo` in `CheckoutForm`.

> Use native Edit/Write here — the Serena hook only guards `.cs`.

- [ ] **Step 1: Replace the payment model**

In `checkout.model.ts`, delete the `PaymentInfo` interface and replace with:

```typescript
export interface PaymentProof {
  /** The uploaded proof-of-payment file (image or PDF). Not persisted between sessions. */
  file: File | null;
  fileName: string;
}
```

and change `CheckoutForm.paymentInfo: PaymentInfo` to `paymentProof: PaymentProof`.

In `checkout.service.ts`, replace the `paymentInfo: { ... }` block inside `getEmptyForm()` with:

```typescript
      paymentProof: {
        file: null,
        fileName: ''
      },
```

- [ ] **Step 2: Send multipart from the order service**

In `order.service.ts`, replace `createOrder` with (mirrors the existing `FormData` pattern in `features/catalog/services/try-on.service.ts`, and uses the dotted/indexed field names ASP.NET Core model binding expects):

```typescript
  createOrder(checkout: CheckoutForm, cartItems: CartItem[]): Observable<Order> {
    const formData = new FormData();

    const address = checkout.shippingAddress;
    formData.append('ShippingAddress.FirstName', address.firstName);
    formData.append('ShippingAddress.LastName', address.lastName);
    formData.append('ShippingAddress.Email', address.email);
    formData.append('ShippingAddress.Phone', address.phone);
    formData.append('ShippingAddress.Street', address.street);
    formData.append('ShippingAddress.City', address.city);
    formData.append('ShippingAddress.State', address.state);
    formData.append('ShippingAddress.ZipCode', address.zipCode);
    formData.append('ShippingAddress.Country', address.country);

    cartItems.forEach((item, index) => {
      formData.append(`Items[${index}].ProductId`, item.productId);
      formData.append(`Items[${index}].Quantity`, item.quantity.toString());
      if (item.selectedVariant?.size) {
        formData.append(`Items[${index}].Variant.Size`, item.selectedVariant.size);
      }
      if (item.selectedVariant?.color) {
        formData.append(`Items[${index}].Variant.Color`, item.selectedVariant.color);
      }
    });

    if (checkout.paymentProof.file) {
      formData.append('paymentProof', checkout.paymentProof.file);
    }

    return this.apiService.post<Order>(this.apiUrl, formData)
      .pipe(
        map((response: ApiResponse<Order>) => response.data)
      );
  }
```

> `ApiService.post` forwards the body to `HttpClient`, which sets the multipart boundary automatically for a `FormData` body. Do **not** set a `Content-Type` header manually.

- [ ] **Step 3: Turn the payment form into an upload form**

Replace `payment-form.component.ts` with:

```typescript
import { Component, OnInit, Output, EventEmitter, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { PaymentProof } from '../../models/checkout.model';
import { ApiService } from '../../../../core/services/api.service';
import { ApiResponse } from '../../../../core/models/api-response.model';
import { environment } from '@env/environment';

const ALLOWED_TYPES = ['image/jpeg', 'image/png', 'image/webp', 'application/pdf'];
const MAX_BYTES = 10485760;

@Component({
  selector: 'app-payment-form',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './payment-form.component.html',
  styleUrls: ['./payment-form.component.scss']
})
export class PaymentFormComponent implements OnInit {
  @Output() submitted = new EventEmitter<PaymentProof>();

  private readonly api = inject(ApiService);

  selectedFile: File | null = null;
  errorMessage = '';
  paymentInstructions = '';

  ngOnInit() {
    this.api
      .get<string>(`${environment.tenantSlug}/payment-instructions`)
      .subscribe({
        next: (response: ApiResponse<string>) => (this.paymentInstructions = response.data ?? ''),
        // Instructions are informational; a failure must not block the upload.
        error: () => (this.paymentInstructions = '')
      });
  }

  onFileSelected(event: Event): void {
    this.errorMessage = '';
    this.selectedFile = null;

    const files = (event.target as HTMLInputElement).files;
    if (!files || files.length === 0) return;

    const file = files[0];

    if (!ALLOWED_TYPES.includes(file.type)) {
      this.errorMessage = 'Please upload a JPEG, PNG, WebP or PDF file.';
      return;
    }

    if (file.size > MAX_BYTES) {
      this.errorMessage = 'File must be 10 MB or smaller.';
      return;
    }

    this.selectedFile = file;
  }

  onSubmit(): void {
    if (!this.selectedFile) {
      this.errorMessage = 'Payment proof is required.';
      return;
    }

    this.submitted.emit({ file: this.selectedFile, fileName: this.selectedFile.name });
  }
}
```

> `environment.tenantSlug` is confirmed to exist in `fashionsaas-storefront/src/environments/environment.ts` (value `'chic-boutique'` in dev). Note that `environment.prod.ts` intentionally leaves it empty pending subdomain-based tenant resolution — so in production the instructions call will 404 and the component falls back to the "contact the store" message, which is the correct degradation and needs no extra handling here.

Replace `payment-form.component.html` with:

```html
<div class="payment-form">
  <h3>Payment</h3>

  <div class="alert alert-info" *ngIf="paymentInstructions">
    <strong>How to pay</strong>
    <p class="mb-0" style="white-space: pre-line">{{ paymentInstructions }}</p>
  </div>
  <div class="alert alert-secondary" *ngIf="!paymentInstructions">
    Please contact the store for payment details.
  </div>

  <p>
    Pay using the details above, then upload a screenshot or PDF receipt as proof.
    Your order is placed on hold until the store verifies your payment.
  </p>

  <label for="payment-proof" class="form-label">Payment proof</label>
  <input
    id="payment-proof"
    type="file"
    class="form-control"
    accept="image/jpeg,image/png,image/webp,application/pdf"
    (change)="onFileSelected($event)" />

  <div class="text-danger mt-2" *ngIf="errorMessage">{{ errorMessage }}</div>
  <div class="text-success mt-2" *ngIf="selectedFile">Attached: {{ selectedFile.name }}</div>

  <button
    type="button"
    class="btn btn-primary mt-3"
    [disabled]="!selectedFile"
    (click)="onSubmit()">
    Review Order
  </button>
</div>
```

- [ ] **Step 4: Update the checkout container**

In `checkout.component.ts`, change the import `PaymentInfo` to `PaymentProof` and replace `onPaymentSubmit`:

```typescript
  onPaymentSubmit(paymentProof: PaymentProof) {
    const currentForm = this.checkoutService.getCheckoutForm();
    this.checkoutService.setCheckoutForm({
      ...currentForm,
      paymentProof
    });
    this.currentStep = 'review';
  }
```

- [ ] **Step 5: Update the review step**

In `checkout-review.component.html`, replace the "Card ending in …" block with the attached filename. Read the file first, then replace that markup with:

```html
        <h4>Payment Proof</h4>
        <div>{{ (checkoutForm.paymentProof.fileName) || 'No file attached' }}</div>
        <div class="text-muted small">Your order will be on hold until the store verifies this payment.</div>
```

If `checkout-review.component.ts` declares a `PaymentInfo`-typed input or field, change it to `PaymentProof`.

- [ ] **Step 6: Update the specs**

Update every `*.spec.ts` in `features/checkout/` that references `paymentInfo`, `cardNumber`, `cardholderName`, `expiryMonth`, `expiryYear` or `cvv`. In `order.service.spec.ts`, replace the JSON-body assertion with:

```typescript
    expect(req.request.body instanceof FormData).toBe(true);
    expect((req.request.body as FormData).get('ShippingAddress.FirstName')).toBe('Test');
    expect((req.request.body as FormData).has('paymentProof')).toBe(true);
```

Add a `payment-form.component.spec.ts` case for each rejection path:

```typescript
  it('rejects a disallowed content type', () => {
    const file = new File(['x'], 'evil.exe', { type: 'application/x-msdownload' });
    component.onFileSelected({ target: { files: [file] } } as unknown as Event);
    expect(component.selectedFile).toBeNull();
    expect(component.errorMessage).toContain('JPEG');
  });

  it('rejects a file over 10 MB', () => {
    const big = new File([new ArrayBuffer(10485761)], 'big.pdf', { type: 'application/pdf' });
    component.onFileSelected({ target: { files: [big] } } as unknown as Event);
    expect(component.selectedFile).toBeNull();
    expect(component.errorMessage).toContain('10 MB');
  });

  it('accepts a valid PDF', () => {
    const file = new File(['%PDF-1.7'], 'receipt.pdf', { type: 'application/pdf' });
    component.onFileSelected({ target: { files: [file] } } as unknown as Event);
    expect(component.selectedFile).toBe(file);
    expect(component.errorMessage).toBe('');
  });
```

- [ ] **Step 7: Run the frontend build and tests**

```bash
cd fashionsaas-storefront && npx ng build
```

Expected: `Application bundle generation complete.` with no errors.

```bash
cd fashionsaas-storefront && npx ng test --watch=false --browsers=ChromeHeadless
```

Expected: 0 failures.

- [ ] **Step 8: Verify in the browser**

Start the API and `ng serve`, then place a real order through the UI with a PDF receipt. Confirm: the payment step shows the instructions, "Review Order" stays disabled until a file is attached, the order is created (`201`), and the confirmation shows status **Pending**. Check the browser console for zero errors and confirm the `POST /api/store/orders` request is `multipart/form-data`.

- [ ] **Step 9: Commit**

```bash
git add fashionsaas-storefront/src/app/features/checkout/
git commit -m "feat(storefront): replace card form with payment-proof upload at checkout"
```

---

## Task 8: Storefront — viewing the proof

**Files:**
- Modify: `fashionsaas-storefront/src/app/admin/shared/services/order-admin.service.ts`
- Modify: `fashionsaas-storefront/src/app/admin/orders/order-detail/order-detail.component.{ts,html}`
- Modify: `fashionsaas-storefront/src/app/features/checkout/components/order-confirmation/order-confirmation.component.html`
- Test: the co-located `*.spec.ts` files

**Interfaces:**
- Consumes: `GET /api/tenant/orders/{id}/payment-proof` (Task 5).
- Produces: `OrderAdminService.getPaymentProof(orderId: string): Observable<Blob>`.

- [ ] **Step 1: Add the proof fetch to the admin service**

In `order-admin.service.ts` (its `base` is already `'tenant/orders'`), add:

```typescript
  /**
   * Fetches the payment proof as a Blob. The backend streams the file, so this bypasses
   * ApiService's JSON envelope and calls HttpClient directly with responseType: 'blob'.
   */
  getPaymentProof(orderId: string): Observable<Blob> {
    return this.http.get(`${environment.apiBaseUrl}/${this.base}/${orderId}/payment-proof`, {
      responseType: 'blob'
    });
  }
```

The service currently declares only `constructor(private apiService: ApiService)` and imports `HttpParams` (not `HttpClient`) from `@angular/common/http`. So you must also:

- change the import to `import { HttpClient, HttpParams } from '@angular/common/http';`
- add `import { environment } from '@env/environment';`
- change the constructor to `constructor(private apiService: ApiService, private http: HttpClient) {}`

- [ ] **Step 2: Render the proof in the admin order detail**

In `order-detail.component.ts`, add:

```typescript
  proofUrl: string | null = null;
  proofIsPdf = false;
  proofError = '';

  loadPaymentProof(orderId: string): void {
    this.proofError = '';
    this.orderAdmin.getPaymentProof(orderId).subscribe({
      next: (blob) => {
        this.proofIsPdf = blob.type === 'application/pdf';
        // Object URL is revoked in ngOnDestroy to avoid leaking the blob.
        this.proofUrl = URL.createObjectURL(blob);
      },
      error: () => (this.proofError = 'No payment proof available for this order.')
    });
  }

  ngOnDestroy(): void {
    if (this.proofUrl) {
      URL.revokeObjectURL(this.proofUrl);
    }
  }
```

Implement `OnDestroy` on the class, call `loadPaymentProof(orderId)` where the order is loaded, and inject the admin order service as `orderAdmin` if it is not already.

In `order-detail.component.html`, add above the Confirm/Cancel buttons:

```html
<div class="card mb-3">
  <div class="card-header">Payment Proof</div>
  <div class="card-body">
    <div class="text-muted" *ngIf="proofError">{{ proofError }}</div>
    <img *ngIf="proofUrl && !proofIsPdf" [src]="proofUrl" alt="Payment proof" class="img-fluid" />
    <a *ngIf="proofUrl && proofIsPdf" [href]="proofUrl" target="_blank" rel="noopener">Open PDF receipt</a>
    <p class="text-muted small mt-2 mb-0">
      Verify this payment before confirming the order. Confirm approves it; Cancel rejects it with a reason.
    </p>
  </div>
</div>
```

- [ ] **Step 3: Tell the customer the order is on hold**

In `order-confirmation.component.html`, change the "What's Next?" list so the first item reads:

```html
  <li>Your order is on hold until the store verifies your payment</li>
```

- [ ] **Step 3b: Let the customer view their own proof**

Without this, the customer-facing endpoint from Task 5 has no consumer.

In `fashionsaas-storefront/src/app/features/account/services/account.service.ts` (which already calls `store/orders` and `store/orders/{id}`), add — injecting `HttpClient` and importing `environment` the same way as in Step 1 if they are not already present:

```typescript
  /** Fetches the customer's own payment proof as a Blob (the backend streams the file). */
  getOrderPaymentProof(orderId: string): Observable<Blob> {
    return this.http.get(`${environment.apiBaseUrl}/store/orders/${orderId}/payment-proof`, {
      responseType: 'blob'
    });
  }
```

In `features/account/components/order-history/order-history.component.ts`, add:

```typescript
  proofUrls = new Map<string, string>();

  viewProof(orderId: string): void {
    this.account.getOrderPaymentProof(orderId).subscribe({
      next: (blob) => {
        const url = URL.createObjectURL(blob);
        this.proofUrls.set(orderId, url);
        window.open(url, '_blank');
      },
      error: () => this.toast?.error('Payment proof is unavailable.')
    });
  }

  ngOnDestroy(): void {
    this.proofUrls.forEach((url) => URL.revokeObjectURL(url));
  }
```

Implement `OnDestroy` on the class. If the component has no `toast` service, replace that error line with a component field (e.g. `this.proofError = 'Payment proof is unavailable.'`) rendered in the template.

In `order-history.component.html`, add a per-order action:

```html
<button type="button" class="btn btn-link btn-sm" (click)="viewProof(order.orderId)">
  View payment proof
</button>
```

- [ ] **Step 4: Add specs**

In `order-admin.service.spec.ts`:

```typescript
  it('requests the payment proof as a blob', () => {
    service.getPaymentProof('order-1').subscribe();
    const req = httpMock.expectOne(`${base}/order-1/payment-proof`);
    expect(req.request.method).toBe('GET');
    expect(req.request.responseType).toBe('blob');
  });
```

In `order-detail.component.spec.ts`, add a case asserting `proofError` is set when the request fails, and that `proofIsPdf` is `true` for an `application/pdf` blob.

- [ ] **Step 5: Run the frontend build and tests**

```bash
cd fashionsaas-storefront && npx ng build
```

Expected: `Application bundle generation complete.` with no errors.

```bash
cd fashionsaas-storefront && npx ng test --watch=false --browsers=ChromeHeadless
```

Expected: 0 failures.

- [ ] **Step 6: Verify the whole flow in the browser**

1. As a customer, place an order with a PDF proof → order shows **Pending**.
2. As the tenant, open the order in admin → the proof renders/links.
3. Click **Confirm** → status becomes **Confirmed**; then Ship → Deliver.
4. Place a second order, then **Cancel** it with a reason → status becomes **Cancelled** and the reason is recorded.
5. Confirm an order that has no proof (insert one directly in SQL) → the API returns `400`.

- [ ] **Step 7: Full-suite regression**

```bash
dotnet build FashionSaaS.sln && dotnet test FashionSaaS.sln
```

Expected: `0 Warning(s) 0 Error(s)`; 0 tests failed. Record the exact test count.

- [ ] **Step 8: Commit**

```bash
git add fashionsaas-storefront/src/app/admin/ fashionsaas-storefront/src/app/features/checkout/components/order-confirmation/
git commit -m "feat(storefront): render payment proof for tenant review and flag on-hold orders"
```

---

## Validate

- [ ] `dotnet build FashionSaaS.sln` → `0 Warning(s) 0 Error(s)`
- [ ] `dotnet test FashionSaaS.sln` → 0 failed (record the count)
- [ ] `mcp__serena__get_diagnostics_for_file` (`min_severity: 2`) clean on every touched `.cs` file
- [ ] `cd fashionsaas-storefront && npx ng build` → bundle generated, no errors
- [ ] `cd fashionsaas-storefront && npx ng test --watch=false --browsers=ChromeHeadless` → 0 failures
- [ ] Manual: proof-required checkout, tenant approve path, tenant reject path, confirm-without-proof `400`, cross-customer proof read `404`
- [ ] Manual: the customer can view their own proof from order history, and both proof endpoints (tenant + store) have a working frontend consumer
- [ ] `grep -rn "CardLast4\|PaymentInfo\|CreateOrderPaymentDto" --include=*.cs src/ tests/` returns **only** historical migration files under `Persistence/Migrations/`

## Deliberate deviation from the spec

**Where the file checks live.** Spec §4.2 says a new FluentValidation validator enforces "proof present, content-type in allowlist, size within max". This plan instead puts those three checks in the **controller** (Task 4, Step 5) and repeats them in the **service** (Task 4, Step 4).

Reason: the codebase already settled this question the other way for the identical case. `UploadImageRequestValidator` carries an explicit comment that "the actual file content-type and size limits are enforced at the API boundary", and `ProductImagesController.Upload` does exactly that. FluentValidation also cannot see the uploaded bytes, so the magic-number check has to live outside a validator regardless — splitting the three size/type checks away from it would scatter one concern across two layers.

The spec's actual requirement — defense in depth, checked in more than one place — is preserved: controller **and** service both validate. If the reviewer prefers the literal spec wording, this is a plan-level decision to escalate, not something to silently "fix" during implementation.

## Notes for the reviewer

- **Migrations retain `CardLast4`** in their `.Designer.cs` snapshots by design — those are historical records of past schema. Only `ApplicationDbContextModelSnapshot.cs` should lose it, via the new migration.
- **The 10 MB in-memory buffer** in `StoreOrdersController.Create` is deliberate: the magic-number check needs to read the header and then re-read from position 0, and `IFormFile` streams are not reliably seekable. `[RequestSizeLimit]` bounds it.
- **Task 3 intentionally ends with a failing (uncompilable) build, and with no EF migration yet.** It is schema-only; Task 4 removes the `CardLast4`/`PaymentInfo` usages that break it, and only then (Step 4b) generates and applies the migration — `dotnet ef migrations add` needs a design-time build of the whole solution, which can't succeed while Task 3's changes stand alone. Do not "fix" Task 3 by leaving the card field in place, and do not try to force a migration out of Task 3.
