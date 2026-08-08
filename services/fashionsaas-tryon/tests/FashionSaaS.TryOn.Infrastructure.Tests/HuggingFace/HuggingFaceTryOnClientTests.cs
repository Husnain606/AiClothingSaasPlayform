using System.Net;
using System.Text;
using FashionSaaS.TryOn.Application.HuggingFace;
using FashionSaaS.TryOn.Infrastructure.HuggingFace;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FashionSaaS.TryOn.Infrastructure.Tests.HuggingFace;

public class HuggingFaceTryOnClientTests
{
    // Short-lived test doubles here are cleaned up at process exit; CA2000 is suppressed to
    // match the existing StubHandler pattern used elsewhere in this test suite.
#pragma warning disable CA2000
    private static HuggingFaceTryOnClient CreateClient(HttpMessageHandler handler) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("https://test-space.hf.space") },
            Options.Create(new HuggingFaceSettings { SpaceUrl = "https://test-space.hf.space", ApiToken = "test-token" }),
            NullLogger<HuggingFaceTryOnClient>.Instance);

    [Fact]
    public async Task SubmitAsync_UploadsBothImagesThenSubmits_ReturnsEventId()
    {
        var handler = new SequenceHandler(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[\"/tmp/person.jpg\"]") },
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[\"/tmp/garment.jpg\"]") },
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"event_id\":\"evt-123\"}") });

        HuggingFaceTryOnClient client = CreateClient(handler);

        var jobId = await client.SubmitAsync([1, 2, 3], [4, 5, 6], CancellationToken.None);

        jobId.Should().Be("evt-123");
        handler.Requests.Should().HaveCount(3);
        handler.Requests[2].RequestUri!.PathAndQuery.Should().Contain("/call/");
    }

    [Fact]
    public async Task PollAsync_SseCompleteEvent_ReturnsCompleteWithResultUrl()
    {
        const string sse = "event: complete\ndata: [{\"path\": \"https://test-space.hf.space/file=/tmp/result.png\"}]\n\n";
        var handler = new SequenceHandler(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(sse, Encoding.UTF8, "text/event-stream") });

        HuggingFaceTryOnClient client = CreateClient(handler);

        HuggingFaceJobResult result = await client.PollAsync("evt-123", CancellationToken.None);

        result.State.Should().Be(HuggingFaceJobState.Complete);
        result.ResultImageUrl.Should().Be("https://test-space.hf.space/file=/tmp/result.png");
    }

    [Fact]
    public async Task PollAsync_SseErrorEvent_ReturnsFailedWithMessage()
    {
        const string sse = "event: error\ndata: \"CUDA out of memory\"\n\n";
        var handler = new SequenceHandler(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(sse, Encoding.UTF8, "text/event-stream") });

        HuggingFaceTryOnClient client = CreateClient(handler);

        HuggingFaceJobResult result = await client.PollAsync("evt-123", CancellationToken.None);

        result.State.Should().Be(HuggingFaceJobState.Failed);
        result.ErrorMessage.Should().Contain("CUDA out of memory");
    }

    [Fact]
    public async Task PollAsync_NoTerminalEventYet_ReturnsPending()
    {
        const string sse = "event: generating\ndata: {\"progress\": 0.4}\n\n";
        var handler = new SequenceHandler(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(sse, Encoding.UTF8, "text/event-stream") });

        HuggingFaceTryOnClient client = CreateClient(handler);

        HuggingFaceJobResult result = await client.PollAsync("evt-123", CancellationToken.None);

        result.State.Should().Be(HuggingFaceJobState.Pending);
    }

    [Fact]
    public async Task PollAsync_ConnectionDrops_ReturnsPendingNotThrow()
    {
        var handler = new SequenceHandler(new HttpRequestException("connection reset"));

        HuggingFaceTryOnClient client = CreateClient(handler);

        HuggingFaceJobResult result = await client.PollAsync("evt-123", CancellationToken.None);

        result.State.Should().Be(HuggingFaceJobState.Pending);
    }
#pragma warning restore CA2000
}

/// <summary>Replays a fixed sequence of responses (or throws) per call, in order — one per SendAsync.</summary>
internal sealed class SequenceHandler : HttpMessageHandler
{
    private readonly Queue<object> _queue;
    public List<HttpRequestMessage> Requests { get; } = [];

    public SequenceHandler(params object[] responsesOrExceptions) => _queue = new Queue<object>(responsesOrExceptions);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        var next = _queue.Count > 0 ? _queue.Dequeue() : throw new InvalidOperationException("No more queued responses.");
        if (next is Exception ex)
        {
            throw ex;
        }

        return Task.FromResult((HttpResponseMessage)next);
    }
}
