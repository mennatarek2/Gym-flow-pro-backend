namespace GMS.Application.DTOs.Members;

/// <summary>
/// Request DTO for updating a member. All fields optional — only provided fields are updated.
/// </summary>
public class UpdateMemberRequest
{
    public string? FullName { get; set; }
    public string? FullNameAr { get; set; }
    public string? Phone { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public string? NationalId { get; set; }
    public string? EmergencyContact { get; set; }
    public string? Email { get; set; }
    public string? Notes { get; set; }
}
