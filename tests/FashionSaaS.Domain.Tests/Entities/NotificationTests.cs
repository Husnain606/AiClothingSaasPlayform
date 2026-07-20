using FashionSaaS.Domain.Entities;
using FluentAssertions;

namespace FashionSaaS.Domain.Tests.Entities;

public class NotificationTests
{
    [Fact]
    public void Notification_DefaultsIsReadFalse()
    {
        var notification = new Notification();

        notification.IsRead.Should().BeFalse();
        notification.ReadAt.Should().BeNull();
    }
}
