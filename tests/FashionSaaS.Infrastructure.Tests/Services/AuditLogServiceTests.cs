using FashionSaaS.Domain.Entities;
using FashionSaaS.Infrastructure.Persistence;
using FashionSaaS.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace FashionSaaS.Infrastructure.Tests.Services;

/// <summary>
/// C2 — AuditLogService.MaskSensitive must mask sensitive keys regardless of case
/// (JSON serialises as PascalCase "Iban", but review found original code used "IBAN").
/// The fix is a HashSet with StringComparer.OrdinalIgnoreCase which matches any casing.
/// </summary>
public class AuditLogServiceTests
{
    private static ApplicationDbContext CreateContext()
    {
        DbContextOptions<ApplicationDbContext> options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        // ApplicationDbContext requires ICurrentTenantService; use a minimal stub.
        return new ApplicationDbContext(options, new StubCurrentTenantService());
    }

    // ── C2: case-insensitive masking — "Iban" (PascalCase from JSON serialisation) ──

    [Fact]
    public async Task LogAsync_IbanProperty_IsMaskedInPersistedNewValues()
    {
        await using ApplicationDbContext ctx = CreateContext();
        var svc = new AuditLogService(ctx);

        // "Iban" is the PascalCase JSON key produced by System.Text.Json
        var newValues = new { Iban = "PK36SCBL0000001123456702" };

        await svc.LogAsync(null, null, "BankAccountCreated", "BankAccount",
            Guid.NewGuid(), null, newValues, "127.0.0.1", "xunit");

        AuditLog log = await ctx.AuditLogs.SingleAsync();
        log.NewValues.Should().Contain("***MASKED***");
        log.NewValues.Should().NotContain("PK36SCBL0000001123456702");
    }

    [Fact]
    public async Task LogAsync_IbanUppercaseProperty_IsMaskedInPersistedNewValues()
    {
        await using ApplicationDbContext ctx = CreateContext();
        var svc = new AuditLogService(ctx);

        // "IBAN" uppercase variant must also be masked (case-insensitive match)
        var dict = new Dictionary<string, object>(StringComparer.Ordinal) { ["IBAN"] = "PK36SCBL9999" };

        await svc.LogAsync(null, null, "Test", "BankAccount",
            Guid.NewGuid(), null, dict, "127.0.0.1", "xunit");

        AuditLog log = await ctx.AuditLogs.SingleAsync();
        log.NewValues.Should().Contain("***MASKED***");
        log.NewValues.Should().NotContain("PK36SCBL9999");
    }

    [Fact]
    public async Task LogAsync_AccountNumberProperty_IsMaskedInPersistedOldAndNewValues()
    {
        await using ApplicationDbContext ctx = CreateContext();
        var svc = new AuditLogService(ctx);

        var oldValues = new { AccountNumber = "12345678" };
        var newValues = new { AccountNumber = "87654321" };

        await svc.LogAsync(null, null, "BankAccountUpdated", "BankAccount",
            Guid.NewGuid(), oldValues, newValues, "127.0.0.1", "xunit");

        AuditLog log = await ctx.AuditLogs.SingleAsync();
        log.OldValues.Should().Contain("***MASKED***");
        log.OldValues.Should().NotContain("12345678");
        log.NewValues.Should().Contain("***MASKED***");
        log.NewValues.Should().NotContain("87654321");
    }

    [Fact]
    public async Task LogAsync_NonSensitiveProperty_IsNotMasked()
    {
        await using ApplicationDbContext ctx = CreateContext();
        var svc = new AuditLogService(ctx);

        var newValues = new { BankName = "HBL" };

        await svc.LogAsync(null, null, "BankAccountUpdated", "BankAccount",
            Guid.NewGuid(), null, newValues, "127.0.0.1", "xunit");

        AuditLog log = await ctx.AuditLogs.SingleAsync();
        log.NewValues.Should().Contain("HBL");
        log.NewValues.Should().NotContain("***MASKED***");
    }

    [Fact]
    public async Task LogAsync_NullOldValues_PersistedAsNull()
    {
        await using ApplicationDbContext ctx = CreateContext();
        var svc = new AuditLogService(ctx);

        await svc.LogAsync(null, null, "BankAccountCreated", "BankAccount",
            Guid.NewGuid(), null, new { AccountNumber = "1234" }, "127.0.0.1", "xunit");

        AuditLog log = await ctx.AuditLogs.SingleAsync();
        log.OldValues.Should().BeNull();
        log.NewValues.Should().Contain("***MASKED***");
    }
}

/// <summary>Minimal stub to satisfy ApplicationDbContext's ICurrentTenantService dependency.</summary>
file sealed class StubCurrentTenantService : FashionSaaS.Application.Interfaces.ICurrentTenantService
{
    public Guid? TenantId => null;
    public string? TenantSlug => null;
    public bool IsResolved => false;
    public void SetTenant(Guid tenantId, string slug) { }
}
