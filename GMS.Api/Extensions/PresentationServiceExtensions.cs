namespace GMS.Api.Extensions;

using Microsoft.Extensions.DependencyInjection;
using GMS.Core.Interfaces;
using GMS.Api.Services;

/// <summary>
/// Extension methods for registering Presentation layer services.
/// </summary>
public static class PresentationServiceExtensions
{
    public static IServiceCollection AddPresentation(this IServiceCollection services)
    {
        // SignalR real-time notifier for check-in events
        services.AddScoped<ICheckinNotifier, SignalRCheckinNotifier>();
        services.AddScoped<IMemberOrderNotifier, SignalRMemberOrderNotifier>();

        return services;
    }
}

