using FluentAssertions;

namespace FashionSaaS.TryOn.Domain.Tests;

public class ChatRequestTests
{
    [Fact]
    public void NewChatRequest_HasNonEmptyId()
    {
        var request = new ChatRequest();
        request.Id.Should().NotBeEmpty();
    }

    [Fact]
    public void NewChatRequest_DefaultsToCompletedStatus()
    {
        // ChatRequestStatus.Completed is the enum's zero value (default(ChatRequestStatus)) — this
        // test pins the enum's declared order so a future reordering is caught.
        var request = new ChatRequest();
        request.Status.Should().Be(ChatRequestStatus.Completed);
    }

    [Fact]
    public void ChatRequest_CanBeMarkedFailedWithReason()
    {
        var request = new ChatRequest
        {
            Status = ChatRequestStatus.Failed,
            FailureReason = "Gemini API timeout"
        };
        request.Status.Should().Be(ChatRequestStatus.Failed);
        request.FailureReason.Should().Be("Gemini API timeout");
    }
}
