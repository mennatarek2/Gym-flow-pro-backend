namespace GMS.Api.Services;

using Microsoft.AspNetCore.SignalR;
using GMS.Api.Hubs;
using GMS.Core.Interfaces;

/// <summary>
/// SignalR implementation of ICheckinNotifier.
/// Pushes real-time events to tenant-isolated groups.
/// </summary>
public class SignalRCheckinNotifier : ICheckinNotifier
{
    private readonly IHubContext<AttendanceHub> _hubContext;
    private readonly ILogger<SignalRCheckinNotifier> _logger;

    public SignalRCheckinNotifier(
        IHubContext<AttendanceHub> hubContext,
        ILogger<SignalRCheckinNotifier> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task NotifyCheckinAsync(Guid tenantId, Guid memberId, string memberName,
        string memberNumber, DateTime checkInTime, string entryMethod)
    {
        try
        {
            await _hubContext.Clients
                .Group($"tenant-{tenantId}")
                .SendAsync("MemberCheckedIn", new
                {
                    memberId,
                    memberName,
                    memberNumber,
                    checkInTime,
                    entryMethod
                });

            _logger.LogDebug(
                "SignalR: MemberCheckedIn pushed to tenant-{TenantId} for {MemberNumber}",
                tenantId, memberNumber);
        }
        catch (Exception ex)
        {
            // Non-critical — don't fail check-in if SignalR push fails
            _logger.LogWarning(ex, "SignalR push failed for tenant-{TenantId}", tenantId);
        }
    }
}
