namespace GMS.Application.Jobs;

using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using GMS.Application.Interfaces;

/// <summary>
/// One-off Hangfire job enqueued by IImportService.EnqueueExecuteAsync: processes a
/// 'dry_run_ready' import batch's 'ok' rows in chunks of 500, creating a GymMember + Membership
/// per row, then sets the batch to 'completed'.
/// </summary>
public class ExecuteImportJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ExecuteImportJob> _logger;

    public ExecuteImportJob(IServiceScopeFactory scopeFactory, ILogger<ExecuteImportJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    [AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 60, 300, 900 })]
    public async Task ExecuteAsync(Guid batchId, Guid tenantId)
    {
        using var scope = _scopeFactory.CreateScope();
        var importService = scope.ServiceProvider.GetRequiredService<IImportService>();

        await importService.ExecuteAsync(batchId, tenantId);

        _logger.LogInformation("ExecuteImportJob: batch {BatchId} executed", batchId);
    }
}
