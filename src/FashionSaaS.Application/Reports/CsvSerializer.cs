using System.Globalization;
using System.Reflection;
using System.Text;

namespace FashionSaaS.Application.Reports;

public static class CsvSerializer
{
    public static string Serialize<T>(IEnumerable<T> rows)
    {
        PropertyInfo[] props = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(',', props.Select(p => Escape(p.Name))));
        foreach (T? row in rows)
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
        field.Contains(',', StringComparison.Ordinal)
        || field.Contains('"', StringComparison.Ordinal)
        || field.Contains('\n', StringComparison.Ordinal)
            ? $"\"{field.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
            : field;
}
