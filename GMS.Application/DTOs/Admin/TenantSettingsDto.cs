namespace GMS.Application.DTOs.Admin;

/// <summary>
/// Request/Response DTO for tenant settings (Gym Identity Phase A).
/// </summary>
public class TenantSettingsDto
{
    public Guid TenantId { get; set; }
    public string GymName { get; set; } = string.Empty;
    public string GymNameAr { get; set; } = string.Empty;
    public string GymCode { get; set; } = string.Empty;
    public string? ShortName { get; set; }
    public string? LogoUrl { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Email { get; set; }
    public string? Website { get; set; }
    public string? Address { get; set; }
    public string PrimaryColor { get; set; } = BrandingDefaults.PrimaryColor;
    public string SecondaryColor { get; set; } = BrandingDefaults.SecondaryColor;
    public string AccentColor { get; set; } = BrandingDefaults.AccentColor;
    public string CardPrimaryColor { get; set; } = BrandingDefaults.PrimaryColor;
    public bool ShowGymLogoOnCard { get; set; } = true;
    /// <summary>Null when the gym has not set a maximum inside count.</summary>
    public int? GymMaxCapacity { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
}

/// <summary>Staff-readable branding snapshot (no Owner required).</summary>
public class TenantBrandingDto
{
    public string GymName { get; set; } = string.Empty;
    public string GymNameAr { get; set; } = string.Empty;
    public string? ShortName { get; set; }
    public string? LogoUrl { get; set; }
    public string PrimaryColor { get; set; } = BrandingDefaults.PrimaryColor;
    public string SecondaryColor { get; set; } = BrandingDefaults.SecondaryColor;
    public string AccentColor { get; set; } = BrandingDefaults.AccentColor;
    public string CardPrimaryColor { get; set; } = BrandingDefaults.PrimaryColor;
    public bool ShowGymLogoOnCard { get; set; } = true;
}

public static class BrandingDefaults
{
    public const string PrimaryColor = "#7ACC00";
    public const string SecondaryColor = "#148F8F";
    public const string AccentColor = "#A0E040";
}
