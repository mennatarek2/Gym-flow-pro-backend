namespace GMS.Application.DTOs.Members;

/// <summary>
/// Full member detail DTO with current membership and recent attendance.
/// </summary>
public class MemberDetailDto
{
    public Guid Id { get; set; }
    public string MemberNumber { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string FullNameAr { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public DateOnly DateOfBirth { get; set; }
    public string? ProfilePhotoUrl { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; }
    public int InvitationQuotaRemaining { get; set; }
    public string? ReferralCode { get; set; }
    public int SuccessfulReferralCount { get; set; }
    public string? ReferralTier { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>Member App claim status (never includes activation plaintext).</summary>
    public MemberAppAccessStatusDto MemberApp { get; set; } = new();

    // Current membership
    public MembershipSummaryDto? CurrentMembership { get; set; }

    // Recent attendance (last 5)
    public List<AttendanceSummaryDto> RecentAttendance { get; set; } = new();
}

public class MembershipSummaryDto
{
    public Guid Id { get; set; }
    public string PlanName { get; set; } = string.Empty;
    public string PlanNameAr { get; set; } = string.Empty;
    public string PlanType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public int? SessionsRemaining { get; set; }
    public DateOnly? FrozenFromDate { get; set; }
    public DateOnly? FrozenUntilDate { get; set; }
    public decimal AmountPaid { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
}

public class AttendanceSummaryDto
{
    public Guid Id { get; set; }
    public DateTime CheckInAtUtc { get; set; }
    public DateTime? CheckOutAtUtc { get; set; }
    public string EntryMethod { get; set; } = string.Empty;
}
