using FashionSaaS.API.Hubs;
using FashionSaaS.Application.Interfaces;
using FashionSaaS.Application.Notifications;
using FashionSaaS.Domain.Enums;
using FashionSaaS.Domain.Events;
using FashionSaaS.Infrastructure.Persistence;
using MediatR;
using Microsoft.AspNetCore.SignalR;

namespace FashionSaaS.API.EventHandlers;

/// <summary>
/// Persists a broadcast notification for tenant admins on every order status transition
/// (confirm/ship/deliver/cancel), then best-effort pushes it to both the
/// <c>tenant:{TenantId}</c> group (admins) and the <c>user:{UserId}</c> group of the customer
/// whose order changed, resolved from the order's <c>Customer</c> record by email match — see
/// design spec §OPEN QUESTIONS/D3 and the remarks on <see cref="Handle"/> for why this is a
/// resolved User id rather than the Customer entity id, and why the match is best-effort. See
/// <see cref="OrderPlacedNotificationHandler"/> for why this lives in the API project.
/// </summary>
public class OrderStatusChangedNotificationHandler(
    NotificationService notificationService,
    ICustomerRepository customerRepository,
    IUserRepository userRepository,
    IHubContext<NotificationsHub> hubContext,
    ILogger<OrderStatusChangedNotificationHandler> logger)
    : INotificationHandler<DomainEventNotification<OrderStatusChangedEvent>>
{
    public async Task Handle(DomainEventNotification<OrderStatusChangedEvent> notification, CancellationToken cancellationToken)
    {
        OrderStatusChangedEvent evt = notification.DomainEvent;
        var title = $"Order {evt.OrderNumber} {evt.NewStatus}";
        var message = $"Order {evt.OrderNumber} moved from {evt.PreviousStatus} to {evt.NewStatus}.";

        Domain.Entities.Notification saved = await notificationService.CreateAsync(
            evt.TenantId, recipientUserId: null, NotificationType.OrderStatusChanged,
            title, message, "Order", evt.OrderId, cancellationToken);

        // CA1031 suppressed on both catches below: "must never throw" boundary (persist-then-
        // push, D2) — the Notification row already committed above, so either group push
        // failing must be swallowed and logged independently, never fail the handler.
#pragma warning disable CA1031
        try
        {
            await hubContext.Clients.Group($"tenant:{evt.TenantId}")
                .SendAsync("ReceiveNotification", saved, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to push OrderStatusChanged live notification to tenant group for order {OrderId}", evt.OrderId);
        }

        // evt.CustomerId is a Customer entity id (Customers.Id), NOT the JWT/User id that
        // NotificationsHub groups connections by ("user:{ClaimTypes.NameIdentifier}") — these
        // are two different entities/tables (see Customer.cs / User.cs), so we cannot push to
        // "user:{evt.CustomerId}" directly. Checkout requires an authenticated Customer-role
        // login (StoreOrdersController is [Authorize(Roles = "Customer")]), and the Customer
        // row is get-or-created from that same authenticated user's email at order time
        // (OrderService.CreateAsync), so resolving the User by that email is the best
        // available reverse link. This is still best-effort, not guaranteed: it misses orders
        // whose Customer row predates this login requirement or was created by an admin
        // (CustomerService) with no corresponding User account, and it goes stale if the User
        // later changes their email without the Customer row being updated to match. In those
        // cases the push is skipped (logged at Information, not a failure) rather than pushed
        // to the wrong recipient.
        try
        {
            Domain.Entities.Customer? customer = await customerRepository.GetByIdAsync(evt.CustomerId);
            Domain.Entities.User? user = customer is null
                ? null
                : await userRepository.GetByEmailAsync(customer.Email);

            if (user is not null)
            {
                await hubContext.Clients.Group($"user:{user.Id}")
                    .SendAsync("ReceiveNotification", saved, cancellationToken);
            }
            else
            {
                logger.LogInformation(
                    "Skipped OrderStatusChanged live push for order {OrderId}: no User account links to Customer {CustomerId}",
                    evt.OrderId, evt.CustomerId);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to push OrderStatusChanged live notification to customer for order {OrderId}", evt.OrderId);
        }
#pragma warning restore CA1031
    }
}
