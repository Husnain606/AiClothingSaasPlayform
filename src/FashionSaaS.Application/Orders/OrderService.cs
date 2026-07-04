using FashionSaaS.Application.Common;
using FashionSaaS.Application.Interfaces;
using FashionSaaS.Application.Orders.DTOs;
using FashionSaaS.Domain.Entities;
using FashionSaaS.Domain.Enums;
using Mapster;
using Microsoft.Extensions.Logging;

namespace FashionSaaS.Application.Orders;

/// <summary>
/// Order creation, pricing, stock coupling, and status lifecycle. All pricing is
/// computed server-side from the tenant's own product/variant records — clients can
/// never influence price, tax, or total via the request payload. Stock decrements and
/// restorations are recorded as append-only <see cref="StockAdjustment"/> rows, mirroring
/// <c>InventoryService</c>'s bookkeeping, so the running total and the ledger never diverge.
/// </summary>
public class OrderService(
    IOrderRepository orderRepository,
    ICustomerRepository customerRepository,
    IProductRepository productRepository,
    IProductVariantRepository variantRepository,
    IStockAdjustmentRepository stockAdjustmentRepository,
    IUnitOfWork unitOfWork,
    IAuditLogService auditLogService,
    ICurrentTenantService currentTenant,
    ILogger<OrderService> logger)
{
    private const decimal TaxRate = 0.10m;

    public async Task<ResponseData<OrderDto>> CreateAsync(string customerEmail, string customerFirstName,
        string customerLastName, string? customerPhone, CreateOrderRequest request, Guid actingUserId,
        string ipAddress, string userAgent, CancellationToken ct = default)
    {
        if (currentTenant.TenantId is not { } tenantId)
            return ResponseData<OrderDto>.Failure("Tenant could not be resolved.", 400);

        var orderItems = new List<OrderItem>();
        var stockDecrements = new List<(ProductVariant Variant, int Quantity)>();
        decimal subtotal = 0m;

        foreach (var line in request.Items)
        {
            var product = await productRepository.GetByIdAsync(line.ProductId);
            if (product is null || product.TenantId != tenantId || product.Status != ProductStatus.Active)
                return ResponseData<OrderDto>.Failure($"Product '{line.ProductId}' is not available.", 400);

            ProductVariant? variant = null;
            var wantsVariant = !string.IsNullOrWhiteSpace(line.Variant?.Size) || !string.IsNullOrWhiteSpace(line.Variant?.Color);
            if (wantsVariant)
            {
                var variants = await variantRepository.GetByProductAsync(product.Id, ct);
                variant = variants.FirstOrDefault(v =>
                    string.Equals(v.Size, line.Variant!.Size, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(v.Color, line.Variant!.Color, StringComparison.OrdinalIgnoreCase));

                if (variant is null)
                    return ResponseData<OrderDto>.Failure(
                        $"Requested variant (Size='{line.Variant!.Size}', Color='{line.Variant!.Color}') was not found for product '{product.Name}'.", 400);

                if (variant.StockQuantity < line.Quantity)
                    return ResponseData<OrderDto>.Failure(
                        $"Insufficient stock for product '{product.Name}' (Size='{variant.Size}', Color='{variant.Color}'): requested {line.Quantity}, available {variant.StockQuantity}.", 400);
            }

            var unitPrice = variant?.PriceOverride ?? product.BasePrice;
            subtotal += unitPrice * line.Quantity;

            orderItems.Add(new OrderItem
            {
                ProductId = product.Id,
                ProductVariantId = variant?.Id,
                ProductName = product.Name,
                Size = variant?.Size ?? string.Empty,
                Color = variant?.Color ?? string.Empty,
                UnitPrice = unitPrice,
                Quantity = line.Quantity
            });

            if (variant is not null)
                stockDecrements.Add((variant, line.Quantity));
        }

        var customer = await customerRepository.GetOrCreateByEmailAsync(
            tenantId, customerEmail, customerFirstName, customerLastName, customerPhone, ct);

        var tax = Math.Round(subtotal * TaxRate, 2, MidpointRounding.AwayFromZero);
        const decimal shippingCost = 0m;
        var total = subtotal + tax + shippingCost;

        var orderNumber = $"ORD-{DateTime.UtcNow.Year}-{(await orderRepository.CountForYearAsync(tenantId, DateTime.UtcNow.Year, ct)) + 1:D6}";
        var cardNumber = request.PaymentInfo.CardNumber ?? string.Empty;
        var cardLast4 = cardNumber.Length >= 4 ? cardNumber[^4..] : cardNumber;

        var order = new Order
        {
            TenantId = tenantId,
            CustomerId = customer.Id,
            OrderNumber = orderNumber,
            Status = OrderStatus.Pending,
            OrderDate = DateTime.UtcNow,
            ShippingFirstName = request.ShippingAddress.FirstName,
            ShippingLastName = request.ShippingAddress.LastName,
            ShippingEmail = request.ShippingAddress.Email,
            ShippingPhone = request.ShippingAddress.Phone,
            ShippingStreet = request.ShippingAddress.Street,
            ShippingCity = request.ShippingAddress.City,
            ShippingState = request.ShippingAddress.State,
            ShippingZipCode = request.ShippingAddress.ZipCode,
            ShippingCountry = request.ShippingAddress.Country,
            CardLast4 = cardLast4,
            Subtotal = subtotal,
            Tax = tax,
            ShippingCost = shippingCost,
            Total = total,
            Items = orderItems
        };

        // Decrement stock and record the adjustment ledger only after every line has
        // validated successfully — no partial mutation on a rejected order.
        foreach (var (variant, quantity) in stockDecrements)
        {
            variant.StockQuantity -= quantity;

            // Variant instances come from GetByProductAsync, which reads AsNoTracking (that
            // repository method is shared with read-heavy listing call sites) — explicitly
            // mark this instance as modified so the decrement is actually persisted.
            await variantRepository.UpdateAsync(variant);

            await stockAdjustmentRepository.AddAsync(new StockAdjustment
            {
                TenantId = tenantId,
                ProductVariantId = variant.Id,
                Delta = -quantity,
                Reason = StockAdjustmentReason.OrderPlaced,
                ResultingQuantity = variant.StockQuantity,
                AdjustedByUserId = actingUserId
            });
        }

        await orderRepository.AddAsync(order);
        await unitOfWork.SaveChangesAsync(ct);

        await auditLogService.LogAsync(actingUserId, tenantId, "OrderCreated", "Order", order.Id,
            null, new { order.OrderNumber, order.Subtotal, order.Tax, order.Total }, ipAddress, userAgent);

        logger.LogInformation("Order {OrderNumber} created for tenant {TenantId}", order.OrderNumber, tenantId);

        return ResponseData<OrderDto>.Success(order.Adapt<OrderDto>(), "Order created.", 201);
    }

    public async Task<ResponseData<PagedResult<OrderDto>>> GetAllAsync(OrderFilter filter, CancellationToken ct = default)
    {
        if (currentTenant.TenantId is not { } tenantId)
            return ResponseData<PagedResult<OrderDto>>.Failure("Tenant could not be resolved.", 400);

        filter.TenantId = tenantId;

        var (items, total) = await orderRepository.GetPagedAsync(filter, ct);

        var page = new PagedResult<OrderDto>
        {
            Items = items.Select(o => o.Adapt<OrderDto>()).ToList(),
            TotalCount = total,
            Page = filter.Page,
            PageSize = filter.PageSize
        };

        return ResponseData<PagedResult<OrderDto>>.Success(page);
    }

    public async Task<ResponseData<OrderDto>> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var order = await orderRepository.GetByIdWithItemsAsync(id, ct);
        if (order is null)
            return ResponseData<OrderDto>.Failure("Order not found.", 404);

        return ResponseData<OrderDto>.Success(order.Adapt<OrderDto>());
    }

    public async Task<ResponseData<PagedResult<OrderDto>>> GetForCustomerAsync(string customerEmail, int page,
        int pageSize, CancellationToken ct = default)
    {
        if (currentTenant.TenantId is not { } tenantId)
            return ResponseData<PagedResult<OrderDto>>.Failure("Tenant could not be resolved.", 400);

        var filter = new OrderFilter { TenantId = tenantId, CustomerEmail = customerEmail, Page = page, PageSize = pageSize };
        var (items, total) = await orderRepository.GetPagedAsync(filter, ct);

        var result = new PagedResult<OrderDto>
        {
            Items = items.Select(o => o.Adapt<OrderDto>()).ToList(),
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        };

        return ResponseData<PagedResult<OrderDto>>.Success(result);
    }

    public async Task<ResponseData<OrderDto>> GetByIdForCustomerAsync(Guid id, string customerEmail, CancellationToken ct = default)
    {
        var order = await orderRepository.GetByIdWithItemsAsync(id, ct);
        if (order is null || !string.Equals(order.ShippingEmail, customerEmail, StringComparison.OrdinalIgnoreCase))
            return ResponseData<OrderDto>.Failure("Order not found.", 404);

        return ResponseData<OrderDto>.Success(order.Adapt<OrderDto>());
    }

    public Task<ResponseData<OrderDto>> ConfirmAsync(Guid id, Guid actingUserId, string ipAddress, string userAgent,
        CancellationToken ct = default) =>
        TransitionAsync(id, OrderStatus.Confirmed, "confirm", "OrderConfirmed", actingUserId, ipAddress, userAgent, ct);

    public async Task<ResponseData<OrderDto>> ShipAsync(Guid id, string? trackingNumber, Guid actingUserId,
        string ipAddress, string userAgent, CancellationToken ct = default) =>
        await TransitionAsync(id, OrderStatus.Shipped, "ship", "OrderShipped", actingUserId, ipAddress, userAgent, ct,
            order => order.TrackingNumber = trackingNumber);

    public Task<ResponseData<OrderDto>> DeliverAsync(Guid id, Guid actingUserId, string ipAddress, string userAgent,
        CancellationToken ct = default) =>
        TransitionAsync(id, OrderStatus.Delivered, "deliver", "OrderDelivered", actingUserId, ipAddress, userAgent, ct);

    private async Task<ResponseData<OrderDto>> TransitionAsync(Guid id, OrderStatus target, string actionVerb,
        string auditAction, Guid actingUserId, string ipAddress, string userAgent, CancellationToken ct,
        Action<Order>? beforeSave = null)
    {
        var order = await orderRepository.GetByIdWithItemsAsync(id, ct);
        if (order is null)
            return ResponseData<OrderDto>.Failure("Order not found.", 404);

        if (!order.CanTransitionTo(target))
            return ResponseData<OrderDto>.Failure($"Cannot {actionVerb} an order in status {order.Status}", 400);

        var previousStatus = order.Status;
        order.Status = target;
        beforeSave?.Invoke(order);

        await unitOfWork.SaveChangesAsync(ct);

        await auditLogService.LogAsync(actingUserId, order.TenantId, auditAction, "Order", order.Id,
            new { Status = previousStatus }, new { Status = target }, ipAddress, userAgent);

        logger.LogInformation("Order {OrderNumber} transitioned {From} -> {To}", order.OrderNumber, previousStatus, target);

        return ResponseData<OrderDto>.Success(order.Adapt<OrderDto>());
    }

    public async Task<ResponseData<OrderDto>> CancelAsync(Guid id, string reason, bool asCustomer, string? customerEmail,
        Guid actingUserId, string ipAddress, string userAgent, CancellationToken ct = default)
    {
        var order = await orderRepository.GetByIdWithItemsAsync(id, ct);
        if (order is null)
            return ResponseData<OrderDto>.Failure("Order not found.", 404);

        if (asCustomer && !string.Equals(order.ShippingEmail, customerEmail, StringComparison.OrdinalIgnoreCase))
            return ResponseData<OrderDto>.Failure("Order not found.", 404);

        if (!order.CanTransitionTo(OrderStatus.Cancelled))
            return ResponseData<OrderDto>.Failure($"Cannot cancel an order in status {order.Status}", 400);

        var previousStatus = order.Status;
        order.Status = OrderStatus.Cancelled;
        order.CancelReason = reason;

        foreach (var item in order.Items)
        {
            if (item.ProductVariantId is not { } variantId)
                continue;

            var variant = await variantRepository.GetByIdAsync(variantId);
            if (variant is null)
            {
                logger.LogWarning("Variant {VariantId} missing during stock restore for order {OrderId}", variantId, order.Id);
                continue;
            }

            variant.StockQuantity += item.Quantity;

            await stockAdjustmentRepository.AddAsync(new StockAdjustment
            {
                TenantId = order.TenantId,
                ProductVariantId = variant.Id,
                Delta = item.Quantity,
                Reason = StockAdjustmentReason.OrderCancelled,
                ResultingQuantity = variant.StockQuantity,
                AdjustedByUserId = actingUserId
            });
        }

        await unitOfWork.SaveChangesAsync(ct);

        await auditLogService.LogAsync(actingUserId, order.TenantId, "OrderCancelled", "Order", order.Id,
            new { Status = previousStatus }, new { Status = OrderStatus.Cancelled, order.CancelReason }, ipAddress, userAgent);

        logger.LogInformation("Order {OrderNumber} cancelled: {Reason}", order.OrderNumber, reason);

        return ResponseData<OrderDto>.Success(order.Adapt<OrderDto>());
    }
}
