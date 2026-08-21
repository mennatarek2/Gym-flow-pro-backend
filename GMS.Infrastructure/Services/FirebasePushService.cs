namespace GMS.Infrastructure.Services;

using Microsoft.Extensions.Logging;
using GMS.Core.Interfaces;

/// <summary>
/// Firebase Cloud Messaging push notification service.
/// 
/// For now, this is a mock/development implementation.
/// Production requires:
///   1. Install NuGet: FirebaseAdmin
///   2. Download service account JSON from Firebase Console
///   3. Set GOOGLE_APPLICATION_CREDENTIALS env var
///   4. Replace mock methods with real FirebaseMessaging.DefaultInstance calls
/// 
/// Topic naming: "gym-{tenantId}" for tenant-wide broadcasts.
/// </summary>
public class FirebasePushService : IPushNotificationService
{
    private readonly ILogger<FirebasePushService> _logger;

    public FirebasePushService(ILogger<FirebasePushService> logger)
    {
        _logger = logger;
    }

    public Task SendToDeviceAsync(string fcmToken, string title, string body)
    {
        _logger.LogInformation(
            "[FCM] Push to device {Token}: {Title} — {Body}",
            fcmToken[..Math.Min(20, fcmToken.Length)] + "...", title, body);

        // TODO: Production implementation:
        // var message = new FirebaseAdmin.Messaging.Message
        // {
        //     Token = fcmToken,
        //     Notification = new Notification { Title = title, Body = body }
        // };
        // await FirebaseMessaging.DefaultInstance.SendAsync(message);

        return Task.CompletedTask;
    }

    public Task SendToTopicAsync(string topic, string title, string body)
    {
        _logger.LogInformation(
            "[FCM] Push to topic {Topic}: {Title} — {Body}",
            topic, title, body);

        // TODO: Production implementation:
        // var message = new FirebaseAdmin.Messaging.Message
        // {
        //     Topic = topic,
        //     Notification = new Notification { Title = title, Body = body }
        // };
        // await FirebaseMessaging.DefaultInstance.SendAsync(message);

        return Task.CompletedTask;
    }
}
