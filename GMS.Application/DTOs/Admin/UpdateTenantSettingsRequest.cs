namespace GMS.Application.DTOs.Admin;

/// <summary>
/// Request DTO for updating tenant settings (Gym Identity Phase A).
/// </summary>
public class UpdateTenantSettingsRequest
{
    public string GymName { get; set; } = string.Empty;
    public string GymNameAr { get; set; } = string.Empty;
    public string? ShortName { get; set; }
    public string? LogoUrl { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Email { get; set; }
    public string? Website { get; set; }
    public string? Address { get; set; }
    public string? PrimaryColor { get; set; }
    public string? SecondaryColor { get; set; }
    public string? AccentColor { get; set; }
    public string? CardPrimaryColor { get; set; }
    public bool? ShowGymLogoOnCard { get; set; }
    /// <summary>1–9999, or null to leave capacity unconfigured.</summary>
    public int? GymMaxCapacity { get; set; }
}
