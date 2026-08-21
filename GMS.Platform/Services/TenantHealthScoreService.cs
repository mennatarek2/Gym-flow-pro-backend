namespace GMS.Platform.Services;

using System.Data;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using GMS.Platform.Constants;
using GMS.Platform.Entities;
using GMS.Platform.Interfaces;
using GMS.Platform.Persistence;

/// <summary>
/// Rules-based tenant health scorer (CP7). No ML — Phase 6 deferral.
/// Missing signals reduce confidence via weight renormalization; they never crash scoring.
/// </summary>
public class TenantHealthScoreService : ITenantHealthScoreService, IComputeTenantHealthScoresJob
{
    private static readonly TimeZoneInfo CairoTz = TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time");
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    private readonly PlatformDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly ILogger<TenantHealthScoreService> _logger;

    public TenantHealthScoreService(
        PlatformDbContext db,
        IConfiguration configuration,
        ILogger<TenantHealthScoreService> logger)
    {
        _db = db;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var weights = LoadWeights();
        _logger.LogInformation(
            "ComputeTenantHealthScores: rules-based (no ML). Weights from PlatformHealth:Weights — " +
            "login_frequency={Login:F2}, feature_breadth={Feature:F2}, payment_health={Payment:F2}, " +
            "member_base_trend={Members:F2}, support_ticket_volume={Support:F2}, usage_vs_cap={Usage:F2}",
            weights.LoginFrequency, weights.FeatureBreadth, weights.PaymentHealth,
            weights.MemberBaseTrend, weights.SupportTicketVolume, weights.UsageVsCap);

        var tenantIds = await _db.Subscriptions
            .AsNoTracking()
            .Where(s => s.Status == SubscriptionStatuses.Active || s.Status == SubscriptionStatuses.PastDue)
            .Select(s => s.TenantId)
            .Distinct()
            .ToListAsync(cancellationToken);

        _logger.LogInformation(
            "ComputeTenantHealthScores: scoring {Count} active/past_due tenant(s)",
            tenantIds.Count);

        var lastLogins = await LoadLastLoginByTenantAsync(cancellationToken);
        var memberCounts = await LoadActiveMemberCountsAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var period = CurrentCairoPeriod(now);
        var priorPeriod = PriorCairoPeriod(now);

        foreach (var tenantId in tenantIds)
        {
            try
            {
                await ScoreTenantAsync(
                    tenantId, weights, lastLogins, memberCounts, period, priorPeriod, now, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ComputeTenantHealthScores failed for tenant {TenantId}", tenantId);
            }
        }
    }

    public async Task<TenantHealthScore> ScoreTenantAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var weights = LoadWeights();
        var lastLogins = await LoadLastLoginByTenantAsync(cancellationToken);
        var memberCounts = await LoadActiveMemberCountsAsync(cancellationToken);
        var now = DateTime.UtcNow;
        return await ScoreTenantAsync(
            tenantId, weights, lastLogins, memberCounts,
            CurrentCairoPeriod(now), PriorCairoPeriod(now), now, cancellationToken);
    }

    private async Task<TenantHealthScore> ScoreTenantAsync(
        Guid tenantId,
        HealthWeights weights,
        IReadOnlyDictionary<Guid, DateTime?> lastLogins,
        IReadOnlyDictionary<Guid, int> memberCounts,
        string period,
        string priorPeriod,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var subscription = await _db.Subscriptions
            .AsNoTracking()
            .Where(s => s.TenantId == tenantId &&
                        (s.Status == SubscriptionStatuses.Active || s.Status == SubscriptionStatuses.PastDue))
            .OrderByDescending(s => s.UpdatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        var invoices = await _db.PlatformInvoices
            .AsNoTracking()
            .Where(i => i.TenantId == tenantId)
            .OrderByDescending(i => i.CreatedAtUtc)
            .Take(6)
            .ToListAsync(cancellationToken);

        var usageCurrent = await _db.UsageCounters
            .AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.Period == period)
            .ToListAsync(cancellationToken);

        var usagePrior = await _db.UsageCounters
            .AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.Period == priorPeriod)
            .ToListAsync(cancellationToken);

