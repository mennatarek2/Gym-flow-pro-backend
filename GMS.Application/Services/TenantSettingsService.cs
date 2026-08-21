namespace GMS.Application.Services;

using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using GMS.Application.Common;
using GMS.Application.DTOs.Admin;
using GMS.Application.Interfaces;
using GMS.Core.Constants;
using GMS.Infrastructure.Persistence;

/// <summary>
/// Service for tenant settings management.
/// Handles gym configuration, branding, QR codes, and tax/invoice settings (JSON-backed).
/// </summary>
public class TenantSettingsService : ITenantSettingsService
{
    private readonly GymFlowProDbContext _dbContext;
    private readonly IAuditService _auditService;
    private readonly ILogger<TenantSettingsService> _logger;

    public TenantSettingsService(
        GymFlowProDbContext dbContext,
        IAuditService auditService,
        ILogger<TenantSettingsService> logger)
    {
        _dbContext = dbContext;
        _auditService = auditService;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<Result<TenantSettingsDto>> GetTenantSettingsAsync(Guid tenantId)
    {
        try
        {
            var tenant = await _dbContext.Tenants
                .FirstOrDefaultAsync(t => t.Id == tenantId);

            if (tenant == null)
                return Result<TenantSettingsDto>.Failure(
                    "Tenant not found / المنظمة غير موجودة");

            return Result<TenantSettingsDto>.Success(MapSettingsDto(tenant));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving tenant settings for {TenantId}", tenantId);
            return Result<TenantSettingsDto>.Failure(
                "Failed to retrieve tenant settings / فشل في جلب إعدادات المنظمة",
                ex.Message);
        }
    }

    /// <inheritdoc/>
    public async Task<Result<TenantSettingsDto>> UpdateTenantSettingsAsync(
        Guid tenantId, UpdateTenantSettingsRequest request)
    {
        try
        {
            var tenant = await _dbContext.Tenants
                .FirstOrDefaultAsync(t => t.Id == tenantId);

            if (tenant == null)
                return Result<TenantSettingsDto>.Failure(
                    "Tenant not found / المنظمة غير موجودة");

            // Reject invalid hex when the client sent a color (empty → keep default).
            if (!string.IsNullOrWhiteSpace(request.PrimaryColor)
                && AccessCardHtmlBuilder.NormalizeHex(request.PrimaryColor) == null)
                return Result<TenantSettingsDto>.Failure("Invalid primary color / لون أساسي غير صالح");
            if (!string.IsNullOrWhiteSpace(request.SecondaryColor)
                && AccessCardHtmlBuilder.NormalizeHex(request.SecondaryColor) == null)
                return Result<TenantSettingsDto>.Failure("Invalid secondary color / لون ثانوي غير صالح");
            if (!string.IsNullOrWhiteSpace(request.AccentColor)
                && AccessCardHtmlBuilder.NormalizeHex(request.AccentColor) == null)
                return Result<TenantSettingsDto>.Failure("Invalid accent color / لون مميز غير صالح");
            if (!string.IsNullOrWhiteSpace(request.CardPrimaryColor)
                && AccessCardHtmlBuilder.NormalizeHex(request.CardPrimaryColor) == null)
                return Result<TenantSettingsDto>.Failure("Invalid card color / لون البطاقة غير صالح");

            if (request.GymMaxCapacity.HasValue
                && (request.GymMaxCapacity.Value < 1 || request.GymMaxCapacity.Value > 9999))
                return Result<TenantSettingsDto>.Failure(
                    "Maximum inside must be between 1 and 9999 / الحد الأقصى للداخل يجب أن يكون بين 1 و 9999");

            var primary = AccessCardHtmlBuilder.NormalizeHex(request.PrimaryColor) ?? BrandingDefaults.PrimaryColor;
            var secondary = AccessCardHtmlBuilder.NormalizeHex(request.SecondaryColor) ?? BrandingDefaults.SecondaryColor;
            var accent = AccessCardHtmlBuilder.NormalizeHex(request.AccentColor) ?? BrandingDefaults.AccentColor;
            var cardPrimary = AccessCardHtmlBuilder.NormalizeHex(request.CardPrimaryColor) ?? primary;

            tenant.Name = request.GymName.Trim();
            tenant.NameAr = request.GymNameAr.Trim();
            tenant.LogoUrl = string.IsNullOrWhiteSpace(request.LogoUrl) ? string.Empty : request.LogoUrl.Trim();
            tenant.PhoneNumber = string.IsNullOrWhiteSpace(request.PhoneNumber) ? string.Empty : request.PhoneNumber.Trim();
            tenant.Address = string.IsNullOrWhiteSpace(request.Address) ? string.Empty : request.Address.Trim();
            tenant.Email = string.IsNullOrWhiteSpace(request.Email) ? string.Empty : request.Email.Trim();
            tenant.UpdatedAtUtc = DateTime.UtcNow;

            var settingsNode = string.IsNullOrWhiteSpace(tenant.Settings)
                ? new JsonObject()
                : (JsonNode.Parse(tenant.Settings) as JsonObject) ?? new JsonObject();

            settingsNode[TenantSettingsKeys.ShortName] = string.IsNullOrWhiteSpace(request.ShortName)
                ? null
                : request.ShortName.Trim();
            settingsNode[TenantSettingsKeys.Website] = string.IsNullOrWhiteSpace(request.Website)
                ? null
                : request.Website.Trim();
            settingsNode[TenantSettingsKeys.BrandPrimaryColor] = primary;
            settingsNode[TenantSettingsKeys.BrandSecondaryColor] = secondary;
            settingsNode[TenantSettingsKeys.BrandAccentColor] = accent;
            settingsNode[TenantSettingsKeys.CardPrimaryColor] = cardPrimary;
            settingsNode[TenantSettingsKeys.CardShowGymLogo] = request.ShowGymLogoOnCard ?? true;
            if (request.GymMaxCapacity.HasValue)
                settingsNode[TenantSettingsKeys.GymMaxCapacity] = request.GymMaxCapacity.Value;
            else
                settingsNode[TenantSettingsKeys.GymMaxCapacity] = null;

            tenant.Settings = settingsNode.ToJsonString();
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation(
                "Tenant settings updated: {TenantId} ({GymName})",
                tenantId, tenant.Name);

            return Result<TenantSettingsDto>.Success(MapSettingsDto(tenant));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating tenant settings for {TenantId}", tenantId);
            return Result<TenantSettingsDto>.Failure(
                "Failed to update tenant settings / فشل في تحديث إعدادات المنظمة",
                ex.Message);
        }
    }

    /// <inheritdoc/>
    public async Task<Result<TenantBrandingDto>> GetBrandingAsync(Guid tenantId)
    {
        try
        {
            var tenant = await _dbContext.Tenants.AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == tenantId);
            if (tenant == null)
                return Result<TenantBrandingDto>.Failure("Tenant not found / المنظمة غير موجودة");

            var full = MapSettingsDto(tenant);
            return Result<TenantBrandingDto>.Success(new TenantBrandingDto
            {
                GymName = full.GymName,
                GymNameAr = full.GymNameAr,
                ShortName = full.ShortName,
                LogoUrl = full.LogoUrl,
                PrimaryColor = full.PrimaryColor,
                SecondaryColor = full.SecondaryColor,
                AccentColor = full.AccentColor,
                CardPrimaryColor = full.CardPrimaryColor,
                ShowGymLogoOnCard = full.ShowGymLogoOnCard
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving branding for {TenantId}", tenantId);
            return Result<TenantBrandingDto>.Failure(
                "Failed to retrieve branding / فشل في جلب الهوية", ex.Message);
        }
    }

    /// <inheritdoc/>
    public async Task<Result<QuickActionsSettingsDto>> GetQuickActionsAsync(Guid tenantId)
    {
        try
        {
            var tenant = await _dbContext.Tenants.AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == tenantId);
            if (tenant == null)
                return Result<QuickActionsSettingsDto>.Failure("Tenant not found / المنظمة غير موجودة");

            return Result<QuickActionsSettingsDto>.Success(ReadQuickActions(tenant.Settings));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving quick actions for {TenantId}", tenantId);
            return Result<QuickActionsSettingsDto>.Failure(
                "Failed to retrieve quick actions / فشل في جلب الاختصارات", ex.Message);
        }
    }

    /// <inheritdoc/>
    public async Task<Result<QuickActionsSettingsDto>> UpdateQuickActionsAsync(
        Guid tenantId, UpdateQuickActionsRequest request)
    {
        try
        {
            var tenant = await _dbContext.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId);
            if (tenant == null)
                return Result<QuickActionsSettingsDto>.Failure("Tenant not found / المنظمة غير موجودة");

            var (tooMany, keys) = QuickActionKeys.Normalize(request.Keys);
            if (tooMany)
                return Result<QuickActionsSettingsDto>.Failure(
                    QuickActionKeys.ValidationError,
                    $"At most {QuickActionKeys.MaxTiles} quick actions / الحد الأقصى {QuickActionKeys.MaxTiles} اختصارات");

            var before = ReadQuickActions(tenant.Settings);

            var settingsNode = string.IsNullOrWhiteSpace(tenant.Settings)
                ? new JsonObject()
                : (JsonNode.Parse(tenant.Settings) as JsonObject) ?? new JsonObject();

            var keysArr = new JsonArray();
            foreach (var k in keys)
                keysArr.Add(k);
            settingsNode[TenantSettingsKeys.QuickActions] = new JsonObject
            {
                ["keys"] = keysArr
            };

            tenant.Settings = settingsNode.ToJsonString();
            tenant.UpdatedAtUtc = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();

            var after = ReadQuickActions(tenant.Settings);
            await _auditService.LogAsync(
                "settings.quick_actions.update",
                "Tenant",
                tenantId,
                new { keys = before.Keys },
                new { keys = after.Keys });

            _logger.LogInformation("Quick actions updated for tenant {TenantId}", tenantId);
            return Result<QuickActionsSettingsDto>.Success(after);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating quick actions for {TenantId}", tenantId);
            return Result<QuickActionsSettingsDto>.Failure(
                "Failed to update quick actions / فشل في تحديث الاختصارات", ex.Message);
        }
    }

