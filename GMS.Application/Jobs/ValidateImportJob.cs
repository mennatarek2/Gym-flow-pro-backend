namespace GMS.Application.Jobs;

using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using GMS.Application.Interfaces;

/// <summary>
/// One-off Hangfire job enqueued by IImportService.UploadAsync/SetMappingAsync/CreateMissingPlansAsync:
/// re-maps and validates every row of an import batch, then sets it to 'dry_run_ready'.
/// </summary>
public class ValidateImportJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ValidateImportJob> _logger;

    public ValidateImportJob(IServiceScopeFactory scopeFactory, ILogger<ValidateImportJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    [AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 60, 300, 900 })]
    public async Task ExecuteAsync(Guid batchId, Guid tenantId)
    {
        using var scope = _scopeFactory.CreateScope();
        var importService = scope.ServiceProvider.GetRequiredService<IImportService>();

        await importService.ValidateAsync(batchId, tenantId);

        _logger.LogInformation("ValidateImportJob: batch {BatchId} validated", batchId);
    }
}
