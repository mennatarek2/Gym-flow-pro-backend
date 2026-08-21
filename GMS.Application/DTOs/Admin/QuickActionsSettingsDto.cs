namespace GMS.Application.DTOs.Admin;

/// <summary>Gym-wide dashboard Quick Actions. One ordered list per tenant.</summary>
public class QuickActionsSettingsDto
{
    public List<string> Keys { get; set; } = new();
}

public class UpdateQuickActionsRequest
{
    public List<string>? Keys { get; set; }
}
