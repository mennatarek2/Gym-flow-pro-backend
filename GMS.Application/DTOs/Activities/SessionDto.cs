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
    public string Status { get; set; } = "";
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
    public Guid MemberId { get; set; }
    public string MemberName { get; set; } = "";
    public string? MemberPhone { get; set; }
    public string Status { get; set; } = "";
    public string Source { get; set; } = "";
    public DateTime? CheckedInAtUtc { get; set; }
}

public class CreateBookingRequest
{
    public Guid SessionId { get; set; }
    public Guid MemberId { get; set; }
    public Guid? CoveringMembershipId { get; set; }
    public string? Source { get; set; }
}
