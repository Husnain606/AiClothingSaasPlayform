using FashionSaaS.API.Logging;
using FluentAssertions;
using Serilog.Core;
using Serilog.Events;

namespace FashionSaaS.Infrastructure.Tests.Logging;

/// <summary>
/// C3 — SensitiveDataDestructuringPolicy must mask SecretBase32 (MFA setup secret)
/// plus all other known-sensitive properties, case-insensitively.
/// </summary>
public class SensitiveDataDestructuringPolicyTests
{
    private readonly SensitiveDataDestructuringPolicy _policy = new();

    private static StructureValue Destructure(SensitiveDataDestructuringPolicy policy, object value)
    {
        var factory = new StubPropertyValueFactory();
        var result = policy.TryDestructure(value, factory, out LogEventPropertyValue? logValue);
        result.Should().BeTrue("policy must intercept objects with sensitive properties");
        return (StructureValue)logValue;
    }

    // ── C3: SecretBase32 is masked ───────────────────────────────────────────

    [Fact]
    public void TryDestructure_ObjectWithSecretBase32_MasksIt()
    {
        var dto = new { SecretBase32 = "JBSWY3DPEHPK3PXP", OtherField = "visible" };

        StructureValue structure = Destructure(_policy, dto);

        LogEventProperty secretProp = structure.Properties.Single(p => string.Equals(p.Name, "SecretBase32", StringComparison.Ordinal));
        secretProp.Value.Should().BeOfType<ScalarValue>()
            .Which.Value.Should().Be("***MASKED***");

        LogEventProperty otherProp = structure.Properties.Single(p => string.Equals(p.Name, "OtherField", StringComparison.Ordinal));
        otherProp.Value.Should().BeOfType<ScalarValue>()
            .Which.Value.Should().Be("visible");
    }

    // ── Existing sensitive properties still masked ───────────────────────────

    [Fact]
    public void TryDestructure_ObjectWithPassword_MasksIt()
    {
        var dto = new { Password = "super-secret", Username = "admin" };

        StructureValue structure = Destructure(_policy, dto);

        structure.Properties.Single(p => string.Equals(p.Name, "Password", StringComparison.Ordinal))
            .Value.Should().BeOfType<ScalarValue>()
            .Which.Value.Should().Be("***MASKED***");
    }

    [Fact]
    public void TryDestructure_ObjectWithIban_MasksIt()
    {
        var dto = new { Iban = "PK36SCBL0000001123456702", BankName = "HBL" };

        StructureValue structure = Destructure(_policy, dto);

        structure.Properties.Single(p => string.Equals(p.Name, "Iban", StringComparison.Ordinal))
            .Value.Should().BeOfType<ScalarValue>()
            .Which.Value.Should().Be("***MASKED***");

        structure.Properties.Single(p => string.Equals(p.Name, "BankName", StringComparison.Ordinal))
            .Value.Should().BeOfType<ScalarValue>()
            .Which.Value.Should().Be("HBL");
    }

    [Fact]
    public void TryDestructure_ObjectWithAccountNumber_MasksIt()
    {
        var dto = new { AccountNumber = "12345678" };

        StructureValue structure = Destructure(_policy, dto);

        structure.Properties.Single(p => string.Equals(p.Name, "AccountNumber", StringComparison.Ordinal))
            .Value.Should().BeOfType<ScalarValue>()
            .Which.Value.Should().Be("***MASKED***");
    }

    // ── Non-sensitive object — policy passes through (returns false) ─────────

    [Fact]
    public void TryDestructure_ObjectWithNoSensitiveProps_ReturnsFalse()
    {
        var dto = new { BankName = "HBL", BranchCode = "0012" };
        var factory = new StubPropertyValueFactory();

        var intercepted = _policy.TryDestructure(dto, factory, out _);

        intercepted.Should().BeFalse("no sensitive properties — policy should not intercept");
    }

    // ── Null and primitive — policy passes through (returns false) ───────────

    [Fact]
    public void TryDestructure_NullValue_ReturnsFalse()
    {
        var factory = new StubPropertyValueFactory();
        var intercepted = _policy.TryDestructure(null!, factory, out _);
        intercepted.Should().BeFalse();
    }

    [Fact]
    public void TryDestructure_StringValue_ReturnsFalse()
    {
        var factory = new StubPropertyValueFactory();
        var intercepted = _policy.TryDestructure("plain string", factory, out _);
        intercepted.Should().BeFalse();
    }
}

/// <summary>
/// Minimal <see cref="ILogEventPropertyValueFactory"/> stub that wraps raw values as
/// <see cref="ScalarValue"/> so property values are inspectable in tests.
/// </summary>
file sealed class StubPropertyValueFactory : ILogEventPropertyValueFactory
{
    public LogEventPropertyValue CreatePropertyValue(object? value, bool destructureObjects = false)
        => new ScalarValue(value);
}
