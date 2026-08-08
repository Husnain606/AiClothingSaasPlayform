using FluentAssertions;

namespace FashionSaaS.TryOn.Domain.Tests;

public class TryOnStatusTests
{
    [Fact]
    public void TryOnStatus_HasProcessingValue()
    {
        Enum.IsDefined(typeof(TryOnStatus), "Processing").Should().BeTrue();
    }
}
