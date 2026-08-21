namespace GMS.Application.DTOs.Admin;

using System.Text.Json.Serialization;
using GMS.Application.Serialization;

/// <summary>
/// Request DTO for updating a staff user.
/// </summary>
public class UpdateStaffRequest
{
    public string FullName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty; // Manager | Trainer | Receptionist (canonical PascalCase on persist)
    public bool IsActive { get; set; }
    public string? PhoneNumber { get; set; }
    public string? JobTitle { get; set; }
    public string? Department { get; set; }
    [JsonConverter(typeof(NullableDateOnlyJsonConverter))]
    public DateOnly? HireDate { get; set; }
    public string? Notes { get; set; }
}