        lastLogins.TryGetValue(tenantId, out var lastLogin);
        memberCounts.TryGetValue(tenantId, out var liveMemberCount);

        var factors = new List<SignalFactor>
        {
            ScoreLoginFrequency(lastLogin, now, weights.LoginFrequency),
            ScoreFeatureBreadth(usageCurrent, weights.FeatureBreadth),
            ScorePaymentHealth(subscription, invoices, weights.PaymentHealth),
            ScoreMemberBaseTrend(usageCurrent, usagePrior, liveMemberCount, weights.MemberBaseTrend),
            ScoreSupportTicketsStub(weights.SupportTicketVolume),
            ScoreUsageVsCap(usageCurrent, weights.UsageVsCap)
        };

        var available = factors.Where(f => f.Available).ToList();
        var availableWeight = available.Sum(f => f.ConfiguredWeight);
        var confidence = weights.Total <= 0
            ? 0m
            : Math.Round(availableWeight / weights.Total, 4, MidpointRounding.AwayFromZero);

        int score;
        if (available.Count == 0 || availableWeight <= 0)
        {
            score = 50; // neutral when nothing measurable — watch band, high uncertainty
        }
        else
        {
            var weighted = available.Sum(f => f.Score * (f.ConfiguredWeight / availableWeight));
            score = (int)Math.Clamp(Math.Round(weighted, MidpointRounding.AwayFromZero), 0, 100);
        }

        var band = MapBand(score);
        var payload = new ContributingFactorsPayload
        {
            Model = "rules_v1",
            MlUsed = false,
            ComputedAtUtc = now,
            Score = score,
            RiskBand = band,
            Confidence = confidence,
            Weights = new Dictionary<string, decimal>
            {
                [TenantHealthSignals.LoginFrequency] = weights.LoginFrequency,
                [TenantHealthSignals.FeatureBreadth] = weights.FeatureBreadth,
                [TenantHealthSignals.PaymentHealth] = weights.PaymentHealth,
                [TenantHealthSignals.MemberBaseTrend] = weights.MemberBaseTrend,
                [TenantHealthSignals.SupportTicketVolume] = weights.SupportTicketVolume,
                [TenantHealthSignals.UsageVsCap] = weights.UsageVsCap
            },
            Signals = factors.Select(f => new SignalFactorDto
            {
                Key = f.Key,
                Label = f.Label,
                Available = f.Available,
                Score = f.Available ? f.Score : null,
                ConfiguredWeight = f.ConfiguredWeight,
                EffectiveWeight = f.Available && availableWeight > 0
                    ? Math.Round(f.ConfiguredWeight / availableWeight, 4)
                    : 0,
                Summary = f.Summary,
                Detail = f.Detail
            }).ToList(),
            Summary = BuildHumanSummary(score, band, confidence, factors)
        };

        var json = JsonSerializer.Serialize(payload, JsonOptions);

        var row = await _db.TenantHealthScores.FirstOrDefaultAsync(h => h.TenantId == tenantId, cancellationToken);
        if (row == null)
        {
            row = new TenantHealthScore { TenantId = tenantId };
            _db.TenantHealthScores.Add(row);
        }

        row.Score = score;
        row.RiskBand = band;
        row.ContributingFactorsJson = json;
        row.ComputedAtUtc = now;
        // Preserve assignee across recomputes

