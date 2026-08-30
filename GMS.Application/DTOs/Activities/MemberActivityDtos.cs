namespace GMS.Application.DTOs.Activities;

/// <summary>Member App view of an activity (class or facility) with live eligibility.</summary>
public class MemberActivityDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string NameAr { get; set; } = "";
    public string Description { get; set; } = "";
    public string DescriptionAr { get; set; } = "";
    /// <summary>'class' | 'facility'</summary>
    public string Kind { get; set; } = "";
    public bool BookingRequired { get; set; }
    public decimal? DropInPrice { get; set; }

    /// <summary>'included' | 'limited' | 'unlimited' | 'drop_in' | 'not_entitled'</summary>
    public string Eligibility { get; set; } = "not_entitled";
    /// <summary>Remaining bookings in the current quota period when eligibility = 'limited'; null otherwise.</summary>
    public int? QuotaRemaining { get; set; }
    public int? QuotaLimit { get; set; }
}

public class MemberSessionDto : SessionDto
{
    /// <summary>true when the calling member holds an active booking on this session.</summary>
    public Guid MyBooking { get; set; }
    public bool HasMyBooking => MyBooking != Guid.Empty;
    public string? MyBookingStatus { get; set; }

    /// <summary>Whether the member can book right now (eligibility + capacity + not past).</summary>
    public bool CanBook { get; set; }
    /// <summary>Machine-readable reason when CanBook is false: full | already_booked | not_entitled | past | cancelled_session.</summary>
    public string? CannotBookReason { get; set; }
}

public class MemberBookingDto
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public string Status { get; set; } = "";
    /// <summary>true when this booking still consumes a class credit (late cancel / no-show / checked-in).</summary>
    public bool QuotaConsumed { get; set; }

    // Session snapshot for display
    public Guid ActivityId { get; set; }
    public string ActivityName { get; set; } = "";
    public string ActivityKind { get; set; } = "";
    public DateTime StartsAtUtc { get; set; }
    public DateTime EndsAtUtc { get; set; }
    public string? CoachName { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public DateTime? CheckedInAtUtc { get; set; }

    /// <summary>Remaining class credits after this operation, when the covering entitlement is limited-mode.</summary>
    public int? QuotaRemaining { get; set; }
}

public class MemberCancelPolicyDto
{
    /// <summary>Cancellation is free (quota refunded) until this UTC instant; after it → cancelled_late.</summary>
    public DateTime RefundDeadlineUtc { get; set; }
    /// <summary>Configured late-cancellation window in hours (default 2).</summary>
    public int LateCancellationHours { get; set; }
    public bool IsLate { get; set; }
}

public class MemberDropInRequest
{
    /// <summary>Payment gateway reference from the existing payment architecture ('cash' at reception, gateway ref online).</summary>
    public string Gateway { get; set; } = "cash";
    public string? ExternalRef { get; set; }
}
