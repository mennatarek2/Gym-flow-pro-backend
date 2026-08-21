namespace GMS.Application.Jobs;

using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using GMS.Application.Interfaces;
using GMS.Core.Entities;
using GMS.Core.Interfaces;
using GMS.Infrastructure.Persistence;

/// <summary>
/// Runs nightly at 23:59 Cairo time (registered by <see cref="ZReportJobScheduler"/>, not
/// GMS.Infrastructure.Jobs.JobScheduler — this job depends on IZReportService, an Application-layer
/// interface, and GMS.Infrastructure does not reference GMS.Application; see CreateInvoiceForSaleJob
/// for the same precedent). For each active tenant: builds/stores the Z-Report, renders + uploads
/// its PDF on first generation, WhatsApps it to all Owners, then flags (but never auto-closes) any
/// shift that's been open for more than 8 hours to Managers/Owners.
/// </summary>
public class ZReportGenerationJob
{
    private static readonly TimeZoneInfo CairoTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time");

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ZReportGenerationJob> _logger;

    public ZReportGenerationJob(IServiceScopeFactory scopeFactory, ILogger<ZReportGenerationJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    [AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 60, 300, 900 })]
    public async Task ExecuteAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<GymFlowProDbContext>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        var zReportService = scope.ServiceProvider.GetRequiredService<IZReportService>();
        var fileStorageService = scope.ServiceProvider.GetRequiredService<IFileStorageService>();
        var whatsAppService = scope.ServiceProvider.GetRequiredService<IWhatsAppService>();

        var cairoNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, CairoTimeZone);
        var reportDate = DateOnly.FromDateTime(cairoNow);
        var openShiftCutoffUtc = DateTime.UtcNow.AddHours(-8);

        // Tenant entity has no global query filter (see GymFlowProDbContext), so this is safe
        // before any tenant context has been set.
        var tenants = await dbContext.Tenants
            .Where(t => t.IsActive && !t.IsDeleted)
            .ToListAsync();

        _logger.LogInformation(
            "ZReportGenerationJob: generating Z-Reports for {Count} tenants, date {ReportDate}",
            tenants.Count, reportDate);

        foreach (var tenant in tenants)
        {
            try
            {
                // Sale/SaleLine/Shift/ZReport all carry a combined tenant+soft-delete query filter
                // keyed off the ambient ITenantContext — must match the tenantId passed into
                // IZReportService.BuildAsync or the filters silently AND into an empty result set.
                tenantContext.SetTenant(tenant.Id, tenant.Name, tenant.TimeZone);

                await BuildAndDeliverZReportAsync(
                    dbContext, zReportService, fileStorageService, whatsAppService, tenant, reportDate);

                await FlagLongOpenShiftsAsync(dbContext, whatsAppService, tenant, openShiftCutoffUtc);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ZReportGenerationJob: failed processing tenant {TenantId}", tenant.Id);
            }
        }

        _logger.LogInformation("ZReportGenerationJob: completed for {Count} tenants.", tenants.Count);
    }

    private async Task BuildAndDeliverZReportAsync(
        GymFlowProDbContext dbContext, IZReportService zReportService,
        IFileStorageService fileStorageService, IWhatsAppService whatsAppService,
        Tenant tenant, DateOnly reportDate)
    {
        var buildResult = await zReportService.BuildAsync(tenant.Id, reportDate);
        if (!buildResult.IsSuccess || buildResult.Data == null)
        {
            _logger.LogError(
                "ZReportGenerationJob: failed to build Z-Report for tenant {TenantId}: {Error}",
                tenant.Id, buildResult.Error);
            return;
        }

        var dto = buildResult.Data;

        if (string.IsNullOrWhiteSpace(dto.PdfUrl))
        {
            var pdfResult = await zReportService.GetPdfBytesAsync(tenant.Id, reportDate);
            if (!pdfResult.IsSuccess || pdfResult.Data == null)
            {
                _logger.LogError(
                    "ZReportGenerationJob: failed to render Z-Report PDF for tenant {TenantId}: {Error}",
                    tenant.Id, pdfResult.Error);
                return;
            }

            await using var stream = new MemoryStream(pdfResult.Data);
            var folder = $"z-reports/{tenant.Id}/{reportDate:yyyy}";
            var url = await fileStorageService.UploadAsync(stream, $"z-report-{reportDate:yyyy-MM-dd}.pdf", folder);

            var zReportEntity = await dbContext.ZReports.FirstOrDefaultAsync(z => z.Id == dto.Id);
            if (zReportEntity != null)
            {
                zReportEntity.PdfUrl = url;
                zReportEntity.UpdatedAtUtc = DateTime.UtcNow;
                await dbContext.SaveChangesAsync();
                dto.PdfUrl = url;
            }
        }

        if (string.IsNullOrWhiteSpace(dto.PdfUrl))
            return;

        var owners = await dbContext.AppUsers
            .Where(u => u.TenantId == tenant.Id && u.Role == "Owner" && u.IsActive)
            .ToListAsync();

        foreach (var owner in owners)
        {
            if (string.IsNullOrWhiteSpace(owner.PhoneNumber))
                continue;

            await whatsAppService.SendDocumentAsync(
                owner.PhoneNumber,
                $"{owner.FirstName} {owner.LastName}",
                dto.PdfUrl,
                $"Z-Report for {reportDate:yyyy-MM-dd}",
                $"تقرير الإقفال ليوم {reportDate:yyyy-MM-dd}");
        }
    }

    /// <summary>Alerts Managers/Owners about shifts open more than 8 hours. Never auto-closes them.</summary>
    private async Task FlagLongOpenShiftsAsync(
        GymFlowProDbContext dbContext, IWhatsAppService whatsAppService, Tenant tenant, DateTime cutoffUtc)
    {
        var longOpenShifts = await dbContext.Shifts
            .Include(s => s.User)
            .Where(s => s.TenantId == tenant.Id && s.Status == "open" && s.OpenedAt < cutoffUtc)
            .ToListAsync();

        if (longOpenShifts.Count == 0)
            return;

        var alertRecipients = await dbContext.AppUsers
            .Where(u => u.TenantId == tenant.Id && u.IsActive && (u.Role == "Manager" || u.Role == "Owner"))
            .ToListAsync();

        foreach (var shift in longOpenShifts)
        {
            var hoursOpen = (int)(DateTime.UtcNow - shift.OpenedAt).TotalHours;
            var staffName = shift.User != null ? $"{shift.User.FirstName} {shift.User.LastName}" : "Unknown";

            foreach (var recipient in alertRecipients)
            {
                if (string.IsNullOrWhiteSpace(recipient.PhoneNumber))
                    continue;

                await whatsAppService.SendTemplateAsync(recipient.PhoneNumber, "shift_open_warning", new Dictionary<string, string>
                {
                    ["staff_name"] = staffName,
                    ["opened_at"] = shift.OpenedAt.ToString("yyyy-MM-dd HH:mm"),
                    ["hours_open"] = hoursOpen.ToString()
                });
            }
        }

        _logger.LogWarning(
            "ZReportGenerationJob: {Count} shift(s) open >8h for tenant {TenantId} — flagged, NOT auto-closed",
            longOpenShifts.Count, tenant.Id);
    }
}
