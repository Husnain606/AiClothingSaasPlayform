namespace FashionSaaS.Application.Interfaces;

/// <summary>
/// Binary storage for customer payment proofs. The single swap point between local-disk
/// storage (development) and Azure Blob Storage (deployed): implement this interface and
/// change the one registration in Infrastructure's DependencyInjection — no calling code changes.
/// <para>
/// The caller owns the storage key, so keys stay meaningful across providers (a relative path
/// locally, a blob name in Azure). Implementations must reject any key that escapes their root.
/// </para>
/// </summary>
public interface IPaymentProofStorageService
{
    Task SaveAsync(Stream content, string storageKey, CancellationToken ct = default);

    /// <summary>Opens the stored proof for reading. Throws <see cref="FileNotFoundException"/> if absent.</summary>
    Task<Stream> OpenReadAsync(string storageKey, CancellationToken ct = default);

    /// <summary>Best-effort removal used for orphan cleanup. Must never throw.</summary>
    Task DeleteAsync(string storageKey, CancellationToken ct = default);
}
