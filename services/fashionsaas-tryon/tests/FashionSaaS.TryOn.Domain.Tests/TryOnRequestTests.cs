using FluentAssertions;

namespace FashionSaaS.TryOn.Domain.Tests;

public class TryOnRequestTests
{
    [Fact]
    public void NewTryOnRequest_HasNonEmptyId()
    {
        var request = new TryOnRequest();
        request.Id.Should().NotBeEmpty();
    }

    [Fact]
    public void NewTryOnRequest_DefaultsToCompletedStatus()
    {
        // TryOnStatus.Completed is the enum's zero value (default(TryOnStatus)) — this
        // test pins the enum's declared order so a future reordering is caught.
        var request = new TryOnRequest();
        request.Status.Should().Be(TryOnStatus.Completed);
    }

    [Fact]
    public void TryOnRequest_CanBeMarkedFailedWithReason()
    {
        var request = new TryOnRequest
        {
            Status = TryOnStatus.Failed,
            FailureReason = "Gemini API timeout"
        };
        request.Status.Should().Be(TryOnStatus.Failed);
        request.FailureReason.Should().Be("Gemini API timeout");
    }
}
