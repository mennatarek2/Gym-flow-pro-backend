namespace GMS.Application.Interfaces;

using GMS.Application.Common;
using GMS.Application.DTOs.Admin;

/// <summary>
/// Service interface for tenant settings management.
/// </summary>
public interface ITenantSettingsService
{
    /// <summary>
    /// Get tenant settings.
    /// </summary>
    Task<Result<TenantSettingsDto>> GetTenantSettingsAsync(Guid tenantId);

    /// <summary>
    /// Update tenant settings.
    /// </summary>
    Task<Result<TenantSettingsDto>> UpdateTenantSettingsAsync(Guid tenantId, UpdateTenantSettingsRequest request);

    /// <summary>
    /// Get gym code for current tenant.
    /// </summary>
    Task<Result<string>> GetGymCodeAsync(Guid tenantId);

    /// <summary>
    /// Get QR code poster URL for current tenant.
    /// </summary>
    Task<Result<string>> GetQRPosterUrlAsync(Guid tenantId);

    /// <summary>Reads the tax/invoice keys out of Tenant.Settings (JSON).</summary>
    Task<Result<TaxSettingsDto>> GetTaxSettingsAsync(Guid tenantId);

    /// <summary>Merges the tax/invoice keys into Tenant.Settings (JSON), preserving other keys. Audited.</summary>
    Task<Result<TaxSettingsDto>> UpdateTaxSettingsAsync(Guid tenantId, UpdateTaxSettingsRequest request);

    /// <summary>INVS-10 inventory alert keys from Tenant.Settings.</summary>
    Task<Result<InventoryAlertSettingsDto>> GetInventoryAlertSettingsAsync(Guid tenantId);

    /// <summary>Merges inventory alert keys into Tenant.Settings. Audited.</summary>
    Task<Result<InventoryAlertSettingsDto>> UpdateInventoryAlertSettingsAsync(
        Guid tenantId, UpdateInventoryAlertSettingsRequest request);

    /// <summary>Staff-readable branding (names, logo, colors) for current tenant.</summary>
    Task<Result<TenantBrandingDto>> GetBrandingAsync(Guid tenantId);

    /// <summary>Staff-readable gym-wide Quick Actions keys. Missing config → default four keys (never empty-as-unset).</summary>
    Task<Result<QuickActionsSettingsDto>> GetQuickActionsAsync(Guid tenantId);

    /// <summary>Replace gym-wide Quick Actions (Manager+). Audited. Empty list is stored as-is.</summary>
    Task<Result<QuickActionsSettingsDto>> UpdateQuickActionsAsync(Guid tenantId, UpdateQuickActionsRequest request);

    /// <summary>Persist logo URL after upload (Owner flow). Does not accept tenantId from client.</summary>
    Task<Result<TenantSettingsDto>> SetLogoUrlAsync(Guid tenantId, string? logoUrl);

    /// <summary>Clear logo URL (and optionally delete local file via caller).</summary>
    Task<Result<TenantSettingsDto>> ClearLogoAsync(Guid tenantId);
}
