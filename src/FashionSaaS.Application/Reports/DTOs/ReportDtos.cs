namespace FashionSaaS.Application.Reports.DTOs;

/// <summary>Inclusive UTC date range for report queries.</summary>
public class ReportRange
{
    public DateTime From { get; set; }
    public DateTime To { get; set; }
}

public class SummaryReportDto
{
    /// <summary>Σ Order.Total where Status != Cancelled and OrderDate in [from, to].</summary>
    public decimal Revenue { get; set; }
    /// <summary>Non-cancelled orders in range.</summary>
    public int OrderCount { get; set; }
    /// <summary>Revenue / OrderCount; 0 when there are no orders.</summary>
    public decimal AvgOrderValue { get; set; }
    /// <summary>Customers with CreatedAt in range.</summary>
    public int NewCustomers { get; set; }
    /// <summary>Reviews with Status == Pending (not yet approved/rejected), tenant-wide.</summary>
    public int PendingReviews { get; set; }
    /// <summary>Active variants with StockQuantity &lt;= 5, tenant-wide.</summary>
    public int LowStockCount { get; set; }
}

/// <summary>
/// One time bucket. PeriodStart is the bucket start (Day = calendar date;
/// Week = Monday-start ISO week; Month = first of month — all UTC).
/// </summary>
public class SalesPointDto
{
    public DateTime PeriodStart { get; set; }
    public decimal Revenue { get; set; }
    public int OrderCount { get; set; }
}

public class TopProductDto
{
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal Revenue { get; set; }
    public int Units { get; set; }
}

public class StatusBreakdownDto
{
    public string Status { get; set; } = string.Empty;
    public int Count { get; set; }
    public decimal Revenue { get; set; }
}

public class CustomerAnalyticsDto
{
    /// <summary>New-customer counts per bucket (OrderCount = customers created; Revenue is always 0).</summary>
    public List<SalesPointDto> NewCustomersOverTime { get; set; } = [];
    /// <summary>Customers with ≥2 non-cancelled orders in range ÷ customers with ≥1 (0 when denominator is 0).</summary>
    public double RepeatPurchaseRate { get; set; }
    public List<TopCustomerDto> TopCustomers { get; set; } = [];
}

public class TopCustomerDto
{
    public Guid CustomerId { get; set; }
    public string Email { get; set; } = string.Empty;
    public decimal TotalSpend { get; set; }
    public int OrderCount { get; set; }
}

public class InventoryTrendsDto
{
    /// <summary>
    /// Stock-adjustment activity per day bucket: OrderCount = number of adjustments,
    /// Revenue = Σ |Delta| (absolute quantity moved).
    /// </summary>
    public List<SalesPointDto> AdjustmentsOverTime { get; set; } = [];
    /// <summary>Active variants at or below the low-stock threshold (StockQuantity &lt;= 5).</summary>
    public List<LowStockItemDto> LowStock { get; set; } = [];
}

public class LowStockItemDto
{
    public Guid VariantId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public int StockQuantity { get; set; }
}

/// <summary>
/// Sales for one category. Aggregates the category's DIRECT products only —
/// descendants' products are NOT rolled up (drill down via categoryId instead).
/// </summary>
public class CategorySalesDto
{
    public Guid CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public decimal Revenue { get; set; }
    public int Units { get; set; }
}

public enum ReportInterval
{
    Day,
    Week,
    Month
}
