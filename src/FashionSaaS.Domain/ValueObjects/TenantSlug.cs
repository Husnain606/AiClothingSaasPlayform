using System.Text.RegularExpressions;

namespace FashionSaaS.Domain.ValueObjects;

public sealed class TenantSlug : IEquatable<TenantSlug>
{
    // Matched length is capped at 50 chars (validated below) and the pattern has no nested
    // quantifiers, so catastrophic backtracking isn't reachable — the explicit timeout is a
    // defense-in-depth bound, not a fix for an actual exponential-time pattern.
    private static readonly Regex ValidPattern =
        new(@"^[a-z0-9][a-z0-9\-]{0,48}[a-z0-9]$|^[a-z0-9]$", RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));

    public string Value { get; }

    public TenantSlug(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Slug cannot be empty.", nameof(value));
        if (value.Length > 50)
            throw new ArgumentException("Slug cannot exceed 50 characters.", nameof(value));
        if (!ValidPattern.IsMatch(value))
            throw new ArgumentException("Slug must be lowercase alphanumeric with hyphens only.", nameof(value));
        Value = value;
    }

    public bool Equals(TenantSlug? other) => other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);
    public override bool Equals(object? obj) => obj is TenantSlug other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode(StringComparison.Ordinal);
    public override string ToString() => Value;
}
