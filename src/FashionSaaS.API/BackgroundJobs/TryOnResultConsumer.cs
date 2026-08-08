using System.Text.Json;
using Azure.Messaging.ServiceBus;
using FashionSaaS.API.Hubs;
using FashionSaaS.Application.Configuration;
using FashionSaaS.Application.Notifications;
using FashionSaaS.Domain.Enums;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace FashionSaaS.API.BackgroundJobs;

/// <summary>
/// The wire shape published by the try-on microservice's ServiceBusTryOnEventPublisher
/// (services/fashionsaas-tryon/.../Messaging/TryOnResultEvent.cs). Duplicated here — the two
/// services are separate deployables with no shared assembly — and must be kept in sync with
/// that type by hand if it ever changes.
/// </summary>
internal sealed record TryOnResultMessage(
    Guid TryOnRequestId,
    Guid TenantId,
    Guid CustomerId,
    Guid ProductId,
    DateTime CreatedAt,
    bool IsSuccess,
    string? ResultImageUrl,
    string? FailureReason);

/// <summary>
/// Consumes try-on result events from the try-on microservice and turns each into a persisted
/// <see cref="Domain.Entities.Notification"/> plus a best-effort live SignalR push to the
/// customer who requested the render — the first Service Bus CONSUMER in this codebase (the
/// try-on service was publish-only). Lives in the API project, not Infrastructure, because it
/// needs <see cref="IHubContext{THub}"/> — same reasoning as OrderPlacedNotificationHandler.
/// Not a MediatR handler: this is triggered by a cross-service broker message, not an in-process
/// domain event.
/// </summary>
public class TryOnResultConsumer(
    NotificationService notificationService,
    IHubContext<NotificationsHub> hubContext,
    ILogger<TryOnResultConsumer> logger,
    ServiceBusClient client,
    IOptions<ServiceBusSettings> settings) : BackgroundService
{
    private static readonly JsonSerializerOptions DeserializeOptions = new() { PropertyNameCaseInsensitive = true };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        ServiceBusSettings config = settings.Value;

        // `await using` ties the processor's lifetime to this method, so shutdown disposes it
        // asynchronously — no Dispose() override doing sync-over-async to clean it up.
        await using ServiceBusProcessor processor = client.CreateProcessor(config.TopicName, config.SubscriptionName);

        processor.ProcessMessageAsync += async args =>
        {
            await HandleMessageAsync(args.Message, args.CancellationToken);
            await args.CompleteMessageAsync(args.Message, args.CancellationToken);
        };

        processor.ProcessErrorAsync += args =>
        {
            logger.LogError(args.Exception, "TryOnResultConsumer processor error on {EntityPath}", args.EntityPath);
            return Task.CompletedTask;
        };

        await processor.StartProcessingAsync(stoppingToken);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            await processor.StopProcessingAsync(CancellationToken.None);
        }
    }

    public async Task HandleMessageAsync(ServiceBusReceivedMessage message, CancellationToken ct)
    {
        TryOnResultMessage? evt;
        try
        {
            evt = JsonSerializer.Deserialize<TryOnResultMessage>(message.Body.ToArray(), DeserializeOptions);
        }
        catch (JsonException ex)
        {
            // A malformed body is a poison message: log and let the caller complete it rather
            // than rethrowing into an endless redeliver-and-fail loop.
            logger.LogWarning(ex, "TryOnResultConsumer received a message that could not be deserialized");
            return;
        }

        if (evt is null)
        {
            logger.LogWarning("TryOnResultConsumer received an empty try-on result message");
            return;
        }

        NotificationType type = evt.IsSuccess ? NotificationType.TryOnCompleted : NotificationType.TryOnFailed;
        var title = evt.IsSuccess ? "Your try-on is ready" : "Your try-on couldn't be completed";
        var body = evt.IsSuccess
            ? "Your virtual try-on has finished rendering."
            : $"Your try-on couldn't be completed: {evt.FailureReason}";

        // RecipientUserId is the customer's User id: the try-on service resolves CustomerId from
        // the JWT's NameIdentifier claim, the same claim NotificationsHub keys user groups by.
        Domain.Entities.Notification saved = await notificationService.CreateAsync(
            evt.TenantId, evt.CustomerId, type, title, body, "TryOnRequest", evt.TryOnRequestId, ct);

        try
        {
            await hubContext.Clients.Group($"user:{evt.CustomerId}")
                .SendAsync("ReceiveNotification", saved, ct);
        }
        // CA1031 suppressed: same "must never throw" boundary as OrderPlacedNotificationHandler —
        // the Notification row already committed above, so a live-push failure of ANY kind must be
        // swallowed and logged rather than fail message processing (which would redeliver it and
        // duplicate the notification).
#pragma warning disable CA1031
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to push try-on result notification for request {TryOnRequestId}", evt.TryOnRequestId);
        }
#pragma warning restore CA1031
    }
}
