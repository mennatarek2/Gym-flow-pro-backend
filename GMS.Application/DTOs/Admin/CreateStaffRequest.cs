namespace GMS.Application.DTOs.Admin;

using System.Text.Json.Serialization;
using GMS.Application.Serialization;

/// <summary>
/// Request DTO for creating a new staff user.
/// Role is case-insensitive input; persisted as canonical Identity names:
/// Manager, Trainer, or Receptionist (not Owner, not Member).
/// </summary>
public class CreateStaffRequest
{
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Role { get; set; } = "Trainer";
    public string? PhoneNumber { get; set; }
    public string? JobTitle { get; set; }
    public string? Department { get; set; }
    [JsonConverter(typeof(NullableDateOnlyJsonConverter))]
    public DateOnly? HireDate { get; set; }
    public string? Notes { get; set; }
}
