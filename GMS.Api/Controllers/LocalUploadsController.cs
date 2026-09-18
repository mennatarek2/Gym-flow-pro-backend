namespace GMS.Api.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using GMS.Core.Interfaces;

/// <summary>
/// Serves files uploaded under a Local Edition's %ProgramData%\HyMotion\uploads root (see
/// LocalFileStorageService.CustomRootUrlPrefix). Uploaded files can contain member documents,
/// invoices, and other sensitive gym data, so — unlike the SaaS "/uploads/..." path, which the
/// existing anonymous static-file middleware serves from wwwroot — this endpoint requires
/// authentication by default. It reuses IFileStorageService.TryReadAsync for reading, so
/// path-traversal protection lives in exactly one place (LocalFileStorageService.ResolveLocalPath).
/// This route only ever receives requests when FileStorage:RootPath is configured (Local); on
/// SaaS no file is ever stored under this prefix, so requests here 404 via the normal "not found"
/// path with no separate edition gate needed.
///
/// Exception: catalog/branding images (product photos, gym logo — folders "products-{tenantId}"
/// and "logos-{tenantId}", see InventoryCatalogController/TenantSettingsController) are rendered
/// via plain &lt;img src&gt; tags all over the dashboard (POS tiles, product lists, header logo).
/// A browser never attaches the app's bearer token to an &lt;img&gt; request, so under a blanket
/// [Authorize] those always 401 and render as broken images — found via Local Edition POS
/// testing. Those two folder prefixes hold no sensitive data (public-facing catalog/branding
/// assets, same trust level as SaaS's anonymous "/uploads/..."), so they're served anonymously;
/// every other folder (employee documents, invoices, z-reports, imports) still requires auth.
/// </summary>
[Route("local-uploads")]
public class LocalUploadsController : BaseApiController
{
    private static readonly FileExtensionContentTypeProvider ContentTypeProvider = new();
    private static readonly string[] PublicFolderPrefixes = ["products-", "logos-"];

    private readonly IFileStorageService _fileStorage;

    public LocalUploadsController(IFileStorageService fileStorage)
    {
        _fileStorage = fileStorage;
    }

    /// <summary>GET /local-uploads/{folder}/{fileName}</summary>
    [HttpGet("{folder}/{fileName}")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(string folder, string fileName)
    {
        var isPublicFolder = PublicFolderPrefixes.Any(p => folder.StartsWith(p, StringComparison.OrdinalIgnoreCase));
        if (!isPublicFolder && User.Identity?.IsAuthenticated != true)
            return Unauthorized();

        var fileUrl = $"/local-uploads/{folder}/{fileName}";
        var bytes = await _fileStorage.TryReadAsync(fileUrl);
        if (bytes == null)
            return NotFound();

        if (!ContentTypeProvider.TryGetContentType(fileName, out var contentType))
            contentType = "application/octet-stream";

        return File(bytes, contentType);
    }
}
