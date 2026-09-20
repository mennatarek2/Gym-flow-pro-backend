namespace GMS.Application.Options;

/// <summary>
/// Biometric attendance foundation options.
/// <see cref="ProvisionalMaxClockSkewMinutes"/> is a PROVISIONAL default for implementation safety —
/// Product Owner must approve the production threshold.
/// </summary>
public class BiometricAttendanceOptions
{
    public const string SectionName = "BiometricAttendance";

    /// <summary>
    /// Provisional max absolute skew between device timestamp and server receipt time.
    /// Events beyond this are stored and marked NeedsReview — not auto-applied to payroll-impacting attendance.
    /// Default 15 minutes until Product Owner approval.
    /// </summary>
    public int ProvisionalMaxClockSkewMinutes { get; set; } = 15;

    /// <summary>Reject device timestamps more than this many days in the future relative to receipt.</summary>
    public int MaxFutureSkewDays { get; set; } = 1;

    /// <summary>Reject / review device timestamps older than this many days (delayed backlog still accepted into raw store, but auto-apply is blocked beyond this).</summary>
    public int MaxPastSkewDays { get; set; } = 14;
}