    /// <inheritdoc/>
    public async Task<Result<TenantSettingsDto>> SetLogoUrlAsync(Guid tenantId, string? logoUrl)
    {
        try
        {
            var tenant = await _dbContext.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId);
            if (tenant == null)
                return Result<TenantSettingsDto>.Failure("Tenant not found / المنظمة غير موجودة");

            tenant.LogoUrl = string.IsNullOrWhiteSpace(logoUrl) ? string.Empty : logoUrl.Trim();
            tenant.UpdatedAtUtc = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();
            return Result<TenantSettingsDto>.Success(MapSettingsDto(tenant));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting logo for {TenantId}", tenantId);
            return Result<TenantSettingsDto>.Failure(
                "Failed to set logo / فشل في حفظ الشعار", ex.Message);
        }
    }

    /// <inheritdoc/>
    public async Task<Result<TenantSettingsDto>> ClearLogoAsync(Guid tenantId)
        => await SetLogoUrlAsync(tenantId, null);

    private static TenantSettingsDto MapSettingsDto(Core.Entities.Tenant tenant)
    {
        var primary = AccessCardHtmlBuilder.NormalizeHex(
                          GetSettingString(tenant.Settings, TenantSettingsKeys.BrandPrimaryColor))
                      ?? BrandingDefaults.PrimaryColor;
        var secondary = AccessCardHtmlBuilder.NormalizeHex(
                            GetSettingString(tenant.Settings, TenantSettingsKeys.BrandSecondaryColor))
                        ?? BrandingDefaults.SecondaryColor;
        var accent = AccessCardHtmlBuilder.NormalizeHex(
                         GetSettingString(tenant.Settings, TenantSettingsKeys.BrandAccentColor))
                     ?? BrandingDefaults.AccentColor;
        var cardPrimary = AccessCardHtmlBuilder.NormalizeHex(
                              GetSettingString(tenant.Settings, TenantSettingsKeys.CardPrimaryColor))
                          ?? primary;

        return new TenantSettingsDto
        {
            TenantId = tenant.Id,
            GymName = tenant.Name,
            GymNameAr = tenant.NameAr,
            GymCode = tenant.GymCode,
            ShortName = GetSettingString(tenant.Settings, TenantSettingsKeys.ShortName),
            LogoUrl = string.IsNullOrWhiteSpace(tenant.LogoUrl) ? null : tenant.LogoUrl,
            PhoneNumber = string.IsNullOrWhiteSpace(tenant.PhoneNumber) ? null : tenant.PhoneNumber,
            Email = string.IsNullOrWhiteSpace(tenant.Email) ? null : tenant.Email,
            Website = GetSettingString(tenant.Settings, TenantSettingsKeys.Website),
            Address = string.IsNullOrWhiteSpace(tenant.Address) ? null : tenant.Address,
            PrimaryColor = primary,
            SecondaryColor = secondary,
            AccentColor = accent,
            CardPrimaryColor = cardPrimary,
            ShowGymLogoOnCard = GetSettingBool(tenant.Settings, TenantSettingsKeys.CardShowGymLogo, true),
            GymMaxCapacity = GymOccupancyService.ReadMaxCapacity(tenant.Settings),
            IsActive = tenant.IsActive,
            CreatedAtUtc = tenant.CreatedAtUtc,
            UpdatedAtUtc = tenant.UpdatedAtUtc
        };
    }

    /// <inheritdoc/>
    public async Task<Result<string>> GetGymCodeAsync(Guid tenantId)
    {
        try
        {
            var tenant = await _dbContext.Tenants
                .FirstOrDefaultAsync(t => t.Id == tenantId);

            if (tenant == null)
                return Result<string>.Failure(
                    "Tenant not found / المنظمة غير موجودة");

            return Result<string>.Success(tenant.GymCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving gym code for {TenantId}", tenantId);
            return Result<string>.Failure(
                "Failed to retrieve gym code / فشل في جلب رمز الصالة",
                ex.Message);
        }
    }

    /// <inheritdoc/>
    public async Task<Result<string>> GetQRPosterUrlAsync(Guid tenantId)
    {
        try
        {
            var tenant = await _dbContext.Tenants
                .FirstOrDefaultAsync(t => t.Id == tenantId);

            if (tenant == null)
                return Result<string>.Failure(
                    "Tenant not found / المنظمة غير موجودة");

            // QR poster URL format: /qr-posters/{gym_code}.pdf
            var qrPosterUrl = $"/qr-posters/{tenant.GymCode}.pdf";

            return Result<string>.Success(qrPosterUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving QR poster URL for {TenantId}", tenantId);
            return Result<string>.Failure(
                "Failed to retrieve QR poster URL / فشل في جلب رابط ملصق QR",
                ex.Message);
        }
    }

    /// <inheritdoc/>
    public async Task<Result<TaxSettingsDto>> GetTaxSettingsAsync(Guid tenantId)
    {
        try
        {
            var tenant = await _dbContext.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId);
            if (tenant == null)
                return Result<TaxSettingsDto>.Failure("Tenant not found / المنظمة غير موجودة");

            return Result<TaxSettingsDto>.Success(ReadTaxSettings(tenant.Settings));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving tax settings for {TenantId}", tenantId);
            return Result<TaxSettingsDto>.Failure(
                "Failed to retrieve tax settings / فشل في جلب الإعدادات الضريبية", ex.Message);
        }
    }

    /// <inheritdoc/>
    public async Task<Result<TaxSettingsDto>> UpdateTaxSettingsAsync(Guid tenantId, UpdateTaxSettingsRequest request)
    {
        try
        {
            var tenant = await _dbContext.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId);
            if (tenant == null)
                return Result<TaxSettingsDto>.Failure("Tenant not found / المنظمة غير موجودة");

            var before = ReadTaxSettings(tenant.Settings);

            // Merge into the existing Settings JSON rather than overwrite it wholesale, so other
            // keys (require_paper_waiver, allow_downgrades, etc.) are preserved.
            var settingsNode = string.IsNullOrWhiteSpace(tenant.Settings)
                ? new JsonObject()
                : (JsonNode.Parse(tenant.Settings) as JsonObject) ?? new JsonObject();

            settingsNode[TenantSettingsKeys.VatEnabled] = request.VatEnabled;
            settingsNode[TenantSettingsKeys.VatRate] = request.VatRate;
            settingsNode[TenantSettingsKeys.TaxRegistrationNumber] = request.TaxRegistrationNumber;
            settingsNode[TenantSettingsKeys.InvoiceFooterText] = request.InvoiceFooterText;
            settingsNode[TenantSettingsKeys.InvoiceFooterTextAr] = request.InvoiceFooterTextAr;

            tenant.Settings = settingsNode.ToJsonString();
            tenant.UpdatedAtUtc = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync();

            var after = ReadTaxSettings(tenant.Settings);
            await _auditService.LogAsync("tenant.tax_settings.update", "Tenant", tenantId, before, after);

            _logger.LogInformation("Tax settings updated for tenant {TenantId}", tenantId);

            return Result<TaxSettingsDto>.Success(after);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating tax settings for {TenantId}", tenantId);
            return Result<TaxSettingsDto>.Failure(
                "Failed to update tax settings / فشل في تحديث الإعدادات الضريبية", ex.Message);
        }
    }

    /// <inheritdoc/>
    public async Task<Result<InventoryAlertSettingsDto>> GetInventoryAlertSettingsAsync(Guid tenantId)
    {
        try
        {
            var tenant = await _dbContext.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId);
            if (tenant == null)
                return Result<InventoryAlertSettingsDto>.Failure("Tenant not found / المنظمة غير موجودة");

            return Result<InventoryAlertSettingsDto>.Success(ReadInventoryAlertSettings(tenant.Settings));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving inventory alert settings for {TenantId}", tenantId);
            return Result<InventoryAlertSettingsDto>.Failure(
                "Failed to retrieve inventory alert settings / فشل في جلب إعدادات تنبيهات المخزون", ex.Message);
        }
    }

    /// <inheritdoc/>
    public async Task<Result<InventoryAlertSettingsDto>> UpdateInventoryAlertSettingsAsync(
        Guid tenantId, UpdateInventoryAlertSettingsRequest request)
    {
        try
        {
            var tenant = await _dbContext.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId);
            if (tenant == null)
                return Result<InventoryAlertSettingsDto>.Failure("Tenant not found / المنظمة غير موجودة");

            var roles = (request.LowStockNotifyRoles ?? new List<string>())
                .Select(r => (r ?? string.Empty).Trim())
                .Where(r => r.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (roles.Count == 0)
                roles = new List<string> { "Owner", "Manager" };

            var windows = (request.ExpiryWindowsDays ?? new List<int>())
                .Where(d => d > 0)
                .Distinct()
                .OrderByDescending(d => d)
                .ToList();
            if (windows.Count == 0)
                windows = new List<int> { 90, 30, 7 };

            var before = ReadInventoryAlertSettings(tenant.Settings);

            var settingsNode = string.IsNullOrWhiteSpace(tenant.Settings)
                ? new JsonObject()
                : (JsonNode.Parse(tenant.Settings) as JsonObject) ?? new JsonObject();

            var rolesArr = new JsonArray();
            foreach (var r in roles) rolesArr.Add(r);
            var windowsArr = new JsonArray();
            foreach (var d in windows) windowsArr.Add(d);
            settingsNode[TenantSettingsKeys.InventoryLowStockNotifyRoles] = rolesArr;
            settingsNode[TenantSettingsKeys.InventoryExpiryWindowsDays] = windowsArr;

            tenant.Settings = settingsNode.ToJsonString();
            tenant.UpdatedAtUtc = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();

            var after = ReadInventoryAlertSettings(tenant.Settings);
            await _auditService.LogAsync("tenant.inventory_alert_settings.update", "Tenant", tenantId, before, after);
            _logger.LogInformation("Inventory alert settings updated for tenant {TenantId}", tenantId);
            return Result<InventoryAlertSettingsDto>.Success(after);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating inventory alert settings for {TenantId}", tenantId);
            return Result<InventoryAlertSettingsDto>.Failure(
                "Failed to update inventory alert settings / فشل في تحديث إعدادات تنبيهات المخزون", ex.Message);
        }
    }

    private static InventoryAlertSettingsDto ReadInventoryAlertSettings(string? settingsJson)
    {
        var roles = GetSettingStringArray(settingsJson, TenantSettingsKeys.InventoryLowStockNotifyRoles);
        var windows = GetSettingIntArray(settingsJson, TenantSettingsKeys.InventoryExpiryWindowsDays);
        return new InventoryAlertSettingsDto
        {
            LowStockNotifyRoles = roles is { Length: > 0 }
                ? roles.ToList()
                : new List<string> { "Owner", "Manager" },
            ExpiryWindowsDays = windows is { Length: > 0 }
                ? windows.ToList()
                : new List<int> { 90, 30, 7 }
        };
    }

    private static string[]? GetSettingStringArray(string? settingsJson, string key)
    {
        if (string.IsNullOrWhiteSpace(settingsJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(settingsJson);
            if (!doc.RootElement.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.Array)
                return null;
            return value.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString()!)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToArray();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static int[]? GetSettingIntArray(string? settingsJson, string key)
    {
        if (string.IsNullOrWhiteSpace(settingsJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(settingsJson);
            if (!doc.RootElement.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.Array)
                return null;
            return value.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.Number)
                .Select(e => e.GetInt32())
                .ToArray();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static QuickActionsSettingsDto ReadQuickActions(string? settingsJson)
    {
        var fallback = new QuickActionsSettingsDto { Keys = QuickActionKeys.DefaultKeys.ToList() };
        if (string.IsNullOrWhiteSpace(settingsJson))
            return fallback;

        try
        {
            using var doc = JsonDocument.Parse(settingsJson);
            if (!doc.RootElement.TryGetProperty(TenantSettingsKeys.QuickActions, out var qa)
                || qa.ValueKind == JsonValueKind.Null)
                return fallback;
            if (qa.ValueKind != JsonValueKind.Object)
                return fallback;
            if (!qa.TryGetProperty("keys", out var keysEl) || keysEl.ValueKind != JsonValueKind.Array)
                return fallback;

            var incoming = keysEl.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString()!)
                .ToList();
            var (_, keys) = QuickActionKeys.Normalize(incoming);
            // Stored empty array is intentional (owner cleared shortcuts).
            return new QuickActionsSettingsDto { Keys = keys };
        }
        catch (JsonException)
        {
            return fallback;
        }
    }

    private static TaxSettingsDto ReadTaxSettings(string? settingsJson) => new()
    {
        VatEnabled = GetSettingBool(settingsJson, TenantSettingsKeys.VatEnabled, false),
        VatRate = GetSettingDecimal(settingsJson, TenantSettingsKeys.VatRate, 0.14m),
        TaxRegistrationNumber = GetSettingString(settingsJson, TenantSettingsKeys.TaxRegistrationNumber),
        InvoiceFooterText = GetSettingString(settingsJson, TenantSettingsKeys.InvoiceFooterText),
        InvoiceFooterTextAr = GetSettingString(settingsJson, TenantSettingsKeys.InvoiceFooterTextAr)
    };

    private static bool GetSettingBool(string? settingsJson, string key, bool defaultValue)
    {
        if (string.IsNullOrWhiteSpace(settingsJson))
            return defaultValue;

        try
        {
            using var doc = JsonDocument.Parse(settingsJson);
            return doc.RootElement.TryGetProperty(key, out var value) &&
                   value.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? value.GetBoolean()
                : defaultValue;
        }
        catch (JsonException)
        {
            return defaultValue;
        }
    }

    private static decimal GetSettingDecimal(string? settingsJson, string key, decimal defaultValue)
    {
        if (string.IsNullOrWhiteSpace(settingsJson))
            return defaultValue;

        try
        {
            using var doc = JsonDocument.Parse(settingsJson);
            return doc.RootElement.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number
                ? value.GetDecimal()
                : defaultValue;
        }
        catch (JsonException)
        {
            return defaultValue;
        }
    }

    private static string? GetSettingString(string? settingsJson, string key)
    {
        if (string.IsNullOrWhiteSpace(settingsJson))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(settingsJson);
            return doc.RootElement.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