        await _db.SaveChangesAsync(cancellationToken);
        return row;
    }

    private HealthWeights LoadWeights()
    {
        var section = _configuration.GetSection("PlatformHealth:Weights");
        var w = new HealthWeights(
            section.GetValue("LoginFrequency", 0.20m),
            section.GetValue("FeatureBreadth", 0.15m),
            section.GetValue("PaymentHealth", 0.25m),
            section.GetValue("MemberBaseTrend", 0.20m),
            section.GetValue("SupportTicketVolume", 0.05m),
            section.GetValue("UsageVsCap", 0.15m));

        if (w.Total <= 0)
        {
            _logger.LogWarning("PlatformHealth:Weights sum to zero — falling back to documented starting weights.");
            return new HealthWeights(0.20m, 0.15m, 0.25m, 0.20m, 0.05m, 0.15m);
        }

        return w;
    }

    private string MapBand(int score)
    {
        var healthyMin = _configuration.GetValue("PlatformHealth:Bands:HealthyMin", 75);
        var watchMin = _configuration.GetValue("PlatformHealth:Bands:WatchMin", 50);
        var atRiskMin = _configuration.GetValue("PlatformHealth:Bands:AtRiskMin", 25);

        if (score >= healthyMin) return TenantRiskBands.Healthy;
        if (score >= watchMin) return TenantRiskBands.Watch;
        if (score >= atRiskMin) return TenantRiskBands.AtRisk;
        return TenantRiskBands.Critical;
    }

    private static SignalFactor ScoreLoginFrequency(DateTime? lastLogin, DateTime now, decimal weight)
    {
        const string key = TenantHealthSignals.LoginFrequency;
        const string label = "Login frequency";
        if (!lastLogin.HasValue)
        {
            return Missing(key, label, weight,
                "No staff login activity observed (AspNetUsers.UpdatedAtUtc proxy missing).",
                new { source = "aspnetusers.UpdatedAtUtc", lastLoginAtUtc = (DateTime?)null });
        }

        var days = Math.Max(0, (now - lastLogin.Value).TotalDays);
        var score = days switch
        {
            <= 2 => 100,
            <= 7 => 80,
            <= 14 => 55,
            <= 30 => 30,
            <= 60 => 10,
            _ => 0
        };

        return Available(key, label, weight, score,
            days <= 7
                ? $"Staff activity within the last {days:0} day(s)."
                : $"Staff quiet for {days:0} day(s) — reduced engagement signal.",
            new { source = "aspnetusers.UpdatedAtUtc", lastLoginAtUtc = lastLogin, daysSinceLogin = Math.Round(days, 1) });
    }

    private static SignalFactor ScoreFeatureBreadth(IReadOnlyList<UsageCounter> usage, decimal weight)
    {
        const string key = TenantHealthSignals.FeatureBreadth;
        const string label = "Feature breadth";
        if (usage.Count == 0)
        {
            return Missing(key, label, weight,
                "No usage_counters for the current Cairo month yet (rollup may not have run).",
                new { periodCounters = 0 });
        }

        var active = usage.Count(c => c.Count > 0);
        var total = Math.Max(1, usage.Count);
        var ratio = (decimal)active / total;
        var score = (int)Math.Clamp(Math.Round(ratio * 100m), 0, 100);
        var metrics = usage.Select(c => new { c.Metric, c.Count }).ToList();

        return Available(key, label, weight, score,
            $"{active}/{total} tracked metrics show activity this period.",
            new { activeMetrics = active, trackedMetrics = total, metrics });
    }

    private static SignalFactor ScorePaymentHealth(
        PlatformSubscription? subscription,
        IReadOnlyList<PlatformInvoice> invoices,
        decimal weight)
    {
        const string key = TenantHealthSignals.PaymentHealth;
        const string label = "Payment health";
        if (subscription == null)
        {
            return Missing(key, label, weight, "No active/past_due subscription row found.", null);
        }

        var openUnpaid = invoices.Count(i =>
            (i.Status == "issued" || i.Status == "past_due") && i.PaidAtUtc == null);
        var recentPaid = invoices.Count(i => i.Status == "paid");

        var score = subscription.Status switch
        {
            SubscriptionStatuses.Active when openUnpaid == 0 => 100,
            SubscriptionStatuses.Active when openUnpaid == 1 => 70,
            SubscriptionStatuses.Active => 45,
            SubscriptionStatuses.PastDue when openUnpaid <= 1 => 25,
            SubscriptionStatuses.PastDue => 10,
            _ => 40
        };

        return Available(key, label, weight, score,
            $"Subscription is '{subscription.Status}' with {openUnpaid} unpaid invoice(s) and {recentPaid} recent paid.",
            new
            {
                subscription.Status,
                subscription.CurrentPeriodEnd,
                openUnpaidInvoices = openUnpaid,
                recentPaidInvoices = recentPaid
            });
    }

    private static SignalFactor ScoreMemberBaseTrend(
        IReadOnlyList<UsageCounter> current,
        IReadOnlyList<UsageCounter> prior,
        int liveMemberCount,
        decimal weight)
    {
        const string key = TenantHealthSignals.MemberBaseTrend;
        const string label = "Member-base trend";

        var cur = current.FirstOrDefault(c => c.Metric == UsageMetrics.ActiveMembers)?.Count
                  ?? (liveMemberCount > 0 ? liveMemberCount : (int?)null);
        var prev = prior.FirstOrDefault(c => c.Metric == UsageMetrics.ActiveMembers)?.Count;

        if (cur == null)
        {
            return Missing(key, label, weight,
                "Active member count unavailable for current period.",
                new { liveMemberCount });
        }

        if (prev == null || prev == 0)
        {
            // First observation — treat as available but neutral/slightly positive baseline
            var baseline = cur.Value >= 10 ? 75 : cur.Value > 0 ? 60 : 40;
            return Available(key, label, weight, baseline,
                $"Baseline member count {cur} (no prior period to compare).",
                new { currentActiveMembers = cur, priorActiveMembers = (int?)null, deltaPct = (decimal?)null });
        }

        var deltaPct = ((decimal)cur.Value - prev.Value) / prev.Value;
        var score = deltaPct switch
        {
            >= 0.05m => 100,
            >= 0m => 80,
            >= -0.05m => 60,
            >= -0.15m => 35,
            _ => 10
        };

        return Available(key, label, weight, score,
            deltaPct >= 0
                ? $"Member base up {deltaPct:P0} vs prior month ({prev} → {cur})."
                : $"Member base down {Math.Abs(deltaPct):P0} vs prior month ({prev} → {cur}).",
            new { currentActiveMembers = cur, priorActiveMembers = prev, deltaPct = Math.Round(deltaPct, 4) });
    }

    private static SignalFactor ScoreSupportTicketsStub(decimal weight)
    {
        // Stub until a support tool exists — Available=false so it reduces confidence, not the score.
        return Missing(
            TenantHealthSignals.SupportTicketVolume,
            "Support ticket volume",
            weight,
            "Stubbed: support tool not connected (input treated as unavailable, not zero-scored).",
            new { stubbed = true, ticketCount = 0 });
    }

    private static SignalFactor ScoreUsageVsCap(IReadOnlyList<UsageCounter> usage, decimal weight)
    {
        const string key = TenantHealthSignals.UsageVsCap;
        const string label = "Usage vs cap";
        var capped = usage.Where(c => c.Cap is > 0).ToList();
        if (capped.Count == 0)
        {
            return Missing(key, label, weight,
                "No capped usage_counters available for utilization scoring.",
                null);
        }

        var ratios = capped
            .Select(c => new
            {
                c.Metric,
                c.Count,
                Cap = c.Cap!.Value,
                Ratio = (decimal)c.Count / c.Cap.Value
            })
            .ToList();

        // Sweet spot 30–85% utilization; under-use or hard overage both lower the score.
        var perMetricScores = ratios.Select(r =>
        {
            if (r.Ratio < 0.15m) return 25;      // paying for unused capacity — churn risk
            if (r.Ratio < 0.30m) return 50;
            if (r.Ratio <= 0.85m) return 100;    // healthy utilization
            if (r.Ratio <= 1.00m) return 70;     // approaching limit
            return 40;                            // over cap (soft overage / friction)
        }).ToList();

        var score = (int)Math.Round(perMetricScores.Average());
        return Available(key, label, weight, score,
            $"Utilization across {capped.Count} capped metric(s); average signal score {score}.",
            new { metrics = ratios });
    }

    private static string BuildHumanSummary(
        int score, string band, decimal confidence, IReadOnlyList<SignalFactor> factors)
    {
        var weak = factors
            .Where(f => f.Available && f.Score < 50)
            .OrderBy(f => f.Score)
            .Take(3)
            .Select(f => f.Label)
            .ToList();

        var missing = factors.Count(f => !f.Available);
        var weakText = weak.Count > 0
            ? $" Weakest: {string.Join(", ", weak)}."
            : " No weak signals among measured inputs.";
        return $"Score {score}/100 → {band} (confidence {confidence:P0}; {missing} signal(s) unavailable).{weakText}";
    }

    private static SignalFactor Missing(
        string key, string label, decimal weight, string summary, object? detail) =>
        new(key, label, false, 0, weight, summary, detail);

    private static SignalFactor Available(
        string key, string label, decimal weight, int score, string summary, object? detail) =>
        new(key, label, true, score, weight, summary, detail);

    private async Task<Dictionary<Guid, DateTime?>> LoadLastLoginByTenantAsync(CancellationToken ct)
    {
        if (!_db.Database.IsRelational())
            return new Dictionary<Guid, DateTime?>();

        var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT TenantId, MAX(UpdatedAtUtc) AS LastLoginAtUtc
            FROM dbo.AspNetUsers
            WHERE TenantId IS NOT NULL AND IsActive = 1
            GROUP BY TenantId
            """;

        var map = new Dictionary<Guid, DateTime?>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            map[reader.GetGuid(0)] = reader.IsDBNull(1) ? null : reader.GetDateTime(1);
        return map;
    }

    private async Task<Dictionary<Guid, int>> LoadActiveMemberCountsAsync(CancellationToken ct)
    {
        if (!_db.Database.IsRelational())
            return new Dictionary<Guid, int>();

        var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT TenantId, COUNT(1)
            FROM dbo.gym_members
            WHERE IsDeleted = 0 AND IsActive = 1
            GROUP BY TenantId
            """;

        var map = new Dictionary<Guid, int>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            map[reader.GetGuid(0)] = reader.GetInt32(1);
        return map;
    }

    private static string CurrentCairoPeriod(DateTime utcNow)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(utcNow, CairoTz);
        return $"{local.Year:D4}-{local.Month:D2}";
    }

    private static string PriorCairoPeriod(DateTime utcNow)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(utcNow, CairoTz).AddMonths(-1);
        return $"{local.Year:D4}-{local.Month:D2}";
    }

    private sealed record HealthWeights(
        decimal LoginFrequency,
        decimal FeatureBreadth,
        decimal PaymentHealth,
        decimal MemberBaseTrend,
        decimal SupportTicketVolume,
        decimal UsageVsCap)
    {
        public decimal Total =>
            LoginFrequency + FeatureBreadth + PaymentHealth +
            MemberBaseTrend + SupportTicketVolume + UsageVsCap;
    }

    private sealed record SignalFactor(
        string Key,
        string Label,
        bool Available,
        int Score,
        decimal ConfiguredWeight,
        string Summary,
        object? Detail);

    private sealed class ContributingFactorsPayload
    {
        public string Model { get; set; } = "rules_v1";
        public bool MlUsed { get; set; }
        public DateTime ComputedAtUtc { get; set; }
        public int Score { get; set; }
        public string RiskBand { get; set; } = string.Empty;
        public decimal Confidence { get; set; }
        public Dictionary<string, decimal> Weights { get; set; } = new();
        public List<SignalFactorDto> Signals { get; set; } = new();
        public string Summary { get; set; } = string.Empty;
    }

    private sealed class SignalFactorDto
    {
        public string Key { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public bool Available { get; set; }
        public int? Score { get; set; }
        public decimal ConfiguredWeight { get; set; }
        public decimal EffectiveWeight { get; set; }
        public string Summary { get; set; } = string.Empty;
        public object? Detail { get; set; }
    }
}
