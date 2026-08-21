namespace GMS.Platform.Services;

using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using GMS.Platform.Constants;
using GMS.Platform.DTOs;
using GMS.Platform.Entities;
using GMS.Platform.Interfaces;
using GMS.Platform.Persistence;

public class PlatformRiskQueueService : IPlatformRiskQueueService
{
    private readonly PlatformDbContext _db;
    private readonly IPlatformAuditService _audit;

    public PlatformRiskQueueService(PlatformDbContext db, IPlatformAuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<IReadOnlyList<RiskQueueItemDto>> ListAsync(
        string? bandCsv,
        CancellationToken cancellationToken = default)
    {
        var bands = ParseBands(bandCsv);
        var scores = await _db.TenantHealthScores
            .AsNoTracking()
            .Where(h => bands.Contains(h.RiskBand))
            .OrderBy(h => h.Score)
            .ThenByDescending(h => h.ComputedAtUtc)
            .ToListAsync(cancellationToken);

        if (scores.Count == 0)
            return Array.Empty<RiskQueueItemDto>();

        var tenantIds = scores.Select(s => s.TenantId).ToList();
        var tenants = await LoadTenantsAsync(tenantIds, cancellationToken);
        var subs = await _db.Subscriptions
            .AsNoTracking()
            .Where(s => tenantIds.Contains(s.TenantId))
            .ToListAsync(cancellationToken);
        var subByTenant = subs
            .GroupBy(s => s.TenantId)
            .ToDictionary(
                g => g.Key,
                g => g.FirstOrDefault(s =>
                         s.Status is SubscriptionStatuses.Active or SubscriptionStatuses.PastDue)
                     ?? g.OrderByDescending(x => x.UpdatedAtUtc).First());

        var outcomes = await _db.RiskQueueOutcomes
            .AsNoTracking()
            .Where(o => tenantIds.Contains(o.TenantId))
            .OrderByDescending(o => o.CreatedAtUtc)
            .ToListAsync(cancellationToken);
        var outcomesByTenant = outcomes
            .GroupBy(o => o.TenantId)
            .ToDictionary(g => g.Key, g => g.Take(10).Select(MapOutcome).ToList());

        var items = new List<RiskQueueItemDto>();
        foreach (var h in scores)
        {
            tenants.TryGetValue(h.TenantId, out var tenant);
            subByTenant.TryGetValue(h.TenantId, out var sub);
            outcomesByTenant.TryGetValue(h.TenantId, out var recent);

            items.Add(new RiskQueueItemDto
            {
                TenantId = h.TenantId,
                Name = tenant?.Name ?? string.Empty,
                GymCode = tenant?.GymCode ?? string.Empty,
                PlanTier = sub?.PlanTier,
                SubscriptionStatus = sub?.Status,
                Score = h.Score,
                RiskBand = h.RiskBand,
                ComputedAtUtc = h.ComputedAtUtc,
                AssignedPlatformUserId = h.AssignedPlatformUserId,
                AssignedAtUtc = h.AssignedAtUtc,
                ContributingFactorsJson = h.ContributingFactorsJson,
                Summary = TryExtractSummary(h.ContributingFactorsJson),
                RecentOutcomes = recent ?? new List<RiskQueueOutcomeDto>()
            });
        }

        return items;
    }

    public async Task<PlatformActionResult> AssignAsync(
        Guid tenantId,
        Guid actorPlatformUserId,
        Guid? assigneePlatformUserId,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var row = await _db.TenantHealthScores
            .FirstOrDefaultAsync(h => h.TenantId == tenantId, cancellationToken);

        if (row == null)
            return PlatformActionResult.Fail("NOT_SCORED", "Tenant has no health score yet.");

        var before = new { row.AssignedPlatformUserId, row.AssignedAtUtc };
        row.AssignedPlatformUserId = assigneePlatformUserId;
        row.AssignedAtUtc = assigneePlatformUserId.HasValue ? DateTime.UtcNow : null;
        await _db.SaveChangesAsync(cancellationToken);

        await _audit.LogAsync(
            actorPlatformUserId,
            "platform.risk_queue.assign",
            tenantId,
            before,
            new { row.AssignedPlatformUserId, row.AssignedAtUtc },
            ipAddress);

        return PlatformActionResult.Ok();
    }

    public async Task<(PlatformActionResult Result, RiskQueueOutcomeDto? Outcome)> RecordOutcomeAsync(
        Guid tenantId,
        Guid actorPlatformUserId,
        RecordRiskQueueOutcomeRequest request,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var outcome = (request.Outcome ?? string.Empty).Trim().ToLowerInvariant();
        if (!RiskQueueOutcomes.IsValid(outcome))
            return (PlatformActionResult.Fail("INVALID_OUTCOME",
                "outcome must be contacted|retained|churned|no_answer|watching."), null);

        if (!await _db.TenantHealthScores.AnyAsync(h => h.TenantId == tenantId, cancellationToken))
            return (PlatformActionResult.Fail("NOT_SCORED", "Tenant has no health score yet."), null);

        var row = new RiskQueueOutcome
        {
            TenantId = tenantId,
            PlatformUserId = actorPlatformUserId,
            Outcome = outcome,
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.RiskQueueOutcomes.Add(row);
        await _db.SaveChangesAsync(cancellationToken);

        await _audit.LogAsync(
            actorPlatformUserId,
            "platform.risk_queue.outcome",
            tenantId,
            before: null,
            after: new { row.Id, row.Outcome, row.Note },
            ipAddress);

        return (PlatformActionResult.Ok(), MapOutcome(row));
    }

    private static HashSet<string> ParseBands(string? bandCsv)
    {
        if (string.IsNullOrWhiteSpace(bandCsv))
            return new HashSet<string>(TenantRiskBands.QueueDefault, StringComparer.OrdinalIgnoreCase);

        var set = bandCsv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(TenantRiskBands.IsValid)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return set.Count > 0
            ? set
            : new HashSet<string>(TenantRiskBands.QueueDefault, StringComparer.OrdinalIgnoreCase);
    }

    private static string? TryExtractSummary(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("summary", out var s))
                return s.GetString();
        }
        catch
        {
            // ignore malformed
        }

        return null;
    }

    private static RiskQueueOutcomeDto MapOutcome(RiskQueueOutcome o) => new()
    {
        Id = o.Id,
        TenantId = o.TenantId,
        PlatformUserId = o.PlatformUserId,
        Outcome = o.Outcome,
        Note = o.Note,
        CreatedAtUtc = o.CreatedAtUtc
    };

    private async Task<Dictionary<Guid, TenantNameRow>> LoadTenantsAsync(
        IReadOnlyList<Guid> tenantIds,
        CancellationToken cancellationToken)
    {
        var map = new Dictionary<Guid, TenantNameRow>();
        if (tenantIds.Count == 0 || !_db.Database.IsRelational())
            return map;

        var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        // Load all then filter in memory — tenant volume is small for platform console.
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name, GymCode FROM dbo.tenants WHERE IsDeleted = 0";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var wanted = tenantIds.ToHashSet();
        while (await reader.ReadAsync(cancellationToken))
        {
            var id = reader.GetGuid(0);
            if (!wanted.Contains(id))
                continue;
            map[id] = new TenantNameRow(id, reader["Name"]?.ToString() ?? "", reader["GymCode"]?.ToString() ?? "");
        }

        return map;
    }

    private sealed record TenantNameRow(Guid Id, string Name, string GymCode);
}
