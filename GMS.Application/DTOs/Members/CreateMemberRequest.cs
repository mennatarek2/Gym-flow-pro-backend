namespace GMS.Application.DTOs.Members;

/// <summary>
/// Request DTO for creating a new member.
/// </summary>
public class CreateMemberRequest
{
    public string FullName { get; set; } = string.Empty;
    public string FullNameAr { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public DateOnly DateOfBirth { get; set; }
    public string? NationalId { get; set; }
    public string? EmergencyContact { get; set; }
    public string? Email { get; set; }
    public string? Notes { get; set; }

    /// <summary>Optional inviter share code (INV-3). Mutually alternate with <see cref="ReferringMemberId"/>.</summary>
    public string? ReferralCode { get; set; }

    /// <summary>Optional inviter GymMember.Id when staff picks the member directly.</summary>
    public Guid? ReferringMemberId { get; set; }
}
