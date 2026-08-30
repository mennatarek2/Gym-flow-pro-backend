namespace GMS.Application.Interfaces;

using GMS.Application.DTOs.Notifications;

/// <summary>Best-effort staff notification publish — never throws into business flows.</summary>
public interface IStaffNotificationPublisher
{
    Task TryPublishAsync(Guid tenantId, CreateStaffNotificationRequest request);
}
