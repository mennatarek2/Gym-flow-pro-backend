namespace GMS.Core.Exceptions;

/// <summary>
/// Thrown when inserting a GymAttendance row would violate the one-gym-floor-check-in-per-member-
/// per-Cairo-day unique index — i.e. a concurrent duplicate check-in raced past the application-level
/// "already checked in today" query and lost at the database. Callers should treat this exactly like
/// the ordinary "already checked in" failure, not a 500.
/// </summary>
public class DuplicateCheckinException : DomainException
{
    public DuplicateCheckinException(string message) : base(message)
    {
    }

    public DuplicateCheckinException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
