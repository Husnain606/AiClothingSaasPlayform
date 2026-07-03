namespace FashionSaaS.Application.Orders.DTOs;

public class ShippingAddressDto
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
}

public class CreateOrderItemRequest
{
    public Guid ProductId { get; set; }
    public int Quantity { get; set; }
    public OrderVariantDto? Variant { get; set; }
}

public class OrderVariantDto
{
    public string? Size { get; set; }
    public string? Color { get; set; }
}

public class CreateOrderPaymentDto
{
    public string CardholderName { get; set; } = string.Empty;
    public string CardNumber { get; set; } = string.Empty; // masked "****1111" from storefront; validator enforces
}

public class CreateOrderRequest
{
    public ShippingAddressDto ShippingAddress { get; set; } = new();
    public CreateOrderPaymentDto PaymentInfo { get; set; } = new();
    public List<CreateOrderItemRequest> Items { get; set; } = [];
}

public class OrderItemDto
{
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int Quantity { get; set; }
    public OrderVariantDto? Variant { get; set; }
}

public class OrderDto
{
    public string OrderId { get; set; } = string.Empty;        // OrderNumber, e.g. ORD-2026-000001 (storefront contract)
    public Guid Id { get; set; }                                 // internal Guid for admin detail routes
    public Guid CustomerId { get; set; }
    public DateTime OrderDate { get; set; }
    public string Status { get; set; } = string.Empty;           // lowercase: pending|confirmed|shipped|delivered|cancelled
    public List<OrderItemDto> Items { get; set; } = [];
    public ShippingAddressDto ShippingAddress { get; set; } = new();
    public decimal Subtotal { get; set; }
    public decimal Tax { get; set; }
    public decimal ShippingCost { get; set; }
    public decimal Total { get; set; }
    public string? TrackingNumber { get; set; }
}

public record CancelOrderRequest(string Reason);

public record ShipOrderRequest(string? TrackingNumber);

public class OrderFilter
{
    public Guid? TenantId { get; set; }
    public FashionSaaS.Domain.Enums.OrderStatus? Status { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public Guid? CustomerId { get; set; }
    public string? CustomerEmail { get; set; }   // matches ShippingEmail exactly
    public string? Search { get; set; }   // matches OrderNumber contains
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
