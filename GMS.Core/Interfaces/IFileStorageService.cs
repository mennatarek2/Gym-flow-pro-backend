namespace GMS.Core.Interfaces;

/// <summary>
/// File storage service abstraction.
/// Development: local wwwroot/uploads
/// Production: R2/S3/Azure Blob (future)
/// </summary>
public interface IFileStorageService
{
    /// <summary>
    /// Uploads a file and returns its public URL.
    /// </summary>
    /// <param name="stream">File content stream.</param>
    /// <param name="fileName">Original filename.</param>
    /// <param name="folder">Subfolder (e.g. "profile-photos", "receipts").</param>
    /// <returns>Public URL to access the file.</returns>
    Task<string> UploadAsync(Stream stream, string fileName, string folder);

    /// <summary>
    /// Deletes a file by its URL or path.
    /// </summary>
    Task DeleteAsync(string fileUrl);

    /// <summary>
    /// Checks if a file exists.
    /// </summary>
    Task<bool> ExistsAsync(string fileUrl);

    /// <summary>
    /// Reads file bytes when the URL is locally stored. Returns null when missing or remote-only.
    /// Default is null so existing test doubles need no change.
    /// </summary>
    Task<byte[]?> TryReadAsync(string fileUrl) => Task.FromResult<byte[]?>(null);
}
