namespace GMS.Core.Enums;

/// <summary>
/// Reasons for manual check-in (when staff checks in a member).
/// Manual check-in is a first-class feature, not a fallback.
/// </summary>
public enum ManualCheckinReason
{
    /// <summary>Member's phone is dead/out of battery.</summary>
    DeadPhone = 1,

    /// <summary>Member hasn't installed the app yet.</summary>
    NoAppYet = 2,

    /// <summary>App is experiencing technical issues.</summary>
    AppIssue = 3,

    /// <summary>Other reason (notes should be provided).</summary>
    Other = 4
}
