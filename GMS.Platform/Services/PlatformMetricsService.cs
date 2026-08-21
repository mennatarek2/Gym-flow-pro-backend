namespace GMS.Platform.Services;

using System.Data;
using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using GMS.Platform.Constants;
using GMS.Platform.DTOs;
using GMS.Platform.Entities;
using GMS.Platform.Interfaces;
using GMS.Platform.Persistence;

/// <summary>
/// CP8 SaaS metrics. Snapshot-computed + short Redis cache (fail-open).
/// Annual prices normalize to monthly MRR via ÷12.
/// </summary>
public class PlatformMetricsService : IPlatformMetricsService
{
    private static readonly TimeZoneInfo CairoTz = TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time");
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);

    private readonly PlatformDbContext _db;
    private readonly IDistributedCache _cache;
    private readonly ILogger<PlatformMetricsService> _logger;

    public PlatformMetricsService(
        PlatformDbContext db,
        IDistributedCache cache,
        ILogger<PlatformMetricsService> logger)
    {
        _db = db;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>Normalize a subscription price to monthly recurring revenue (EGP).</summary>
    public static decimal ToMonthlyMrr(decimal priceEgp, string billingCycle)
    {
        if (string.Equals(billingCycle, BillingCycles.Annual, StringComparison.OrdinalIgnoreCase))
            return Math.Round(priceEgp / 12m, 2, MidpointRounding.AwayFromZero);
        return Math.Round(priceEgp, 2, MidpointRounding.AwayFromZero);
    }

    public async Task<MrrSnapshotDto> GetMrrAsync(DateOnly? asOf, CancellationToken cancellationToken = default)
    {
        var day = asOf ?? TodayCairo();
        return await GetOrComputeAsync(
            $"platform:metrics:mrr:{day:yyyy-MM-dd}",
            () => ComputeMrrAsync(day, cancellationToken),
            cancellationToken);
    }

    public async Task<MrrMovementDto> GetMovementAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        if (to < from)
            (from, to) = (to, from);

        return await GetOrComputeAsync(
            $"platform:metrics:movement:{from:yyyy-MM-dd}:{to:yyyy-MM-dd}",
            () => ComputeMovementAsync(from, to, cancellationToken),
            cancellationToken);
    }

    public async Task<ChurnMetricsDto> GetChurnAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        if (to < from)
            (from, to) = (to, from);

        return await GetOrComputeAsync(
            $"platform:metrics:churn:{from:yyyy-MM-dd}:{to:yyyy-MM-dd}",
            () => ComputeChurnAsync(from, to, cancellationToken),
            cancellationToken);
    }

    public async Task<ConversionMetricsDto> GetConversionAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        if (to < from)
            (from, to) = (to, from);

        return await GetOrComputeAsync(
            $"platform:metrics:conversion:{from:yyyy-MM-dd}:{to:yyyy-MM-dd}",
            () => ComputeConversionAsync(from, to, cancellationToken),
            cancellationToken);
    }

    public async Task<TierDistributionDto> GetTierDistributionAsync(DateOnly? asOf, CancellationToken cancellationToken = default)
    {
        var day = asOf ?? TodayCairo();
        return await GetOrComputeAsync(
            $"platform:metrics:tier-dist:{day:yyyy-MM-dd}",
            () => ComputeTierDistributionAsync(day, cancellationToken),
            cancellationToken);
    }

    private async Task<MrrSnapshotDto> ComputeMrrAsync(DateOnly asOf, CancellationToken ct)
    {
        var subs = await _db.Subscriptions.AsNoTracking().ToListAsync(ct);
        var paying = subs.Where(s => CountsTowardMrr(s, asOf)).ToList();
        var mrr = paying.Sum(s => ToMonthlyMrr(s.PriceEgp, s.BillingCycle));
        return new MrrSnapshotDto
        {
            AsOf = asOf,
            MrrEgp = mrr,
            ArrEgp = Math.Round(mrr * 12m, 2, MidpointRounding.AwayFromZero),
            PayingTenantCount = paying.Select(s => s.TenantId).Distinct().Count(),
            ComputedAtUtc = DateTime.UtcNow
        };
    }

    private async Task<MrrMovementDto> ComputeMovementAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        var startAsOf = from.AddDays(-1);
        var starting = await ComputeMrrAsync(startAsOf, ct);
        var endingDirect = await ComputeMrrAsync(to, ct);

        var subs = await _db.Subscriptions.AsNoTracking().ToListAsync(ct);
        var subById = subs.ToDictionary(s => s.Id);

        var fromUtc = StartUtc(from);
        var toUtcExclusive = StartUtc(to.AddDays(1));

        var changes = await _db.SubscriptionChanges.AsNoTracking()
            .Where(c => c.EffectiveAtUtc >= fromUtc && c.EffectiveAtUtc < toUtcExclusive)
            .OrderBy(c => c.EffectiveAtUtc)
            .ToListAsync(ct);

        decimal newMrr = 0, expansion = 0, contraction = 0, churned = 0;

        // New MRR: paying subscriptions first created in-window (not trials).
        foreach (var sub in subs)
        {
            var createdDay = DateOnly.FromDateTime(ToCairo(sub.CreatedAtUtc));
            if (createdDay < from || createdDay > to)
                continue;
            if (!IsPayingStatus(sub.Status))
                continue;
            newMrr += ToMonthlyMrr(sub.PriceEgp, sub.BillingCycle);
        }

        foreach (var change in changes)
        {
            if (!subById.TryGetValue(change.SubscriptionId, out var sub))
                continue;

            if (change.ChangeType == SubscriptionChangeTypes.Reactivation)
            {
                // Reactivation of a previously cancelled customer — expansion of logo count as new MRR.
                newMrr += ToMonthlyMrr(sub.PriceEgp, sub.BillingCycle);
                continue;
            }

            if (change.ChangeType == SubscriptionChangeTypes.Cancellation)
            {
                churned += ToMonthlyMrr(sub.PriceEgp, sub.BillingCycle);
                continue;
            }

            if (change.ChangeType is SubscriptionChangeTypes.Upgrade or SubscriptionChangeTypes.Downgrade
                && !string.IsNullOrWhiteSpace(change.FromTier) && !string.IsNullOrWhiteSpace(change.ToTier))
            {
                var fromMrr = PlatformListPrices.MonthlyEgp(change.FromTier!);
                var toMrr = string.Equals(change.ToTier, sub.PlanTier, StringComparison.OrdinalIgnoreCase)
                    ? ToMonthlyMrr(sub.PriceEgp, sub.BillingCycle)
                    : PlatformListPrices.MonthlyEgp(change.ToTier!);

                var delta = toMrr - fromMrr;
                if (delta > 0)
                    expansion += delta;
                else if (delta < 0)
                    contraction += Math.Abs(delta);
            }
        }

        var endingReconciled = starting.MrrEgp + newMrr + expansion - contraction - churned;
        endingReconciled = Math.Round(endingReconciled, 2, MidpointRounding.AwayFromZero);
        var reconciles = Math.Abs(endingReconciled - endingDirect.MrrEgp) <= 0.05m;

        return new MrrMovementDto
        {
            From = from,
            To = to,
            StartingMrrEgp = starting.MrrEgp,
            NewMrrEgp = Math.Round(newMrr, 2, MidpointRounding.AwayFromZero),
            ExpansionMrrEgp = Math.Round(expansion, 2, MidpointRounding.AwayFromZero),
            ContractionMrrEgp = Math.Round(contraction, 2, MidpointRounding.AwayFromZero),
            ChurnedMrrEgp = Math.Round(churned, 2, MidpointRounding.AwayFromZero),
            EndingMrrEgp = endingReconciled,
            EndingMrrDirectEgp = endingDirect.MrrEgp,
            Reconciles = reconciles,
            ComputedAtUtc = DateTime.UtcNow
        };
    }

    private async Task<ChurnMetricsDto> ComputeChurnAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        var startAsOf = from.AddDays(-1);
        var starting = await ComputeMrrAsync(startAsOf, ct);
        var movement = await ComputeMovementAsync(from, to, ct);

        var gross = starting.MrrEgp <= 0
            ? 0m
            : Math.Round(movement.ChurnedMrrEgp / starting.MrrEgp, 4, MidpointRounding.AwayFromZero);

        var fromUtc = StartUtc(from);
        var toUtcExclusive = StartUtc(to.AddDays(1));
        var churnedTenants = await _db.SubscriptionChanges.AsNoTracking()
            .Where(c => c.ChangeType == SubscriptionChangeTypes.Cancellation
                        && c.EffectiveAtUtc >= fromUtc
                        && c.EffectiveAtUtc < toUtcExclusive)
            .Select(c => c.TenantId)
            .Distinct()
            .CountAsync(ct);

        var cohorts = await BuildCohortsAsync(to, ct);

        return new ChurnMetricsDto
        {
            From = from,
            To = to,
            GrossChurnRate = gross,
            StartingMrrEgp = starting.MrrEgp,
            ChurnedMrrEgp = movement.ChurnedMrrEgp,
            StartingPayingTenants = starting.PayingTenantCount,
            ChurnedTenants = churnedTenants,
            Cohorts = cohorts,
            ComputedAtUtc = DateTime.UtcNow
        };
    }

    private async Task<ConversionMetricsDto> ComputeConversionAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        var fromUtc = StartUtc(from);
        var toUtcExclusive = StartUtc(to.AddDays(1));

        var trialStarts = await _db.SubscriptionChanges.AsNoTracking()
            .Where(c => c.ChangeType == SubscriptionChangeTypes.TrialStart
                        && c.EffectiveAtUtc >= fromUtc
                        && c.EffectiveAtUtc < toUtcExclusive)
            .Select(c => c.SubscriptionId)
            .Distinct()
            .ToListAsync(ct);

        var trialsStarted = trialStarts.Count;
        var converted = 0;
        if (trialStarts.Count > 0)
        {
            var subs = await _db.Subscriptions.AsNoTracking()
                .Where(s => trialStarts.Contains(s.Id))
                .ToListAsync(ct);

            // Converted = left trial into a billed state (active/past_due/suspended, or cancelled after trial end).
            converted = subs.Count(HasEvidenceOfPaid);
        }

        var rate = trialsStarted == 0
            ? 0m
            : Math.Round((decimal)converted / trialsStarted, 4, MidpointRounding.AwayFromZero);

        return new ConversionMetricsDto
        {
            From = from,
            To = to,
            TrialsStarted = trialsStarted,
            ConvertedToPaid = converted,
            ConversionRate = rate,
            ComputedAtUtc = DateTime.UtcNow
        };
    }

    private static bool HasEvidenceOfPaid(PlatformSubscription s) =>
        s.Status is SubscriptionStatuses.Active
            or SubscriptionStatuses.PastDue
            or SubscriptionStatuses.Suspended
        || (s.Status == SubscriptionStatuses.Cancelled
            && s.CancelledAtUtc.HasValue
            && (!s.TrialEndsAtUtc.HasValue || s.CancelledAtUtc > s.TrialEndsAtUtc));

    private async Task<TierDistributionDto> ComputeTierDistributionAsync(DateOnly asOf, CancellationToken ct)
    {
        var subs = await _db.Subscriptions.AsNoTracking().ToListAsync(ct);
        var paying = subs.Where(s => CountsTowardMrr(s, asOf)).ToList();

        var rows = paying
            .GroupBy(s => s.PlanTier.Trim().ToLowerInvariant())
            .OrderBy(g => PlanTiers.Rank(g.Key))
            .Select(g => new TierDistributionRowDto
            {
                PlanTier = g.Key,
                TenantCount = g.Select(x => x.TenantId).Distinct().Count(),
                MrrEgp = Math.Round(g.Sum(x => ToMonthlyMrr(x.PriceEgp, x.BillingCycle)), 2, MidpointRounding.AwayFromZero)
            })
            .ToList();

        return new TierDistributionDto
        {
            AsOf = asOf,
            Tiers = rows,
            TotalMrrEgp = rows.Sum(r => r.MrrEgp),
            TotalPayingTenants = paying.Select(s => s.TenantId).Distinct().Count(),
            ComputedAtUtc = DateTime.UtcNow
        };
    }

    private async Task<List<CohortRetentionDto>> BuildCohortsAsync(DateOnly asOf, CancellationToken ct)
    {
        var signups = await LoadTenantSignupsAsync(ct);
        var subs = await _db.Subscriptions.AsNoTracking().ToListAsync(ct);
        var payingTenantIds = subs.Where(s => CountsTowardMrr(s, asOf)).Select(s => s.TenantId).ToHashSet();

        return signups
            .GroupBy(s => s.CohortMonth)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var signedUp = g.Count();
                var retained = g.Count(x => payingTenantIds.Contains(x.TenantId));
                return new CohortRetentionDto
                {
                    CohortMonth = g.Key,
                    SignedUp = signedUp,
                    RetainedPaying = retained,
                    RetentionRate = signedUp == 0
                        ? 0m
                        : Math.Round((decimal)retained / signedUp, 4, MidpointRounding.AwayFromZero)
                };
            })
            .ToList();
    }

    /// <summary>
    /// Paying MRR membership as-of <paramref name="asOf"/> using current row + cancel/suspend timestamps.
    /// Trialing never contributes. Annual normalized by callers via <see cref="ToMonthlyMrr"/>.
    /// </summary>
    public static bool CountsTowardMrr(PlatformSubscription s, DateOnly asOf)
    {
        var created = DateOnly.FromDateTime(ToCairo(s.CreatedAtUtc));
        if (created > asOf)
            return false;

        if (s.Status == SubscriptionStatuses.Trialing)
            return false;

        if (s.CancelledAtUtc.HasValue)
        {
            var cancelledDay = DateOnly.FromDateTime(ToCairo(s.CancelledAtUtc.Value));
            if (cancelledDay <= asOf)
                return false;
        }

        if (s.Status == SubscriptionStatuses.Suspended && s.SuspendedAtUtc.HasValue)
        {
            var suspendedDay = DateOnly.FromDateTime(ToCairo(s.SuspendedAtUtc.Value));
            if (suspendedDay <= asOf)
                return false;
        }

        // Active / past_due, or cancelled/suspended after asOf (still paying as of asOf).
        return s.Status is SubscriptionStatuses.Active or SubscriptionStatuses.PastDue
            or SubscriptionStatuses.Cancelled or SubscriptionStatuses.Suspended;
    }

    private static bool IsPayingStatus(string status) =>
        status is SubscriptionStatuses.Active or SubscriptionStatuses.PastDue;

    private async Task<T> GetOrComputeAsync<T>(string cacheKey, Func<Task<T>> compute, CancellationToken ct)
    {
        try
        {
            var cached = await _cache.GetStringAsync(cacheKey, ct);
            if (!string.IsNullOrEmpty(cached))
            {
                var hit = JsonSerializer.Deserialize<T>(cached, JsonOptions);
                if (hit != null)
                    return hit;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Platform metrics cache read failed for {Key}", cacheKey);
        }

        var value = await compute();

        try
        {
            await _cache.SetStringAsync(
                cacheKey,
                JsonSerializer.Serialize(value, JsonOptions),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheTtl },
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Platform metrics cache write failed for {Key}", cacheKey);
        }

        return value;
    }

    private async Task<List<TenantSignupRow>> LoadTenantSignupsAsync(CancellationToken ct)
    {
        var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, CreatedAtUtc
            FROM dbo.tenants
            WHERE IsDeleted = 0
            """;

        var rows = new List<TenantSignupRow>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var id = reader.GetGuid(0);
            var created = reader.GetDateTime(1);
            var cairo = ToCairo(created);
            rows.Add(new TenantSignupRow(id, cairo.ToString("yyyy-MM", CultureInfo.InvariantCulture)));
        }

        return rows;
    }

    private static DateOnly TodayCairo() => DateOnly.FromDateTime(ToCairo(DateTime.UtcNow));

    private static DateTime ToCairo(DateTime utc)
    {
        var value = utc.Kind switch
        {
            DateTimeKind.Utc => utc,
            DateTimeKind.Local => utc.ToUniversalTime(),
            _ => DateTime.SpecifyKind(utc, DateTimeKind.Utc)
        };
        return TimeZoneInfo.ConvertTimeFromUtc(value, CairoTz);
    }

    private static DateTime StartUtc(DateOnly cairoDay)
    {
        var local = cairoDay.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(local, CairoTz);
    }

    private sealed record TenantSignupRow(Guid TenantId, string CohortMonth);
}
