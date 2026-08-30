namespace GMS.Api.Services;

using Microsoft.AspNetCore.SignalR;
using GMS.Api.Hubs;
using GMS.Core.Interfaces;

public class SignalRStaffNotificationNotifier : IStaffNotificationRealtimeNotifier
{
    private readonly IHubContext<AttendanceHub> _hub;
    private readonly ILogger<SignalRStaffNotificationNotifier> _logger;

    public SignalRStaffNotificationNotifier(
        IHubContext<AttendanceHub> hub,
        ILogger<SignalRStaffNotificationNotifier> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    public async Task NotifyCreatedAsync(Guid tenantId, IReadOnlyList<Guid> recipientAppUserIds, CancellationToken ct = default)
    {
        try
        {
            await _hub.Clients.Group($"tenant-{tenantId}").SendAsync(
                "StaffNotificationCreated",
                new { recipientAppUserIds, atUtc = DateTime.UtcNow },
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "StaffNotificationCreated SignalR push failed for tenant {TenantId}", tenantId);
        }
    }
}
