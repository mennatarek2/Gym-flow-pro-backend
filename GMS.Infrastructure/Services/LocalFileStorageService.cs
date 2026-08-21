namespace GMS.Infrastructure.Services;

using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Hosting;
using GMS.Core.Interfaces;

/// <summary>
/// Local file storage for development.
/// Saves files to wwwroot/uploads/{folder}/{uniqueFileName}.
/// Returns relative URL for serving via static files middleware.
/// </summary>
public class LocalFileStorageService : IFileStorageService
{
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<LocalFileStorageService> _logger;

    public LocalFileStorageService(IWebHostEnvironment env, ILogger<LocalFileStorageService> logger)
    {
        _env = env;
        _logger = logger;
    }

    public async Task<string> UploadAsync(Stream stream, string fileName, string folder)
    {
        var uploadsDir = Path.Combine(_env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot"), "uploads", folder);
        Directory.CreateDirectory(uploadsDir);

        // Generate unique filename to avoid collisions
        var ext = Path.GetExtension(fileName);
        var uniqueName = $"{Guid.NewGuid():N}{ext}";
        var filePath = Path.Combine(uploadsDir, uniqueName);

        await using var fileStream = new FileStream(filePath, FileMode.Create);
        await stream.CopyToAsync(fileStream);

        var url = $"/uploads/{folder}/{uniqueName}";

        _logger.LogInformation("File uploaded: {FileName} → {Url}", fileName, url);
        return url;
    }

    public Task DeleteAsync(string fileUrl)
    {
        if (string.IsNullOrEmpty(fileUrl)) return Task.CompletedTask;

        var relativePath = fileUrl.TrimStart('/');
        var fullPath = Path.Combine(_env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot"), relativePath);

        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
            _logger.LogInformation("File deleted: {Path}", fullPath);
        }

        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string fileUrl)
    {
        var fullPath = ResolveLocalPath(fileUrl);
        return Task.FromResult(fullPath != null && File.Exists(fullPath));
    }

    public async Task<byte[]?> TryReadAsync(string fileUrl)
    {
        var fullPath = ResolveLocalPath(fileUrl);
        if (fullPath == null || !File.Exists(fullPath)) return null;
        return await File.ReadAllBytesAsync(fullPath);
    }

    private string? ResolveLocalPath(string fileUrl)
    {
        if (string.IsNullOrWhiteSpace(fileUrl)) return null;
        if (fileUrl.Contains("..", StringComparison.Ordinal)) return null;
        if (!fileUrl.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase)) return null;

        var relativePath = fileUrl.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        var root = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
        var fullPath = Path.GetFullPath(Path.Combine(root, relativePath));
        var uploadsRoot = Path.GetFullPath(Path.Combine(root, "uploads"));
        if (!fullPath.StartsWith(uploadsRoot, StringComparison.OrdinalIgnoreCase)) return null;
        return fullPath;
    }
}
