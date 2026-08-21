namespace GMS.Application.DTOs.Admin;

/// <summary>INVS-10 inventory alert keys in Tenant.Settings JSON.</summary>
public class InventoryAlertSettingsDto
{
    /// <summary>Staff roles notified by Hangfire low-stock job. Default Owner, Manager.</summary>
    public List<string> LowStockNotifyRoles { get; set; } = new() { "Owner", "Manager" };

    /// <summary>Expiry alert windows in days. Default 90, 30, 7.</summary>
    public List<int> ExpiryWindowsDays { get; set; } = new() { 90, 30, 7 };
}

public class UpdateInventoryAlertSettingsRequest
{
    public List<string> LowStockNotifyRoles { get; set; } = new();
    public List<int> ExpiryWindowsDays { get; set; } = new();
}
