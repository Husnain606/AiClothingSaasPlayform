using System.Text;
using System.Text.Json;
using FashionSaaS.TryOn.Application.HuggingFace;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FashionSaaS.TryOn.Infrastructure.HuggingFace;

/// <summary>
/// Hand-rolled Gradio queue client — not Refit, since Refit can't model an SSE poll response.
/// Verify this against your actual duplicated Space's "Use via API" panel (plan Task 2, Step 0)
/// before trusting it in production; the upload-then-submit shape here is the current common
/// Gradio 4.x/5.x pattern, not something this plan could test live.
/// </summary>
public class HuggingFaceTryOnClient : IHuggingFaceTryOnClient
{
    private const string PredictApiName = "tryon"; // CONFIRM against your Space's real api_name (Task 2, Step 0)

    private readonly HttpClient _http;
    private readonly string _spaceUrl;
    private readonly ILogger<HuggingFaceTryOnClient> _logger;

    public HuggingFaceTryOnClient(HttpClient http, IOptions<HuggingFaceSettings> settings, ILogger<HuggingFaceTryOnClient> logger)
    {
        _http = http;
        _spaceUrl = settings.Value.SpaceUrl.TrimEnd('/');
        _http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.Value.ApiToken);
        _logger = logger;
    }

    public async Task<string> SubmitAsync(byte[] personPhoto, byte[] garmentImage, CancellationToken ct)
    {
        var personPath = await UploadAsync(personPhoto, "person.jpg", ct);
        var garmentPath = await UploadAsync(garmentImage, "garment.jpg", ct);

        var payload = new
        {
            data = new object[]
            {
                new { path = personPath, meta = new { _type = "gradio.FileData" } },
                new { path = garmentPath, meta = new { _type = "gradio.FileData" } }
            }
        };

        using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await _http.PostAsync(new Uri($"{_spaceUrl}/call/{PredictApiName}"), content, ct);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("event_id").GetString()
            ?? throw new InvalidOperationException("Hugging Face submit response had no event_id.");
    }

    private async Task<string> UploadAsync(byte[] imageBytes, string fileName, CancellationToken ct)
    {
        using var content = new MultipartFormDataContent();
        using var fileContent = new ByteArrayContent(imageBytes);
        content.Add(fileContent, "files", fileName);

        using HttpResponseMessage response = await _http.PostAsync(new Uri($"{_spaceUrl}/upload"), content, ct);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement[0].GetString()
            ?? throw new InvalidOperationException("Hugging Face upload response had no file path.");
    }

    public async Task<HuggingFaceJobResult> PollAsync(string jobId, CancellationToken ct)
    {
        // Any transient failure (dropped connection, timeout) is reported as Pending, never
        // thrown — the caller (TryOnPollingWorker) just tries again on its next tick, and the
        // 10-minute overall timeout (enforced by the worker, not here) is what actually gives up.
#pragma warning disable CA1031
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"{_spaceUrl}/call/{PredictApiName}/{jobId}"));
            using HttpResponseMessage response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            await using Stream stream = await response.Content.ReadAsStreamAsync(ct);
            using StreamReader reader = new(stream);

            string? currentEvent = null;
            string? line;
            while ((line = await reader.ReadLineAsync(ct)) is not null)
            {
                if (line.StartsWith("event: ", StringComparison.Ordinal))
                {
                    currentEvent = line["event: ".Length..].Trim();
                }
                else if (line.StartsWith("data: ", StringComparison.Ordinal) && currentEvent is not null)
                {
                    var data = line["data: ".Length..];

                    if (string.Equals(currentEvent, "complete", StringComparison.Ordinal))
                    {
                        using var doc = JsonDocument.Parse(data);
                        var resultUrl = doc.RootElement[0].GetProperty("path").GetString();

                        // Gradio's `path` is not guaranteed to be an absolute URL - it can be a
                        // server-side file path. The storefront binds this straight into <img [src]>,
                        // where a relative value would silently resolve against the STOREFRONT's
                        // origin and render a broken image. Only accept an absolute http(s) URL.
                        if (!Uri.TryCreate(resultUrl, UriKind.Absolute, out Uri? parsed)
                            || (!string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)
                                && !string.Equals(parsed.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal)))
                        {
                            return new HuggingFaceJobResult(HuggingFaceJobState.Failed, null,
                                $"Hugging Face returned a result path that is not an absolute http(s) URL: '{resultUrl}'");
                        }

                        return new HuggingFaceJobResult(HuggingFaceJobState.Complete, resultUrl, null);
                    }

                    if (string.Equals(currentEvent, "error", StringComparison.Ordinal))
                    {
                        return new HuggingFaceJobResult(HuggingFaceJobState.Failed, null, data.Trim('"'));
                    }
                }
            }

            return new HuggingFaceJobResult(HuggingFaceJobState.Pending, null, null);
        }
        // `ct.IsCancellationRequested` excluded deliberately: on shutdown the caller's token is what
        // cancelled us, and swallowing that as "Pending" would hide a real cancellation from the
        // worker and keep it looping. Only a timeout/transport failure counts as retryable here.
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException
                                   && !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Transient error polling Hugging Face job {JobId}; will retry", jobId);
            return new HuggingFaceJobResult(HuggingFaceJobState.Pending, null, null);
        }
#pragma warning restore CA1031
    }
}
