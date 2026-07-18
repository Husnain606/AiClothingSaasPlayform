using FluentAssertions;

namespace FashionSaaS.TryOn.Domain.Tests;

public class MeasurementRequestTests
{
    [Fact]
    public void NewMeasurementRequest_HasNonEmptyId()
    {
        var request = new MeasurementRequest();
        request.Id.Should().NotBeEmpty();
    }

    [Fact]
    public void NewMeasurementRequest_DefaultsToCompletedStatus()
    {
        // MeasurementStatus.Completed is the enum's zero value (default(MeasurementStatus)) — this
        // test pins the enum's declared order so a future reordering is caught.
        var request = new MeasurementRequest();
        request.Status.Should().Be(MeasurementStatus.Completed);
    }

    [Fact]
    public void MeasurementRequest_CanBeMarkedFailedWithReason()
    {
        var request = new MeasurementRequest
        {
            Status = MeasurementStatus.Failed,
            FailureReason = "Gemini API timeout"
        };
        request.Status.Should().Be(MeasurementStatus.Failed);
        request.FailureReason.Should().Be("Gemini API timeout");
    }
}
