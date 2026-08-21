namespace GMS.Application.Jobs;

using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using GMS.Application.Interfaces;
using GMS.Infrastructure.Persistence;

/// <summary>
/// One-off Hangfire job enqueued by IInvoiceService.EnqueueForSale: creates the invoice for a
/// sale, then enqueues the (currently stubbed, P7) delivery job for it.
///
/// Lives in GMS.Application (not GMS.Infrastructure/Jobs, where the recurring jobs live) because
/// it depends on IInvoiceService/IInvoiceDeliveryJob — GMS.Infrastructure does not reference
/// GMS.Application (the dependency direction in this codebase runs the other way).
/// </summary>
public class CreateInvoiceForSaleJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CreateInvoiceForSaleJob> _logger;

    public CreateInvoiceForSaleJob(IServiceScopeFactory scopeFactory, ILogger<CreateInvoiceForSaleJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    [AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 60, 300, 900 })]
    public async Task ExecuteAsync(Guid saleId)
    {
        using var scope = _scopeFactory.CreateScope();
        var invoiceService = scope.ServiceProvider.GetRequiredService<IInvoiceService>();
        var dbContext = scope.ServiceProvider.GetRequiredService<GymFlowProDbContext>();

        await invoiceService.CreateForSaleAsync(saleId);

        var invoiceId = await dbContext.Invoices
            .IgnoreQueryFilters()
            .Where(i => i.SaleId == saleId && i.Type == "invoice")
            .Select(i => (Guid?)i.Id)
            .FirstOrDefaultAsync();

        if (invoiceId.HasValue)
        {
            BackgroundJob.Enqueue<IInvoiceDeliveryJob>(job => job.Execute(invoiceId.Value));
        }
        else
        {
            _logger.LogWarning(
                "CreateInvoiceForSaleJob: no invoice found for sale {SaleId} after CreateForSaleAsync", saleId);
        }
    }
}
