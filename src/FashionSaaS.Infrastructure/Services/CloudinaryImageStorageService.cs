using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using FashionSaaS.Application.Configuration;
using FashionSaaS.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FashionSaaS.Infrastructure.Services;

/// <summary>
/// Cloudinary-backed image storage. Uses the official CloudinaryDotNet SDK behind the
/// <see cref="IImageStorageService"/> abstraction (CONVENTIONS §1) so Application/Domain stay
/// storage-agnostic. Secrets come from <see cref="CloudinarySettings"/> via the Options pattern (§2)
/// and are never logged (§9).
/// </summary>
public class CloudinaryImageStorageService : IImageStorageService
{
    private readonly Cloudinary _cloudinary;
    private readonly ILogger<CloudinaryImageStorageService> _logger;

    public CloudinaryImageStorageService(
        IOptions<CloudinarySettings> options,
        ILogger<CloudinaryImageStorageService> logger)
    {
        var settings = options.Value;
        _cloudinary = new Cloudinary(new Account(settings.CloudName, settings.ApiKey, settings.ApiSecret));
        _cloudinary.Api.Secure = true;
        _logger = logger;
    }

    public async Task<(string PublicId, string Url)> UploadAsync(
        Stream content, string fileName, string folder, CancellationToken ct = default)
    {
        var uploadParams = new ImageUploadParams
        {
            File = new FileDescription(fileName, content),
            Folder = folder,
        };

        var result = await _cloudinary.UploadAsync(uploadParams, ct);

        if (result.Error is not null || result.SecureUrl is null)
        {
            // Safe message only — never surface Cloudinary credentials or raw error internals.
            _logger.LogError("Cloudinary upload failed for {FileName} (status {StatusCode})",
                fileName, result.StatusCode);
            throw new InvalidOperationException("Image upload failed.");
        }

        return (result.PublicId, result.SecureUrl.ToString());
    }

    public async Task DeleteAsync(string publicId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        // Best-effort: a Cloudinary delete failure must never block the DB row removal.
        try
        {
            var result = await _cloudinary.DestroyAsync(new DeletionParams(publicId));
            if (!string.Equals(result.Result, "ok", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Cloudinary delete returned {Result} for {PublicId}",
                    result.Result, publicId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cloudinary delete failed for {PublicId}", publicId);
        }
    }
}
