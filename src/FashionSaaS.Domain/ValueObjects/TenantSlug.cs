using System.Text.RegularExpressions;

namespace FashionSaaS.Domain.ValueObjects;

public class TenantSlug : IEquatable<TenantSlug>
{
    private static readonly Regex ValidPattern = new(@"^[a-z0-9][a-z0-9\-]{0,48}[a-z0-9]$|^[a-z0-9]$", RegexOptions.Compiled);

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

    public bool Equals(TenantSlug? other) => other is not null && Value == other.Value;
    public override bool Equals(object? obj) => obj is TenantSlug other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode();
    public override string ToString() => Value;
}
