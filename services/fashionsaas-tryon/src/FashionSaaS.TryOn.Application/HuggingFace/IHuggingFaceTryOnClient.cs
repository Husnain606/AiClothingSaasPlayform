namespace FashionSaaS.TryOn.Application.HuggingFace;

public enum HuggingFaceJobState
{
    Pending,
    Complete,
    Failed
}

#pragma warning disable CA1054
public record HuggingFaceJobResult(HuggingFaceJobState State, string? ResultImageUrl, string? ErrorMessage);
#pragma warning restore CA1054

/// <summary>
/// Talks to your duplicated Hugging Face Space. Not a Refit interface — Gradio's queue API is
/// job-based (submit, then poll an SSE stream), which Refit doesn't model. This is the ONLY
/// abstraction the rest of the try-on flow depends on; if you switch Spaces or providers later,
/// only the Infrastructure implementation changes.
/// </summary>
public interface IHuggingFaceTryOnClient
{
    /// <summary>Submits a render job. Returns the Space's job/event id.</summary>
    Task<string> SubmitAsync(byte[] personPhoto, byte[] garmentImage, CancellationToken ct);

    /// <summary>
    /// Checks a job's current state. Returns Pending (not an exception) for both "still
    /// rendering" and "transient connection problem" — the caller polls again either way.
    /// </summary>
    Task<HuggingFaceJobResult> PollAsync(string jobId, CancellationToken ct);
}
