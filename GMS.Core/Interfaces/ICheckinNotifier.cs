namespace GMS.Core.Interfaces;

/// <summary>
/// Abstraction for real-time check-in notifications.
/// Implemented via SignalR in the API layer to maintain Clean Architecture.
/// </summary>
public interface ICheckinNotifier
{
    /// <summary>
    /// Pushes a "MemberCheckedIn" event to the tenant's SignalR group.
    /// </summary>
    Task NotifyCheckinAsync(Guid tenantId, Guid memberId, string memberName,
        string memberNumber, DateTime checkInTime, string entryMethod);
}
