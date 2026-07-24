using FashionSaaS.Domain.Enums;

namespace FashionSaaS.Domain.Entities;

public class Order : BaseEntity
{
    public Guid TenantId { get; set; }
    public Guid CustomerId { get; set; }
    public string OrderNumber { get; set; } = string.Empty; // ORD-{yyyy}-{000001}, unique per tenant
    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public DateTime OrderDate { get; set; } = DateTime.UtcNow;

    // Flattened shipping snapshot — orders are immutable records
    public string ShippingFirstName { get; set; } = string.Empty;
    public string ShippingLastName { get; set; } = string.Empty;
    public string ShippingEmail { get; set; } = string.Empty;
    public string ShippingPhone { get; set; } = string.Empty;
    public string ShippingStreet { get; set; } = string.Empty;
    public string ShippingCity { get; set; } = string.Empty;
    public string ShippingState { get; set; } = string.Empty;
    public string ShippingZipCode { get; set; } = string.Empty;
    public string ShippingCountry { get; set; } = string.Empty;

    public string CardLast4 { get; set; } = string.Empty; // masked reference ONLY

    public decimal Subtotal { get; set; }
    public decimal Tax { get; set; }
    public decimal ShippingCost { get; set; }
    public decimal Total { get; set; }

    // Discount snapshot — orders are immutable records, so the applied code/amount are
    // captured at order time rather than joined live from the (mutable) Discount row.
    public Guid? DiscountId { get; set; }
    public string? DiscountCode { get; set; }
    public decimal DiscountAmount { get; set; }

    public string? TrackingNumber { get; set; }
    public string? CancelReason { get; set; }

    public Customer? Customer { get; set; }
    public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();

    private static readonly Dictionary<OrderStatus, OrderStatus[]> AllowedTransitions = new()
    {
        [OrderStatus.Pending] = [OrderStatus.Confirmed, OrderStatus.Cancelled],
        [OrderStatus.Confirmed] = [OrderStatus.Shipped, OrderStatus.Cancelled],
        [OrderStatus.Shipped] = [OrderStatus.Delivered],
        [OrderStatus.Delivered] = [],
        [OrderStatus.Cancelled] = []
    };

    public bool CanTransitionTo(OrderStatus target) =>
        AllowedTransitions[Status].Contains(target);
}
