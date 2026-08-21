namespace GMS.Api.Hubs;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

/// <summary>
/// SignalR hub for real-time attendance dashboard.
/// 
/// Tenant-isolated groups: each gym gets its own group ($"tenant-{tenantId}").
/// Events from GYM-CAIRO-01 never reach GYM-GIZA-02 clients.
/// 
/// Redis backplane ensures events propagate across multiple App Service instances.
/// 
/// Client connection: wss://{host}/hubs/attendance
/// </summary>
[Authorize]
public class AttendanceHub : Hub
{
    private readonly ILogger<AttendanceHub> _logger;

    public AttendanceHub(ILogger<AttendanceHub> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// On connect, extract tenant_id from JWT claim and add to tenant group.
    /// </summary>
    public override async Task OnConnectedAsync()
    {
        var tenantId = Context.User?.FindFirst("tenant_id")?.Value;

        if (!string.IsNullOrEmpty(tenantId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"tenant-{tenantId}");
            _logger.LogInformation(
                "SignalR client connected: {ConnectionId} joined tenant-{TenantId}",
                Context.ConnectionId, tenantId);
        }
        else
        {
            _logger.LogWarning("SignalR client connected without tenant_id claim: {ConnectionId}", Context.ConnectionId);
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var tenantId = Context.User?.FindFirst("tenant_id")?.Value;

        if (!string.IsNullOrEmpty(tenantId))
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"tenant-{tenantId}");
            _logger.LogInformation(
                "SignalR client disconnected: {ConnectionId} left tenant-{TenantId}",
                Context.ConnectionId, tenantId);
        }

        await base.OnDisconnectedAsync(exception);
    }
}
