namespace GMS.Application.DTOs.Activities;

/// <summary>Read-only Member App view of an upcoming class session (list row).</summary>
public class MemberClassListItemDto
{
    /// <summary>Activity session id — the schedulable class instance.</summary>
    public Guid Id { get; set; }
    public Guid ActivityId { get; set; }
    public string Name { get; set; } = "";
    public string NameAr { get; set; } = "";
    public string Description { get; set; } = "";
    public string? TrainerName { get; set; }
    public Guid? TrainerId { get; set; }

    /// <summary>Cairo calendar date for the session start.</summary>
    public DateOnly Date { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public int DurationMinutes { get; set; }

    /// <summary>UTC instants for clients that prefer absolute timestamps.</summary>
    public DateTime StartsAtUtc { get; set; }
    public DateTime EndsAtUtc { get; set; }

    /// <summary>Drop-in fee when the class is paid; null when included in membership only.</summary>
    public decimal? Price { get; set; }

    public int Capacity { get; set; }
    public int BookedCount { get; set; }
    public int AvailableSeats { get; set; }

    /// <summary>ActivitySession status: upcoming | completed | cancelled</summary>
    public string Status { get; set; } = "";
}

/// <summary>Read-only Member App class session detail.</summary>
public class MemberClassDetailsDto
{
    public Guid Id { get; set; }
    public Guid ActivityId { get; set; }
    public string Name { get; set; } = "";
    public string NameAr { get; set; } = "";
    public string Description { get; set; } = "";
    public string DescriptionAr { get; set; } = "";

    public MemberClassScheduleDto Schedule { get; set; } = new();
    public MemberClassTrainerDto? Trainer { get; set; }
    public decimal? Price { get; set; }
    public MemberClassAvailabilityDto Availability { get; set; } = new();

    /// <summary>ActivitySession status: upcoming | completed | cancelled</summary>
    public string Status { get; set; } = "";
    public bool BookingRequired { get; set; }
}

public class MemberClassScheduleDto
{
    public DateOnly Date { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public int DurationMinutes { get; set; }
    public DateTime StartsAtUtc { get; set; }
    public DateTime EndsAtUtc { get; set; }
}

public class MemberClassTrainerDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
}

public class MemberClassAvailabilityDto
{
    public int Capacity { get; set; }
    public int BookedCount { get; set; }
    public int AvailableSeats { get; set; }
}
