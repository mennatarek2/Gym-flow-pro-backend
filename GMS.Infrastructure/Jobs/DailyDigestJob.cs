namespace GMS.Infrastructure.Jobs;

using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using GMS.Core.Interfaces;
using GMS.Infrastructure.Persistence;

/// <summary>
/// Runs daily at 9:00 AM Cairo time. Per active tenant, sends a WhatsApp digest to Owner/Manager
/// staff summarizing today's/this week's expiring memberships and the outstanding debtor total —
/// skipped entirely for a tenant with nothing to report.
/// </summary>
public class DailyDigestJob
{
    private static readonly TimeZoneInfo CairoTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time");

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DailyDigestJob> _logger;

    public DailyDigestJob(IServiceScopeFactory scopeFactory, ILogger<DailyDigestJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    [AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 60, 300, 900 })]
    public async Task ExecuteAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<GymFlowProDbContext>();
        var whatsAppService = scope.ServiceProvider.GetRequiredService<IWhatsAppService>();

        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, CairoTimeZone));
        var weekEnd = today.AddDays(7);

        var tenants = await dbContext.Tenants.IgnoreQueryFilters().Where(t => t.IsActive).ToListAsync();
        var sentCount = 0;

        foreach (var tenant in tenants)
        {
            var expiringToday = await dbContext.Memberships
                .IgnoreQueryFilters()
                .CountAsync(m => m.TenantId == tenant.Id && !m.IsDeleted
                               && m.Status == "active" && m.EndDate == today);

            var expiringWeek = await dbContext.Memberships
                .IgnoreQueryFilters()
                .CountAsync(m => m.TenantId == tenant.Id && !m.IsDeleted
                               && m.Status == "active" && m.EndDate > today && m.EndDate <= weekEnd);

            var debtors = await dbContext.Sales
                .IgnoreQueryFilters()
                .Where(s => s.TenantId == tenant.Id && !s.IsDeleted && s.Status == "partially_paid" && s.AmountDue > 0)
                .GroupBy(s => s.MemberId)
                .Select(g => g.Sum(s => s.AmountDue))
                .ToListAsync();

            var debtorCount = debtors.Count;
            var totalDue = debtors.Sum();

            var hasExpiring = expiringToday > 0 || expiringWeek > 0;
            var hasDebtors = debtorCount > 0;

            if (!hasExpiring && !hasDebtors)
                continue;

            var recipients = await dbContext.AppUsers
                .IgnoreQueryFilters()
                .Where(u => u.TenantId == tenant.Id && !u.IsDeleted && u.IsActive
                         && (u.Role == "Owner" || u.Role == "Manager"))
                .ToListAsync();

            var parameters = new Dictionary<string, string>
            {
                ["expiringToday"] = expiringToday.ToString(),
                ["expiringWeek"] = expiringWeek.ToString(),
                ["debtorCount"] = debtorCount.ToString(),
                ["totalDue"] = totalDue.ToString("F2")
            };

            foreach (var recipient in recipients)
            {
                if (string.IsNullOrWhiteSpace(recipient.PhoneNumber))
                    continue;

                await whatsAppService.SendTemplateAsync(recipient.PhoneNumber, "daily_digest", parameters);
                sentCount++;
            }
        }

        _logger.LogInformation("DailyDigestJob: sent {Count} digest message(s) across {TenantCount} tenant(s)",
            sentCount, tenants.Count);
    }
}
