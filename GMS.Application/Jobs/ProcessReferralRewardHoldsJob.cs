namespace GMS.Application.Jobs;

using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using GMS.Application.Interfaces;

/// <summary>Grants due referral_rewards pending_hold rows (credit / free_days).</summary>
public class ProcessReferralRewardHoldsJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ProcessReferralRewardHoldsJob> _logger;

    public ProcessReferralRewardHoldsJob(
        IServiceScopeFactory scopeFactory,
        ILogger<ProcessReferralRewardHoldsJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    [AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 60, 300, 900 })]
    public async Task ExecuteAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var rewards = scope.ServiceProvider.GetRequiredService<IReferralRewardService>();
        var count = await rewards.ProcessDueHoldsAsync();
        _logger.LogInformation("ProcessReferralRewardHoldsJob: granted {Count} reward(s)", count);
    }
}
