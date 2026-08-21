namespace GMS.Api.Services;

using Microsoft.AspNetCore.SignalR;
using GMS.Api.Hubs;
using GMS.Core.Interfaces;

/// <summary>Pushes member-store order events to the tenant attendance hub group.</summary>
public class SignalRMemberOrderNotifier : IMemberOrderNotifier
{
    private readonly IHubContext<AttendanceHub> _hubContext;
    private readonly ILogger<SignalRMemberOrderNotifier> _logger;

    public SignalRMemberOrderNotifier(
        IHubContext<AttendanceHub> hubContext,
        ILogger<SignalRMemberOrderNotifier> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task NotifyCreatedAsync(
        Guid tenantId, Guid orderId, string orderNumber, Guid memberId, string memberName, CancellationToken ct = default)
    {
        try
        {
            await _hubContext.Clients
                .Group($"tenant-{tenantId}")
                .SendAsync("MemberOrderCreated", new
                {
                    orderId,
                    orderNumber,
                    memberId,
                    memberName,
                    status = "pending"
                }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SignalR MemberOrderCreated failed for tenant-{TenantId}", tenantId);
        }
    }

    public async Task NotifyStatusChangedAsync(
        Guid tenantId,
        Guid orderId,
        string orderNumber,
        string status,
        Guid memberId,
        CancellationToken ct = default)
    {
        try
        {
            await _hubContext.Clients
                .Group($"tenant-{tenantId}")
                .SendAsync("MemberOrderStatusChanged", new
                {
                    orderId,
                    orderNumber,
                    memberId,
                    status
                }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SignalR MemberOrderStatusChanged failed for tenant-{TenantId}", tenantId);
        }
    }
}
