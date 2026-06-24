using Serilog.Core;
using Serilog.Events;

namespace FashionSaaS.API.Logging;

/// <summary>
/// Serilog destructuring policy that replaces sensitive property values with "***MASKED***".
/// Applies to any logged object whose properties match the sensitive-name list (case-insensitive).
/// </summary>
public sealed class SensitiveDataDestructuringPolicy : IDestructuringPolicy
{
    private static readonly HashSet<string> SensitiveProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "Password",
        "PasswordHash",
        "Token",
        "TokenHash",
        "RefreshToken",
        "AccountNumber",
        "Iban",
        "TotpSecret",
        "Secret",
    };

    private const string MaskedValue = "***MASKED***";

    public bool TryDestructure(
        object value,
        ILogEventPropertyValueFactory propertyValueFactory,
        out LogEventPropertyValue result)
    {
        if (value is null)
        {
            result = null!;
            return false;
        }

        var type = value.GetType();

        // Only handle non-primitive, non-string reference types (i.e. DTO / entity / anonymous objects)
        if (type.IsPrimitive || value is string || type.IsEnum || value is IEnumerable<object>)
        {
            result = null!;
            return false;
        }

        var properties = type.GetProperties(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

        if (properties.Length == 0)
        {
            result = null!;
            return false;
        }

        // Only intercept if at least one property matches a sensitive name.
        bool hasSensitive = false;
        foreach (var prop in properties)
        {
            if (SensitiveProperties.Contains(prop.Name))
            {
                hasSensitive = true;
                break;
            }
        }

        if (!hasSensitive)
        {
            result = null!;
            return false;
        }

        var logProperties = new List<LogEventProperty>(properties.Length);
        foreach (var prop in properties)
        {
            if (prop.GetIndexParameters().Length > 0)
                continue; // skip indexed properties

            LogEventPropertyValue propValue;
            if (SensitiveProperties.Contains(prop.Name))
            {
                propValue = new ScalarValue(MaskedValue);
            }
            else
            {
                object? rawValue = null;
                try { rawValue = prop.GetValue(value); }
                catch { /* ignore inaccessible properties */ }

                propValue = propertyValueFactory.CreatePropertyValue(rawValue, destructureObjects: true);
            }

            logProperties.Add(new LogEventProperty(prop.Name, propValue));
        }

        result = new StructureValue(logProperties);
        return true;
    }
}
