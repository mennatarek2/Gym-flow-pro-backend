namespace GMS.Application.Services;

using GMS.Core.Interfaces;

/// <summary>No-op realtime notifier used when SignalR is unavailable (tests / jobs).</summary>
public sealed class NullStaffNotificationRealtimeNotifier : IStaffNotificationRealtimeNotifier
{
    public Task NotifyCreatedAsync(Guid tenantId, IReadOnlyList<Guid> recipientAppUserIds, CancellationToken ct = default)
        => Task.CompletedTask;
}
