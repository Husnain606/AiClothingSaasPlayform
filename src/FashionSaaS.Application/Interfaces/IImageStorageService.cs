namespace FashionSaaS.Application.Interfaces;

public interface IImageStorageService
{
    Task<(string PublicId, string Url)> UploadAsync(Stream content, string fileName, string folder, CancellationToken ct = default);
    Task DeleteAsync(string publicId, CancellationToken ct = default);
}
