namespace GMS.Application.DTOs.Activities;

public class SessionDto
{
    public Guid Id { get; set; }
    public Guid ActivityId { get; set; }
    public string ActivityName { get; set; } = "";
    public string ActivityKind { get; set; } = "";
    public DateTime StartsAtUtc { get; set; }
    public DateTime EndsAtUtc { get; set; }
    public int Capacity { get; set; }
    public int BookedCount { get; set; }
    public int CheckedInCount { get; set; }
    /// <summary>Seats still available (Capacity − BookedCount, floored at 0).</summary>
    public int RemainingCapacity { get; set; }
    public string Status { get; set; } = "";
    public Guid? CoachUserId { get; set; }
    public string? CoachName { get; set; }
}

public class SessionDetailDto : SessionDto
{
    public List<BookingDto> Bookings { get; set; } = new();
}

public class BookingDto
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public Guid? MemberId { get; set; }
    public string MemberName { get; set; } = "";
    public string? MemberPhone { get; set; }
    public string? GuestName { get; set; }
    public string? GuestPhone { get; set; }
    public string Status { get; set; } = "";
    public string Source { get; set; } = "";
    public DateTime? CheckedInAtUtc { get; set; }
}

public class CreateBookingRequest
{
    public Guid SessionId { get; set; }
    public Guid? MemberId { get; set; }
    public string? GuestName { get; set; }
    public string? GuestPhone { get; set; }
    public Guid? CoveringMembershipId { get; set; }
    /// <summary>Drop-in path: the paid sale (LineType 'drop_in') backing this booking.</summary>
    public Guid? SaleId { get; set; }
    public string? Source { get; set; }
}
