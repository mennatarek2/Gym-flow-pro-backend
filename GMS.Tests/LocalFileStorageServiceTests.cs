namespace GMS.Tests;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using GMS.Core.Configuration;
using GMS.Infrastructure.Services;

/// <summary>
/// Local Edition file storage: default uploads root switches to %ProgramData%\HyMotion\uploads
/// for Local (served only via the authenticated LocalUploadsController — "/local-uploads/..."),
/// stays at wwwroot/uploads for SaaS (unchanged, "/uploads/..." via anonymous static files), and
/// path resolution rejects traversal regardless of edition.
/// </summary>
public class LocalFileStorageServiceTests : IDisposable
{
    private sealed class FakeWebHostEnvironment : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = "";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = "GMS.Tests";
        public string EnvironmentName { get; set; } = "Development";
    }

    private readonly string _contentRoot;

    public LocalFileStorageServiceTests()
    {
        _contentRoot = Path.Combine(Path.GetTempPath(), "hymotion-filestorage-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_contentRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_contentRoot))
            Directory.Delete(_contentRoot, recursive: true);
    }

    private LocalFileStorageService NewService(DeploymentEdition edition, string? rootPathOverride = null)
    {
        var env = new FakeWebHostEnvironment { ContentRootPath = _contentRoot, WebRootPath = Path.Combine(_contentRoot, "wwwroot") };
        var values = new Dictionary<string, string?>();
        if (rootPathOverride != null) values["FileStorage:RootPath"] = rootPathOverride;
        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        return new LocalFileStorageService(env, NullLogger<LocalFileStorageService>.Instance, config, edition);
    }

    [Fact]
    public async Task SaaS_DefaultsToWwwrootUploads_AndUploadsUrlPrefix()
    {
        var svc = NewService(DeploymentEdition.SaaS);
        await using var stream = new MemoryStream(new byte[] { 1, 2, 3 });

        var url = await svc.UploadAsync(stream, "photo.jpg", "profile-photos");

        Assert.StartsWith("/uploads/profile-photos/", url);
        Assert.True(await svc.ExistsAsync(url));
    }

    [Fact]
    public void Local_WithoutOverride_DefaultsRootToProgramDataHyMotionUploads()
    {
        // Path computation only — must NOT perform any I/O against the real machine-wide
        // %ProgramData%\HyMotion\uploads here (no override means that's where this points).
        var svc = NewService(DeploymentEdition.Local);

        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "HyMotion", "uploads");
        Assert.Equal(expected, svc.UploadsRoot);
    }

    [Fact]
    public async Task Local_WithOverride_UsesLocalUploadsUrlPrefix_AndRoundTrips()
    {
        // Exercises the exact same code path (_urlPrefix = CustomRootUrlPrefix) that the true
        // Local default would use, but confined to this test's temp directory.
        var customRoot = Path.Combine(_contentRoot, "programdata-stand-in", "uploads");
        var svc = NewService(DeploymentEdition.Local, customRoot);
        await using var stream = new MemoryStream(new byte[] { 1, 2, 3 });

        var url = await svc.UploadAsync(stream, "receipt.pdf", "receipts");

        Assert.StartsWith("/local-uploads/receipts/", url);
        Assert.True(await svc.ExistsAsync(url));
        Assert.Equal(new byte[] { 1, 2, 3 }, await svc.TryReadAsync(url));

        await svc.DeleteAsync(url);
        Assert.False(await svc.ExistsAsync(url));
    }

    [Theory]
    [InlineData("/local-uploads/../../secrets.txt")]
    [InlineData("/local-uploads/receipts/../../../etc/passwd")]
    [InlineData("/uploads/receipts/x.txt")] // wrong prefix for this instance's mode
    [InlineData("not-a-path")]
    public async Task Local_RejectsTraversalAndWrongPrefixUrls(string maliciousUrl)
    {
        var svc = NewService(DeploymentEdition.Local, Path.Combine(_contentRoot, "programdata-stand-in", "uploads"));

        Assert.Null(await svc.TryReadAsync(maliciousUrl));
        Assert.False(await svc.ExistsAsync(maliciousUrl));
    }

    [Fact]
    public async Task ExplicitRootPathOverride_WorksForEitherEdition()
    {
        var customRoot = Path.Combine(_contentRoot, "custom-uploads-root");
        var svc = NewService(DeploymentEdition.SaaS, customRoot);
        await using var stream = new MemoryStream(new byte[] { 5 });

        var url = await svc.UploadAsync(stream, "x.png", "logos");

        Assert.StartsWith("/local-uploads/logos/", url); // custom root always uses the authenticated prefix
        Assert.True(File.Exists(Directory.GetFiles(Path.Combine(customRoot, "logos")).Single()));
    }
}
