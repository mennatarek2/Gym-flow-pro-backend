namespace GMS.Infrastructure.Services;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Hosting;
using GMS.Core.Configuration;
using GMS.Core.Interfaces;

/// <summary>
/// Local file storage for development, and for the Local Edition's on-disk uploads.
/// Saves files to {uploadsRoot}/{folder}/{uniqueFileName}.
///
/// SaaS (and Local without an explicit override): uploads root is wwwroot/uploads, returning
/// "/uploads/..." URLs served anonymously by the existing static-file middleware — unchanged
/// behavior either way.
///
/// Local Edition defaults its uploads root to %ProgramData%\HyMotion\uploads instead — computed
/// here via Environment.SpecialFolder rather than a config string, since "%ProgramData%" has no
/// meaning to .NET configuration (it's shell syntax, not something IConfiguration expands), and a
/// literal resolved path would hardcode a machine-specific assumption. Files there live outside
/// wwwroot and are NOT anonymously exposed: returned URLs use the "/local-uploads/..." prefix,
/// served only by the authenticated LocalUploadsController — never by static-file middleware.
/// An explicit "FileStorage:RootPath" setting overrides either default, for either edition.
/// This is all additive: the SaaS "/uploads/..." contract is untouched.
/// </summary>
public class LocalFileStorageService : IFileStorageService
{
    public const string DefaultUrlPrefix = "/uploads/";
    public const string CustomRootUrlPrefix = "/local-uploads/";

    private readonly IWebHostEnvironment _env;
    private readonly ILogger<LocalFileStorageService> _logger;
    private readonly string _uploadsRoot;
    private readonly string _urlPrefix;

    /// <summary>The resolved physical uploads root. Exposed read-only for diagnostics/tests — not part of IFileStorageService.</summary>
    public string UploadsRoot => _uploadsRoot;

    public LocalFileStorageService(
        IWebHostEnvironment env, ILogger<LocalFileStorageService> logger, IConfiguration configuration, DeploymentEdition edition)
    {
        _env = env;
        _logger = logger;

        var configuredRoot = configuration["FileStorage:RootPath"];
        if (!string.IsNullOrWhiteSpace(configuredRoot))
        {
            _uploadsRoot = configuredRoot;
            _urlPrefix = CustomRootUrlPrefix;
        }
        else if (edition == DeploymentEdition.Local)
        {
            _uploadsRoot = GMS.Core.Configuration.LocalRuntimePaths.UploadsDir;
            _urlPrefix = CustomRootUrlPrefix;
        }
        else
        {
            _uploadsRoot = Path.Combine(_env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot"), "uploads");
            _urlPrefix = DefaultUrlPrefix;
        }
    }

    public async Task<string> UploadAsync(Stream stream, string fileName, string folder)
    {
        var uploadsDir = Path.Combine(_uploadsRoot, folder);
        Directory.CreateDirectory(uploadsDir);

        // Generate unique filename to avoid collisions
        var ext = Path.GetExtension(fileName);
        var uniqueName = $"{Guid.NewGuid():N}{ext}";
        var filePath = Path.Combine(uploadsDir, uniqueName);

        await using var fileStream = new FileStream(filePath, FileMode.Create);
        await stream.CopyToAsync(fileStream);

        var url = $"{_urlPrefix}{folder}/{uniqueName}";

        _logger.LogInformation("File uploaded: {FileName} → {Url}", fileName, url);
        return url;
    }

    public Task DeleteAsync(string fileUrl)
    {
        if (string.IsNullOrEmpty(fileUrl)) return Task.CompletedTask;

        var fullPath = ResolveLocalPath(fileUrl);
        if (fullPath == null) return Task.CompletedTask;

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
        if (!fileUrl.StartsWith(_urlPrefix, StringComparison.OrdinalIgnoreCase)) return null;

        var relativePath = fileUrl[_urlPrefix.Length..].Replace('/', Path.DirectorySeparatorChar);
        var uploadsRoot = Path.GetFullPath(_uploadsRoot);
        var fullPath = Path.GetFullPath(Path.Combine(uploadsRoot, relativePath));
        if (!fullPath.StartsWith(uploadsRoot, StringComparison.OrdinalIgnoreCase)) return null;
        return fullPath;
    }
}
