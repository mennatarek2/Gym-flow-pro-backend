namespace GMS.Core.Constants;

/// <summary>
/// Lifecycle statuses for <see cref="Entities.ActivityBooking"/>. Stored as lowercase strings.
/// Quota consumption rule: booked, checked_in, cancelled_late and no_show consume a class credit;
/// cancelled (>= late-cancel window before start) refunds it.
/// </summary>
public static class ActivityBookingStatuses
{
    public const string Booked = "booked";
    public const string CheckedIn = "checked_in";
    /// <summary>Cancelled at least the configured hours before session start — quota refunded.</summary>
    public const string Cancelled = "cancelled";
    /// <summary>Cancelled inside the late-cancellation window — quota remains consumed.</summary>
    public const string CancelledLate = "cancelled_late";
    /// <summary>Session ended without check-in or cancellation — quota remains consumed.</summary>
    public const string NoShow = "no_show";

    /// <summary>Statuses that occupy a seat (capacity).</summary>
    public static readonly IReadOnlyCollection<string> SeatOccupying =
        new[] { Booked, CheckedIn };

    /// <summary>Statuses that consume the plan's class-quota credit.</summary>
    public static readonly IReadOnlyCollection<string> QuotaConsuming =
        new[] { Booked, CheckedIn, CancelledLate, NoShow };

    public static bool IsSeatOccupying(string? status) => status is Booked or CheckedIn;
    public static bool IsQuotaConsuming(string? status) =>
        status is Booked or CheckedIn or CancelledLate or NoShow;
}

/// <summary>Lifecycle statuses for <see cref="Entities.ActivitySession"/>.</summary>
public static class ActivitySessionStatuses
{
    public const string Upcoming = "upcoming";
    public const string Completed = "completed";
    public const string Cancelled = "cancelled";
}
