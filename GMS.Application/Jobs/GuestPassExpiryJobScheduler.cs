namespace GMS.Application.Jobs;

using Hangfire;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

/// <summary>
/// Registers guest_pass expiry — Application-layer job (IInvitationService), same pattern as Z-Report.
/// </summary>
public class GuestPassExpiryJobScheduler : IHostedService
{
    private const string CairoTimeZone = "Egypt Standard Time";
    private readonly ILogger<GuestPassExpiryJobScheduler> _logger;

    public GuestPassExpiryJobScheduler(ILogger<GuestPassExpiryJobScheduler> logger)
    {
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var cairoTz = TimeZoneInfo.FindSystemTimeZoneById(CairoTimeZone);

        // Daily 00:15 Cairo — after MembershipStatusExpiryJob (00:05)
        RecurringJob.AddOrUpdate<GuestPassExpiryJob>(
            "guest-pass-expiry",
            job => job.ExecuteAsync(),
            "15 0 * * *",
            new RecurringJobOptions { TimeZone = cairoTz });

        _logger.LogInformation(
            "GuestPassExpiryJobScheduler: guest-pass-expiry registered (00:15 Cairo).");

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
