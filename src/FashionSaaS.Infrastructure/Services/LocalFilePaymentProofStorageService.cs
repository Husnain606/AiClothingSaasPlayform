using FashionSaaS.Application.Configuration;
using FashionSaaS.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FashionSaaS.Infrastructure.Services;

/// <summary>
/// Local-filesystem payment-proof storage for development. Every key is resolved against the
/// configured root and rejected if it escapes it, so a crafted key can never read or write
/// outside the proof directory. Replaced wholesale by an Azure Blob implementation at deploy
/// time — see <see cref="IPaymentProofStorageService"/>.
/// </summary>
public class LocalFilePaymentProofStorageService : IPaymentProofStorageService
{
    private readonly string _root;
    private readonly ILogger<LocalFilePaymentProofStorageService> _logger;

    public LocalFilePaymentProofStorageService(
        IOptions<PaymentProofStorageSettings> options,
        ILogger<LocalFilePaymentProofStorageService> logger)
    {
        _root = Path.GetFullPath(options.Value.RootPath);
        _logger = logger;
    }

    public async Task SaveAsync(Stream content, string storageKey, CancellationToken ct = default)
    {
        var path = ResolveWithinRoot(storageKey);

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        await content.CopyToAsync(file, ct);
    }

    public Task<Stream> OpenReadAsync(string storageKey, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var path = ResolveWithinRoot(storageKey);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Payment proof not found.", storageKey);
        }

        Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string storageKey, CancellationToken ct = default)
    {
        // Best-effort by contract: orphan cleanup must never surface an error to the caller,
        // whose database work has already been decided. CA1031 suppressed deliberately —
        // every exception type is swallowed here by design, not just specific ones.
#pragma warning disable CA1031
        try
        {
            var path = ResolveWithinRoot(storageKey);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Payment proof delete failed for key {StorageKey}", storageKey);
        }
#pragma warning restore CA1031

        return Task.CompletedTask;
    }

    /// <summary>
    /// Resolves a storage key under the configured root, rejecting absolute paths and any
    /// key that traverses outside it.
    /// </summary>
    private string ResolveWithinRoot(string storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey) || Path.IsPathRooted(storageKey))
        {
            throw new InvalidOperationException("Invalid payment proof storage key.");
        }

        var candidate = Path.GetFullPath(Path.Combine(_root, storageKey));

        var rootWithSeparator = _root.EndsWith(Path.DirectorySeparatorChar)
            ? _root
            : _root + Path.DirectorySeparatorChar;

        if (!candidate.StartsWith(rootWithSeparator, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Invalid payment proof storage key.");
        }

        return candidate;
    }
}
