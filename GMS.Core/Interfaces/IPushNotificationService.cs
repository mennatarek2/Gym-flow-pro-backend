namespace GMS.Core.Interfaces;

/// <summary>
/// Push notification service (Firebase Cloud Messaging).
/// </summary>
public interface IPushNotificationService
{
    /// <summary>
    /// Sends a push notification to a specific device.
    /// </summary>
    Task SendToDeviceAsync(string fcmToken, string title, string body);

    /// <summary>
    /// Sends a push notification to a topic (all subscribers).
    /// Topic per tenant: "gym-{tenantId}"
    /// </summary>
    Task SendToTopicAsync(string topic, string title, string body);
}
