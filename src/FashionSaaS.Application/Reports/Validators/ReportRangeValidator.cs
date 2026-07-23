namespace FashionSaaS.Application.Reports.Validators;

/// <summary>
/// Shared range guard for all report queries: from ≤ to and span ≤ 366 days.
/// Returns an error message on violation, or null when the range is valid.
/// </summary>
public static class ReportRangeValidator
{
    public const int MaxRangeDays = 366;

    public static string? Validate(DateTime from, DateTime to)
    {
        // A non-nullable DateTime query parameter that's simply omitted from the request binds
        // to default(DateTime) rather than failing model validation - so an omitted 'from'/'to'
        // previously slipped past this guard entirely (default <= default, span = 0 <= 366) and
        // silently returned an empty/zero report instead of a clear 400. Reject explicitly.
        if (from == default || to == default)
            return "'from' and 'to' are required query parameters.";
        if (from > to)
            return "'from' must be earlier than or equal to 'to'.";
        if ((to - from).TotalDays > MaxRangeDays)
            return $"Date range cannot exceed {MaxRangeDays} days.";
        return null;
    }
}
