namespace GMS.Application.DTOs.Sales;

/// <summary>Inline member creation as part of a sale (walk-in customer).</summary>
public class NewMemberRequest
{
    public string FullName { get; set; } = string.Empty;
    public string? FullNameAr { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>
    /// Optional — a walk-in sale shouldn't be blocked on collecting a birthdate. When omitted,
    /// GymMember.DateOfBirth (a required column) is set to a placeholder; front-desk can fill in
    /// the real value later via the member profile.
    /// </summary>
    public DateOnly? DateOfBirth { get; set; }

    /// <summary>Optional — forwarded into member create when present on the sale.</summary>
    public string? ReferralCode { get; set; }

    public Guid? ReferringMemberId { get; set; }
}
