namespace GMS.Api.Hosting;

using GMS.Application.Interfaces;
using GMS.Core.Interfaces;

/// <summary>
/// Local Edition only. Best-effort periodic check-in so Platform receives gym identity without
/// blocking gym requests. Failure never stops the desk — the gym keeps using its cached license.
/// </summary>
public sealed class LocalLicenseHeartbeatService : BackgroundService
{
    public static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<LocalLicenseHeartbeatService> _logger;

    public LocalLicenseHeartbeatService(IServiceScopeFactory scopes, ILogger<LocalLicenseHeartbeatService> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PulseAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogInformation(ex, "Local license heartbeat skipped — gym continues offline.");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task PulseAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopes.CreateScope();
        var setup = scope.ServiceProvider.GetRequiredService<ILocalFirstRunService>();
        var license = scope.ServiceProvider.GetRequiredService<ILocalLicenseClientService>();
        var status = await setup.GetStatusAsync(cancellationToken);
        await license.TryRevalidateAsync(status.GymCode, status.GymName, cancellationToken);
    }
}
