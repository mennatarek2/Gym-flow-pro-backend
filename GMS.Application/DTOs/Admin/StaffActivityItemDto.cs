namespace GMS.Application.DTOs.Admin;

public class StaffActivityItemDto
{
    public Guid Id { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string? EntityType { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public bool AboutThisStaff { get; set; }
}
