namespace GMS.Application.Services;

using GMS.Application.Common;
using GMS.Core.Constants;

/// <summary>
/// Deterministic, DB-free attendance math. Local (Cairo) wall-clock shift times are converted to UTC
/// using the same "Egypt Standard Time" convention every other Cairo-day calculation in this codebase
/// uses (see <see cref="GMS.Core.Utilities.MembershipOperational"/>) — not a per-tenant timezone, since
/// nothing else in the codebase reads one either.
/// </summary>
public static class AttendanceCalculator
{
    private static readonly TimeZoneInfo CairoTimeZone =
        TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time");

    /// <summary>Converts a local Cairo wall-clock time on the given date to UTC.</summary>
    public static DateTime ComputeShiftStartUtc(DateOnly date, TimeOnly start)
    {
        var local = DateTime.SpecifyKind(date.ToDateTime(start), DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(local, CairoTimeZone);
    }

    /// <summary>
    /// Converts the shift's local end time to UTC, rolling to the next calendar day when
    /// <paramref name="end"/> &lt;= <paramref name="start"/> (the shift crosses midnight).
    /// </summary>
    public static DateTime ComputeShiftEndUtc(DateOnly date, TimeOnly start, TimeOnly end)
    {
        var endDate = end <= start ? date.AddDays(1) : date;
        var local = DateTime.SpecifyKind(endDate.ToDateTime(end), DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(local, CairoTimeZone);
    }

    /// <summary>
    /// Late-minutes + status at check-in. No assigned shift means lateness can't be judged, so it's
    /// always on-time in that case.
    /// </summary>
    public static (int LateMinutes, string Status) ComputeCheckIn(
        DateTime checkInAtUtc, DateOnly attendanceDate, TimeOnly? shiftStart, int graceMinutes)
    {
        if (shiftStart == null)
            return (0, AttendanceStatuses.Present);

        var shiftStartUtc = ComputeShiftStartUtc(attendanceDate, shiftStart.Value);
        var graceDeadline = shiftStartUtc.AddMinutes(graceMinutes);
        var lateMinutes = (int)Math.Max(0, Math.Round((checkInAtUtc - graceDeadline).TotalMinutes, MidpointRounding.AwayFromZero));

        return lateMinutes > 0
            ? (lateMinutes, AttendanceStatuses.Late)
            : (0, AttendanceStatuses.Present);
    }

    /// <summary>
    /// Worked/overtime minutes at check-out. Overtime is only computable against an assigned shift.
    /// Fails if checkout is not strictly after check-in.
    /// </summary>
    public static Result<(int WorkedMinutes, int OvertimeMinutes)> ComputeCheckOut(
        DateTime checkInAtUtc, DateTime checkOutAtUtc, DateOnly attendanceDate,
        TimeOnly? shiftStart, TimeOnly? shiftEnd)
    {
        if (checkOutAtUtc <= checkInAtUtc)
            return Result<(int, int)>.Failure("Check-out must be after check-in / وقت الانصراف يجب أن يكون بعد وقت الحضور");

        var workedMinutes = (int)Math.Round((checkOutAtUtc - checkInAtUtc).TotalMinutes, MidpointRounding.AwayFromZero);

        var overtimeMinutes = 0;
        if (shiftStart != null && shiftEnd != null)
        {
            var shiftEndUtc = ComputeShiftEndUtc(attendanceDate, shiftStart.Value, shiftEnd.Value);
            overtimeMinutes = (int)Math.Max(0, Math.Round((checkOutAtUtc - shiftEndUtc).TotalMinutes, MidpointRounding.AwayFromZero));
        }

        return Result<(int, int)>.Success((workedMinutes, overtimeMinutes));
    }
}
