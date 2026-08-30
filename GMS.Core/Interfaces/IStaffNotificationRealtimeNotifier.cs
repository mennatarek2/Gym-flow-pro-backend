namespace GMS.Core.Interfaces;

/// <summary>Best-effort real-time push when staff notifications are created.</summary>
public interface IStaffNotificationRealtimeNotifier
{
    Task NotifyCreatedAsync(Guid tenantId, IReadOnlyList<Guid> recipientAppUserIds, CancellationToken ct = default);
}
