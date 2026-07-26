using FashionSaaS.Application.Common;
using FashionSaaS.Application.Interfaces;
using FashionSaaS.Application.Orders.DTOs;
using FashionSaaS.Domain.Entities;
using FashionSaaS.Domain.Enums;
using FashionSaaS.Domain.Events;
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
    IDiscountRepository discountRepository,
    IOrderPaymentProofRepository paymentProofRepository,
    IPaymentProofStorageService proofStorage,
    IUnitOfWork unitOfWork,
    IAuditLogService auditLogService,
    ICurrentTenantService currentTenant,
    ILogger<OrderService> logger)
{
    private const decimal TaxRate = 0.10m;

    public async Task<ResponseData<OrderDto>> CreateAsync(string customerEmail, string customerFirstName,
        string customerLastName, string? customerPhone, CreateOrderRequest request, Guid actingUserId,
        string ipAddress, string userAgent, Stream proofContent, string proofFileName, string proofContentType,
        long proofSizeBytes, CancellationToken ct = default)
    {
        if (currentTenant.TenantId is not { } tenantId)
            return ResponseData<OrderDto>.Failure("Tenant could not be resolved.", 400);

        if (proofSizeBytes <= 0 || proofSizeBytes > PaymentProofContentTypes.MaxFileSizeBytes)
            return ResponseData<OrderDto>.Failure("Payment proof must be between 1 byte and 10 MB.", 400);

        if (!PaymentProofContentTypes.IsAllowed(proofContentType))
            return ResponseData<OrderDto>.Failure("Payment proof must be a JPEG, PNG, WebP or PDF file.", 400);

        // Never trust the declared content type: confirm the bytes match it, so a renamed
        // executable cannot reach storage.
        var header = new byte[12];
        var headerLength = await proofContent.ReadAsync(header, ct);
        if (!PaymentProofContentTypes.HeaderMatches(header.AsSpan(0, headerLength), proofContentType))
            return ResponseData<OrderDto>.Failure("Payment proof file contents do not match its type.", 400);

        proofContent.Position = 0;

        var orderItems = new List<OrderItem>();
        var stockDecrements = new List<(ProductVariant Variant, int Quantity)>();
        var subtotal = 0m;

        foreach (CreateOrderItemRequest line in request.Items)
        {
            Product? product = await productRepository.GetByIdAsync(line.ProductId);
            if (product is null || product.TenantId != tenantId || product.Status != ProductStatus.Active)
                return ResponseData<OrderDto>.Failure($"Product '{line.ProductId}' is not available.", 400);

            ProductVariant? variant = null;
            var wantsVariant = !string.IsNullOrWhiteSpace(line.Variant?.Size) || !string.IsNullOrWhiteSpace(line.Variant?.Color);
            if (wantsVariant)
            {
                IReadOnlyList<ProductVariant> variants = await variantRepository.GetByProductAsync(product.Id, ct);
                variant = variants.FirstOrDefault(v =>
                    string.Equals(v.Size, line.Variant!.Size, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(v.Color, line.Variant!.Color, StringComparison.OrdinalIgnoreCase));

                if (variant is null)
                {
                    return ResponseData<OrderDto>.Failure(
                        $"Requested variant (Size='{line.Variant!.Size}', Color='{line.Variant!.Color}') was not found for product '{product.Name}'.", 400);
                }

                if (variant.StockQuantity < line.Quantity)
                {
                    return ResponseData<OrderDto>.Failure(
                        $"Insufficient stock for product '{product.Name}' (Size='{variant.Size}', Color='{variant.Color}'): requested {line.Quantity}, available {variant.StockQuantity}.", 400);
                }
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

        Discount? discount = null;
        var discountAmount = 0m;
        if (!string.IsNullOrWhiteSpace(request.DiscountCode))
        {
            discount = await discountRepository.GetByCodeAsync(tenantId, request.DiscountCode, ct);
            if (discount is null || !discount.IsActive)
                return ResponseData<OrderDto>.Failure("Discount code is not valid.", 400);

            DateTime now = DateTime.UtcNow;
            if (now < discount.StartsAt || now > discount.EndsAt)
                return ResponseData<OrderDto>.Failure("Discount code has expired or is not yet active.", 400);

            if (discount.MaxRedemptions.HasValue && discount.RedemptionCount >= discount.MaxRedemptions.Value)
                return ResponseData<OrderDto>.Failure("Discount code has reached its redemption limit.", 400);

            if (discount.MinOrderAmount.HasValue && subtotal < discount.MinOrderAmount.Value)
            {
                return ResponseData<OrderDto>.Failure(
                    $"Order subtotal must be at least {discount.MinOrderAmount.Value:C} to use this discount code.", 400);
            }

            discountAmount = discount.Type == DiscountType.Percentage
                ? Math.Round(subtotal * discount.Value / 100m, 2, MidpointRounding.AwayFromZero)
                : discount.Value;
            // Never let a fixed-amount discount push the payable subtotal below zero.
            discountAmount = Math.Min(discountAmount, subtotal);
        }

        Customer customer = await customerRepository.GetOrCreateByEmailAsync(
            tenantId, customerEmail, customerFirstName, customerLastName, customerPhone, ct);

        var discountedSubtotal = subtotal - discountAmount;
        var tax = Math.Round(discountedSubtotal * TaxRate, 2, MidpointRounding.AwayFromZero);
        const decimal shippingCost = 0m;
        var total = discountedSubtotal + tax + shippingCost;

        var orderNumber = $"ORD-{DateTime.UtcNow.Year}-{(await orderRepository.CountForYearAsync(tenantId, DateTime.UtcNow.Year, ct)) + 1:D6}";

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
            Subtotal = subtotal,
            Tax = tax,
            ShippingCost = shippingCost,
            Total = total,
            DiscountId = discount?.Id,
            DiscountCode = discount?.Code,
            DiscountAmount = discountAmount,
            Items = orderItems
        };

        // Decrement stock and record the adjustment ledger only after every line has
        // validated successfully — no partial mutation on a rejected order.
        foreach ((ProductVariant? variant, var quantity) in stockDecrements)
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

        if (discount is not null)
        {
            discount.RedemptionCount++;
            await discountRepository.UpdateAsync(discount);
        }

        order.AddDomainEvent(new OrderPlacedEvent(order.Id, tenantId, order.OrderNumber, order.Total));

        await orderRepository.AddAsync(order);

        // Write the binary first so a storage failure aborts before anything is committed.
        var storageKey = $"{tenantId}/{order.Id}/{Guid.NewGuid():N}{PaymentProofContentTypes.ExtensionFor(proofContentType)}";
        try
        {
            await proofStorage.SaveAsync(proofContent, storageKey, ct);
        }
        catch (IOException ex)
        {
            logger.LogError(ex, "Payment proof storage failed for tenant {TenantId}", tenantId);
            return ResponseData<OrderDto>.Failure("We couldn't save your payment proof. Please try again.", 502);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogError(ex, "Payment proof storage denied for tenant {TenantId}", tenantId);
            return ResponseData<OrderDto>.Failure("We couldn't save your payment proof. Please try again.", 502);
        }

        await paymentProofRepository.AddAsync(new OrderPaymentProof
        {
            TenantId = tenantId,
            OrderId = order.Id,
            StorageKey = storageKey,
            ContentType = proofContentType,
            OriginalFileName = proofFileName,
            SizeBytes = proofSizeBytes,
            UploadedAt = DateTime.UtcNow
        });

        try
        {
            // The order, its proof row and the stock decrements commit together — an order can
            // never be persisted without its proof.
            await unitOfWork.SaveChangesAsync(ct);
        }
        // CA1031 deliberately broad: the Application layer has no dependency on EF Core (or any
        // persistence-specific exception type) by design, and any save failure — not only a
        // provider-specific one — must trigger the same orphaned-file cleanup before rethrowing.
        // Nothing here is swallowed; the exception always propagates.
#pragma warning disable CA1031
        catch (Exception)
        {
            // The committed state is the source of truth; an orphaned file is harmless, an
            // order without a proof is not. Cleanup is best-effort and never throws.
            await proofStorage.DeleteAsync(storageKey, ct);
            throw;
        }
#pragma warning restore CA1031

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

        (IReadOnlyList<Order>? items, var total) = await orderRepository.GetPagedAsync(filter, ct);

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
        Order? order = await orderRepository.GetByIdWithItemsAsync(id, ct);
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
        (IReadOnlyList<Order>? items, var total) = await orderRepository.GetPagedAsync(filter, ct);

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
        Order? order = await orderRepository.GetByIdWithItemsAsync(id, ct);
        if (order is null || !string.Equals(order.ShippingEmail, customerEmail, StringComparison.OrdinalIgnoreCase))
            return ResponseData<OrderDto>.Failure("Order not found.", 404);

        return ResponseData<OrderDto>.Success(order.Adapt<OrderDto>());
    }

    /// <summary>Streams a proof for the owning tenant. Cross-tenant reads return 404, never 403.</summary>
    public async Task<ResponseData<PaymentProofFileDto>> GetProofForTenantAsync(Guid orderId,
        CancellationToken ct = default)
    {
        if (currentTenant.TenantId is not { } tenantId)
            return ResponseData<PaymentProofFileDto>.Failure("Tenant could not be resolved.", 400);

        Order? order = await orderRepository.GetByIdWithItemsAsync(orderId, ct);
        if (order is null || order.TenantId != tenantId)
            return ResponseData<PaymentProofFileDto>.Failure("Payment proof not found.", 404);

        return await OpenProofAsync(orderId, ct);
    }

    /// <summary>
    /// Streams a proof for the customer who placed the order. A non-owner gets the same 404 as a
    /// missing order — a 403 would confirm the order exists.
    /// </summary>
    public async Task<ResponseData<PaymentProofFileDto>> GetProofForCustomerAsync(Guid orderId,
        string customerEmail, CancellationToken ct = default)
    {
        Order? order = await orderRepository.GetByIdWithItemsAsync(orderId, ct);
        if (order is null || !string.Equals(order.ShippingEmail, customerEmail, StringComparison.OrdinalIgnoreCase))
            return ResponseData<PaymentProofFileDto>.Failure("Payment proof not found.", 404);

        return await OpenProofAsync(orderId, ct);
    }

    private async Task<ResponseData<PaymentProofFileDto>> OpenProofAsync(Guid orderId, CancellationToken ct)
    {
        OrderPaymentProof? proof = await paymentProofRepository.GetByOrderIdAsync(orderId, ct);
        if (proof is null)
            return ResponseData<PaymentProofFileDto>.Failure("Payment proof not found.", 404);

        try
        {
            Stream content = await proofStorage.OpenReadAsync(proof.StorageKey, ct);
            return ResponseData<PaymentProofFileDto>.Success(new PaymentProofFileDto
            {
                Content = content,
                ContentType = proof.ContentType,
                FileName = proof.OriginalFileName
            });
        }
        catch (FileNotFoundException ex)
        {
            // Row exists but the binary is gone — a storage inconsistency, not a client error.
            logger.LogError(ex, "Payment proof binary missing for order {OrderId}", orderId);
            return ResponseData<PaymentProofFileDto>.Failure("Payment proof is unavailable.", 502);
        }
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
        Order? order = await orderRepository.GetByIdWithItemsAsync(id, ct);
        if (order is null)
            return ResponseData<OrderDto>.Failure("Order not found.", 404);

        if (!order.CanTransitionTo(target))
            return ResponseData<OrderDto>.Failure($"Cannot {actionVerb} an order in status {order.Status}", 400);

        if (target == OrderStatus.Confirmed && order.PaymentProof is null)
        {
            return ResponseData<OrderDto>.Failure(
                "Payment proof is required before confirming this order.", 400);
        }

        OrderStatus previousStatus = order.Status;
        order.Status = target;
        beforeSave?.Invoke(order);
        order.AddDomainEvent(new OrderStatusChangedEvent(
            order.Id, order.TenantId, order.CustomerId, order.OrderNumber, previousStatus, target));

        await unitOfWork.SaveChangesAsync(ct);

        await auditLogService.LogAsync(actingUserId, order.TenantId, auditAction, "Order", order.Id,
            new { Status = previousStatus }, new { Status = target }, ipAddress, userAgent);

        logger.LogInformation("Order {OrderNumber} transitioned {From} -> {To}", order.OrderNumber, previousStatus, target);

        return ResponseData<OrderDto>.Success(order.Adapt<OrderDto>());
    }

    public async Task<ResponseData<OrderDto>> CancelAsync(Guid id, string reason, bool asCustomer, string? customerEmail,
        Guid actingUserId, string ipAddress, string userAgent, CancellationToken ct = default)
    {
        Order? order = await orderRepository.GetByIdWithItemsAsync(id, ct);
        if (order is null)
            return ResponseData<OrderDto>.Failure("Order not found.", 404);

        if (asCustomer && !string.Equals(order.ShippingEmail, customerEmail, StringComparison.OrdinalIgnoreCase))
            return ResponseData<OrderDto>.Failure("Order not found.", 404);

        if (!order.CanTransitionTo(OrderStatus.Cancelled))
            return ResponseData<OrderDto>.Failure($"Cannot cancel an order in status {order.Status}", 400);

        OrderStatus previousStatus = order.Status;
        order.Status = OrderStatus.Cancelled;
        order.CancelReason = reason;

        foreach (OrderItem item in order.Items)
        {
            if (item.ProductVariantId is not { } variantId)
                continue;

            ProductVariant? variant = await variantRepository.GetByIdAsync(variantId);
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

        order.AddDomainEvent(new OrderStatusChangedEvent(
            order.Id, order.TenantId, order.CustomerId, order.OrderNumber, previousStatus, OrderStatus.Cancelled));

        await unitOfWork.SaveChangesAsync(ct);

        await auditLogService.LogAsync(actingUserId, order.TenantId, "OrderCancelled", "Order", order.Id,
            new { Status = previousStatus }, new { Status = OrderStatus.Cancelled, order.CancelReason }, ipAddress, userAgent);

        logger.LogInformation("Order {OrderNumber} cancelled: {Reason}", order.OrderNumber, reason);

        return ResponseData<OrderDto>.Success(order.Adapt<OrderDto>());
    }
}
