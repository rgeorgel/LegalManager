namespace LegalManager.Application.Interfaces;

public interface IStorageService
{
    Task<string> UploadAsync(Stream fileStream, string objectKey, string contentType, CancellationToken ct = default);
    Task<Stream> DownloadAsync(string objectKey, CancellationToken ct = default);
    Task<string> GetPresignedUrlAsync(string objectKey, int expiresInMinutes = 30, CancellationToken ct = default);
    Task DeleteAsync(string objectKey, CancellationToken ct = default);
}