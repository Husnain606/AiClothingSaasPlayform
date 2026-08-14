using System.Text.Json;
using System.Text.Json.Serialization;
using FashionSaaS.Domain.Entities;
using FashionSaaS.Domain.Enums;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FashionSaaS.Infrastructure.Tests.Hubs;

/// <summary>
/// Guards the wire contract the storefront depends on when it reacts to a pushed Notification.
/// <para>
/// SignalR does NOT use MVC's JsonOptions: <c>AddControllers().AddJsonOptions(...)</c> — where this
/// API registers <see cref="JsonStringEnumConverter"/> — has no effect on hub payloads, which are
/// serialized by <c>JsonHubProtocolOptions.PayloadSerializerOptions</c>. Every storefront consumer
/// compares <c>notification.type</c> against a string name ('TryOnCompleted', 'OrderStatusChanged',
/// ...), so a numeric enum on the wire makes those comparisons silently never match and the UI waits
/// forever. Program.cs therefore registers the converter on the hub protocol explicitly; these tests
/// pin down why that call is load-bearing and must not be dropped.
/// </para>
/// </summary>
public class NotificationPushContractTests
{
    private static JsonSerializerOptions HubPayloadOptions(bool withEnumConverter)
    {
        ServiceCollection services = new();
        services.AddLogging();

        ISignalRServerBuilder signalR = services.AddSignalR();
        if (withEnumConverter)
        {
            // Must stay identical to Program.cs's AddSignalR().AddJsonProtocol(...) registration.
            signalR.AddJsonProtocol(options =>
                options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        }

        using ServiceProvider provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<JsonHubProtocolOptions>>().Value.PayloadSerializerOptions;
    }

    private static Notification CreateNotification(NotificationType type) => new()
    {
        TenantId = Guid.NewGuid(),
        RecipientUserId = Guid.NewGuid(),
        Type = type,
        Title = "Your try-on is ready",
        Message = "Your virtual try-on has finished rendering.",
        EntityName = "TryOnRequest",
        EntityId = Guid.NewGuid()
    };

    [Fact]
    public void HubPayload_WithEnumConverter_SerializesNotificationTypeAsItsStringName()
    {
        var json = JsonSerializer.Serialize(CreateNotification(NotificationType.TryOnCompleted),
            HubPayloadOptions(withEnumConverter: true));

        json.Should().Contain("\"type\":\"TryOnCompleted\"",
            "the storefront matches notification.type against string names");
    }

    [Fact]
    public void HubPayload_WithoutEnumConverter_SerializesNotificationTypeNumerically()
    {
        // Documents the trap this contract exists to prevent: SignalR's own default does NOT
        // stringify enums, which is why relying on MVC's JsonOptions here would break every
        // string-name comparison in the storefront. If this ever starts failing, SignalR's
        // defaults changed and the sibling test above is what actually matters.
        var json = JsonSerializer.Serialize(CreateNotification(NotificationType.TryOnCompleted),
            HubPayloadOptions(withEnumConverter: false));

        json.Should().Contain($"\"type\":{(int)NotificationType.TryOnCompleted}")
            .And.NotContain("TryOnCompleted");
    }

    [Fact]
    public void HubPayload_UsesCamelCasePropertyNames()
    {
        // product-detail.component.ts reads notification.entityId to match its own request id.
        var json = JsonSerializer.Serialize(CreateNotification(NotificationType.TryOnFailed),
            HubPayloadOptions(withEnumConverter: true));

        json.Should().Contain("entityId").And.NotContain("EntityId");
    }
}
