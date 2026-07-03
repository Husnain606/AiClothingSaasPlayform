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
        if (from > to)
            return "'from' must be earlier than or equal to 'to'.";
        if ((to - from).TotalDays > MaxRangeDays)
            return $"Date range cannot exceed {MaxRangeDays} days.";
        return null;
    }
}
