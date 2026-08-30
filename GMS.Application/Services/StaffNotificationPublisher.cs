namespace GMS.Application.Services;

using Microsoft.Extensions.Logging;
using GMS.Application.DTOs.Notifications;
using GMS.Application.Interfaces;

public class StaffNotificationPublisher : IStaffNotificationPublisher
{
    private readonly INotificationService _notifications;
    private readonly ILogger<StaffNotificationPublisher> _logger;

    public StaffNotificationPublisher(INotificationService notifications, ILogger<StaffNotificationPublisher> logger)
    {
        _notifications = notifications;
        _logger = logger;
    }

    public async Task TryPublishAsync(Guid tenantId, CreateStaffNotificationRequest request)
    {
        try
        {
            var result = await _notifications.PublishStaffAsync(tenantId, request);
            if (!result.IsSuccess)
                _logger.LogWarning("Staff notification publish failed: {Error}", result.Error);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Staff notification publish threw for type {Type}", request.Type);
        }
    }
}
