using FashionSaaS.Domain.Entities;
using FashionSaaS.Domain.Enums;
using FluentAssertions;

namespace FashionSaaS.Domain.Tests.Entities;

public class OrderTests
{
    [Theory]
    [InlineData(OrderStatus.Pending, OrderStatus.Confirmed, true)]
    [InlineData(OrderStatus.Confirmed, OrderStatus.Shipped, true)]
    [InlineData(OrderStatus.Shipped, OrderStatus.Delivered, true)]
    [InlineData(OrderStatus.Pending, OrderStatus.Cancelled, true)]
    [InlineData(OrderStatus.Confirmed, OrderStatus.Cancelled, true)]
    [InlineData(OrderStatus.Pending, OrderStatus.Shipped, false)]
    [InlineData(OrderStatus.Pending, OrderStatus.Delivered, false)]
    [InlineData(OrderStatus.Shipped, OrderStatus.Cancelled, false)]
    [InlineData(OrderStatus.Delivered, OrderStatus.Cancelled, false)]
    [InlineData(OrderStatus.Cancelled, OrderStatus.Confirmed, false)]
    [InlineData(OrderStatus.Delivered, OrderStatus.Shipped, false)]
    public void CanTransitionTo_EnforcesLifecycle(OrderStatus from, OrderStatus to, bool expected)
    {
        var order = new Order { Status = from };
        order.CanTransitionTo(to).Should().Be(expected);
    }

    [Fact]
    public void NewOrder_DefaultsToPending()
    {
        new Order().Status.Should().Be(OrderStatus.Pending);
    }
}
