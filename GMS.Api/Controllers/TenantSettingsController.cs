namespace GMS.Api.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GMS.Application.DTOs.Admin;
using GMS.Application.Interfaces;
using GMS.Core.Constants;
using GMS.Core.Interfaces;

/// <summary>
/// Tenant settings controller for gym configuration and branding.
/// Includes gym name, code, logo, contact info, and QR code generation.
/// </summary>
[Route("api/settings")]
public class TenantSettingsController : BaseApiController
{
    private readonly ITenantSettingsService _settingsService;
    private readonly ITenantContext _tenantContext;
    private readonly IFileStorageService _files;
    private readonly ILogger<TenantSettingsController> _logger;

    public TenantSettingsController(
        ITenantSettingsService settingsService,
        ITenantContext tenantContext,
        IFileStorageService files,
        ILogger<TenantSettingsController> logger)
    {
        _settingsService = settingsService;
        _tenantContext = tenantContext;
        _files = files;
        _logger = logger;
    }

    /// <summary>
    /// Get tenant settings (owner only).
    /// GET /api/settings
    /// </summary>
    [HttpGet]
    [Authorize(Policy = "OwnerOnly")]
    [ProducesResponseType(typeof(TenantSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTenantSettings()
    {
        var tenantId = _tenantContext.TenantId;
        var result = await _settingsService.GetTenantSettingsAsync(tenantId);

        if (!result.IsSuccess)
        {
            _logger.LogWarning("Failed to retrieve settings for tenant {TenantId}: {Error}", 
                tenantId, result.Error);
            return NotFound(new { error = result.Error, message = result.Message });
        }

        _logger.LogInformation("Tenant settings retrieved: {TenantId}", tenantId);
        return Ok(result.Data);
    }

    /// <summary>
    /// Update tenant settings (owner only).
    /// PUT /api/settings
    /// </summary>
    [HttpPut]
    [Authorize(Policy = "OwnerOnly")]
    [ProducesResponseType(typeof(TenantSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateTenantSettings([FromBody] UpdateTenantSettingsRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.GymName) || string.IsNullOrWhiteSpace(request.GymNameAr))
            return BadRequest(new 
            { 
                error = "Gym name and Arabic name are required / اسم الصالة والاسم العربي مطلوبان",
                message = "Please provide both English and Arabic gym names"
            });

        var tenantId = _tenantContext.TenantId;
        var result = await _settingsService.UpdateTenantSettingsAsync(tenantId, request);

        if (!result.IsSuccess)
        {
            _logger.LogWarning("Failed to update settings for tenant {TenantId}: {Error}", 
                tenantId, result.Error);
            return BadRequest(new { error = result.Error, message = result.Message });
        }

        _logger.LogInformation("Tenant settings updated: {TenantId} ({GymName})", 
            tenantId, request.GymName);
        return Ok(result.Data);
    }

    /// <summary>
    /// Staff-readable gym branding (names, logo, colors). Tenant from auth context only.
    /// GET /api/settings/branding
    /// </summary>
    [HttpGet("branding")]
    [Authorize]
    [ProducesResponseType(typeof(TenantBrandingDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBranding()
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _settingsService.GetBrandingAsync(_tenantContext.TenantId);
        if (!result.IsSuccess)
            return NotFound(new { error = result.Error, message = result.Message });
        return Ok(result.Data);
    }

    /// <summary>
    /// Upload gym logo (Owner). Stored under uploads/logos-{tenantId}/. Tenant from context only.
    /// POST /api/settings/logo
    /// </summary>
    [HttpPost("logo")]
    [Authorize(Policy = "OwnerOnly")]
    [RequestSizeLimit(2 * 1024 * 1024)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> UploadLogo(IFormFile? file, CancellationToken ct)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        if (file == null || file.Length == 0)
            return BadRequest(new { error = "No image uploaded / لم يتم رفع صورة" });

        if (file.Length > 2 * 1024 * 1024)
            return BadRequest(new { error = "Image must be ≤ 2MB / الصورة يجب ألا تتجاوز 2 ميجا" });

        var contentType = (file.ContentType ?? string.Empty).Trim().ToLowerInvariant();
        var isAllowed =
            contentType is "image/jpeg" or "image/jpg" or "image/png" or "image/webp" or "image/gif";
        if (!isAllowed)
            return BadRequest(new { error = "Only JPEG/PNG/WebP/GIF images / صور JPEG أو PNG أو WebP أو GIF فقط" });

        var extension = Path.GetExtension(file.FileName);
        if (string.IsNullOrWhiteSpace(extension) || extension.Length > 8)
        {
            extension = contentType switch
            {
                "image/png" => ".png",
                "image/webp" => ".webp",
                "image/gif" => ".gif",
                _ => ".jpg"
            };
        }

        var safeName = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        var folder = $"logos-{_tenantContext.TenantId:N}";
        await using var stream = file.OpenReadStream();
        var relativeUrl = await _files.UploadAsync(stream, safeName, folder);

        var absolute = $"{Request.Scheme}://{Request.Host}{relativeUrl}";
        var storeUrl = absolute.Length > 500 ? relativeUrl : absolute;

        // Prefer relative for multi-host (ngrok/localhost); FE resolves via API origin.
        var result = await _settingsService.SetLogoUrlAsync(_tenantContext.TenantId, relativeUrl);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error, message = result.Message });

        return Ok(new
        {
            logoUrl = result.Data!.LogoUrl,
            relativeUrl,
            absoluteUrl = storeUrl,
            settings = result.Data
        });
    }

    /// <summary>
    /// Remove gym logo (Owner). Tenant from context only.
    /// DELETE /api/settings/logo
    /// </summary>
    [HttpDelete("logo")]
    [Authorize(Policy = "OwnerOnly")]
    [ProducesResponseType(typeof(TenantSettingsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteLogo()
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var current = await _settingsService.GetTenantSettingsAsync(_tenantContext.TenantId);
        if (current.IsSuccess && !string.IsNullOrWhiteSpace(current.Data?.LogoUrl))
        {
            var url = current.Data!.LogoUrl!;
            if (url.Contains($"/uploads/logos-{_tenantContext.TenantId:N}/", StringComparison.OrdinalIgnoreCase)
                || url.StartsWith($"/uploads/logos-{_tenantContext.TenantId:N}/", StringComparison.OrdinalIgnoreCase))
            {
                try { await _files.DeleteAsync(url.Contains("://") ? new Uri(url).AbsolutePath : url); }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Logo file delete skipped for tenant {TenantId}", _tenantContext.TenantId);
                }
            }
        }

        var result = await _settingsService.ClearLogoAsync(_tenantContext.TenantId);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error, message = result.Message });
        return Ok(result.Data);
    }

    /// <summary>
    /// Gym-wide Quick Actions for the dashboard. Any staff (incl. Receptionist). Never 404 when unset.
    /// GET /api/settings/quick-actions
    /// </summary>
    [HttpGet("quick-actions")]
    [Authorize(Roles = "Owner,Manager,Trainer,Receptionist")]
    [ProducesResponseType(typeof(QuickActionsSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetQuickActions()
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _settingsService.GetQuickActionsAsync(_tenantContext.TenantId);
        if (!result.IsSuccess)
            return NotFound(new { error = result.Error, message = result.Message });
        return Ok(result.Data);
    }

    /// <summary>
    /// Replace gym-wide Quick Actions (Owner + Manager). Trainer/Receptionist/Member → 403.
    /// PUT /api/settings/quick-actions
    /// </summary>
    [HttpPut("quick-actions")]
    [Authorize(Policy = "ManagerOrAbove")]
    [ProducesResponseType(typeof(QuickActionsSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> UpdateQuickActions([FromBody] UpdateQuickActionsRequest request)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _settingsService.UpdateQuickActionsAsync(
            _tenantContext.TenantId,
            request ?? new UpdateQuickActionsRequest());
        if (!result.IsSuccess)
        {
            if (result.Error == QuickActionKeys.ValidationError)
            {
                return Problem(
                    title: QuickActionKeys.ValidationError,
                    detail: result.Message,
                    statusCode: StatusCodes.Status400BadRequest);
            }

            _logger.LogWarning("Failed to update quick actions for tenant {TenantId}: {Error}",
                _tenantContext.TenantId, result.Error);
            return BadRequest(new { error = result.Error, message = result.Message });
        }

        return Ok(result.Data);
    }

    /// <summary>
    /// Get gym code for current tenant (accessible by any staff).
    /// GET /api/settings/gym-code
    /// </summary>
    [HttpGet("gym-code")]
    [Authorize]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetGymCode()
    {
        var tenantId = _tenantContext.TenantId;
        var result = await _settingsService.GetGymCodeAsync(tenantId);

        if (!result.IsSuccess)
        {
            _logger.LogWarning("Failed to retrieve gym code for tenant {TenantId}: {Error}", 
                tenantId, result.Error);
            return NotFound(new { error = result.Error, message = result.Message });
        }

        _logger.LogInformation("Gym code retrieved: {TenantId}", tenantId);
        return Ok(new { gymCode = result.Data });
    }

    /// <summary>
    /// Get QR code poster URL for current tenant (accessible by any staff).
    /// GET /api/settings/qr-poster
    /// </summary>
    [HttpGet("qr-poster")]
    [Authorize]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetQRPosterUrl()
    {
        var tenantId = _tenantContext.TenantId;
        var result = await _settingsService.GetQRPosterUrlAsync(tenantId);

        if (!result.IsSuccess)
        {
            _logger.LogWarning("Failed to retrieve QR poster URL for tenant {TenantId}: {Error}",
                tenantId, result.Error);
            return NotFound(new { error = result.Error, message = result.Message });
        }

        _logger.LogInformation("QR poster URL retrieved: {TenantId}", tenantId);
        return Ok(new { qrPosterUrl = result.Data });
    }

    /// <summary>
    /// Get tax/invoice settings (VAT, tax registration number, invoice footer text) (owner only).
    /// GET /api/settings/tax
    /// </summary>
    [HttpGet("tax")]
    [Authorize(Policy = "OwnerOnly")]
    [ProducesResponseType(typeof(TaxSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTaxSettings()
    {
        var tenantId = _tenantContext.TenantId;
        var result = await _settingsService.GetTaxSettingsAsync(tenantId);

        if (!result.IsSuccess)
        {
            _logger.LogWarning("Failed to retrieve tax settings for tenant {TenantId}: {Error}",
                tenantId, result.Error);
            return NotFound(new { error = result.Error, message = result.Message });
        }

        return Ok(result.Data);
    }

    /// <summary>
    /// Update tax/invoice settings (owner only). Audited (before/after).
    /// PUT /api/settings/tax
    /// </summary>
    [HttpPut("tax")]
    [Authorize(Policy = "OwnerOnly")]
    [ProducesResponseType(typeof(TaxSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateTaxSettings([FromBody] UpdateTaxSettingsRequest request)
    {
        var tenantId = _tenantContext.TenantId;
        var result = await _settingsService.UpdateTaxSettingsAsync(tenantId, request);

        if (!result.IsSuccess)
        {
            _logger.LogWarning("Failed to update tax settings for tenant {TenantId}: {Error}",
                tenantId, result.Error);
            return BadRequest(new { error = result.Error, message = result.Message });
        }

        return Ok(result.Data);
    }

    /// <summary>INVS-10 inventory alert settings (owner only). GET /api/settings/inventory-alerts</summary>
    [HttpGet("inventory-alerts")]
    [Authorize(Policy = "OwnerOnly")]
    [ProducesResponseType(typeof(InventoryAlertSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetInventoryAlertSettings()
    {
        var tenantId = _tenantContext.TenantId;
        var result = await _settingsService.GetInventoryAlertSettingsAsync(tenantId);
        if (!result.IsSuccess)
        {
            _logger.LogWarning("Failed to retrieve inventory alert settings for tenant {TenantId}: {Error}",
                tenantId, result.Error);
            return NotFound(new { error = result.Error, message = result.Message });
        }
        return Ok(result.Data);
    }

    /// <summary>INVS-10 update inventory alert settings (owner only). PUT /api/settings/inventory-alerts</summary>
    [HttpPut("inventory-alerts")]
    [Authorize(Policy = "OwnerOnly")]
    [ProducesResponseType(typeof(InventoryAlertSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateInventoryAlertSettings([FromBody] UpdateInventoryAlertSettingsRequest request)
    {
        var tenantId = _tenantContext.TenantId;
        var result = await _settingsService.UpdateInventoryAlertSettingsAsync(tenantId, request);
        if (!result.IsSuccess)
        {
            _logger.LogWarning("Failed to update inventory alert settings for tenant {TenantId}: {Error}",
                tenantId, result.Error);
            return BadRequest(new { error = result.Error, message = result.Message });
        }
        return Ok(result.Data);
    }
}
