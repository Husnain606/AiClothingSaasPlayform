using FashionSaaS.Domain.Entities;
using FashionSaaS.Domain.Events;
using FluentAssertions;

namespace FashionSaaS.Domain.Tests;

public class BaseEntityTests
{
    private class ConcreteEntity : BaseEntity { }
    private class ConcreteEvent : IDomainEvent { }

    [Fact]
    public void NewEntity_HasNonEmptyId()
    {
        var entity = new ConcreteEntity();
        entity.Id.Should().NotBeEmpty();
    }

    [Fact]
    public void AddDomainEvent_StoresEvent()
    {
        var entity = new ConcreteEntity();
        var evt = new ConcreteEvent();
        entity.AddDomainEvent(evt);
        entity.DomainEvents.Should().ContainSingle().Which.Should().Be(evt);
    }

    [Fact]
    public void ClearDomainEvents_RemovesAll()
    {
        var entity = new ConcreteEntity();
        entity.AddDomainEvent(new ConcreteEvent());
        entity.AddDomainEvent(new ConcreteEvent());
        entity.ClearDomainEvents();
        entity.DomainEvents.Should().BeEmpty();
    }
}
