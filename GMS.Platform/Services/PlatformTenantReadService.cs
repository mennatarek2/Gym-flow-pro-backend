namespace GMS.Platform.Services;

using System.Data;
using Microsoft.EntityFrameworkCore;
using GMS.Platform.Constants;
using GMS.Platform.DTOs;
using GMS.Platform.Entities;
using GMS.Platform.Interfaces;
using GMS.Platform.Persistence;

public class PlatformTenantReadService : IPlatformTenantReadService
{
    private const int RecentAuditLimit = 25;

    private readonly PlatformDbContext _db;
    private readonly ISubscriptionWriteRepository _repo;

    public PlatformTenantReadService(PlatformDbContext db, ISubscriptionWriteRepository repo)
    {
        _db = db;
        _repo = repo;
    }

    public async Task<PlatformPagedResult<PlatformTenantListItemDto>> ListAsync(
        string? status,
        string? tier,
        string? riskBand,
        string? search,
        int page,
        int pageSize,
        DateOnly? renewingBefore = null,
        bool? hasSubscription = null,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize <= 0 ? 20 : pageSize, 1, 100);

        var tenants = await LoadAllTenantsAsync(cancellationToken);
        var allSubs = await _db.Subscriptions.AsNoTracking().ToListAsync(cancellationToken);
        var tenantIdsWithAnySub = allSubs.Select(s => s.TenantId).ToHashSet();
        var byTenant = PickDisplaySubscription(allSubs);
        var healthByTenant = await _db.TenantHealthScores.AsNoTracking()
            .ToDictionaryAsync(h => h.TenantId, cancellationToken);
        var lastLoginByTenant = await LoadLastLoginByTenantAsync(cancellationToken);
        var ownerByTenant = await LoadOwnersByTenantAsync(cancellationToken);
        var memberUsageByTenant = await _db.UsageCounters
            .AsNoTracking()
            .Where(c => c.Period == CurrentCairoPeriod() && c.Metric == UsageMetrics.ActiveMembers)
            .ToDictionaryAsync(c => c.TenantId, cancellationToken);

        var statusFilter = ParseCsv(status);
        var tierFilter = ParseCsv(tier);
        var riskFilter = ParseCsv(riskBand);
        var searchNorm = string.IsNullOrWhiteSpace(search) ? null : search.Trim();

        IEnumerable<PlatformTenantListItemDto> rows = tenants.Select(t =>
        {
            byTenant.TryGetValue(t.Id, out var sub);
            healthByTenant.TryGetValue(t.Id, out var health);
            lastLoginByTenant.TryGetValue(t.Id, out var lastLogin);
            var hasOwner = ownerByTenant.TryGetValue(t.Id, out var owner);
            memberUsageByTenant.TryGetValue(t.Id, out var memberUsage);
            return new PlatformTenantListItemDto
            {
                Id = t.Id,
                Name = t.Name,
                GymCode = t.GymCode,
                PlanTier = sub?.PlanTier,
                Status = sub?.Status,
                BillingCycle = sub?.BillingCycle,
                CurrentPeriodStart = sub?.CurrentPeriodStart,
                CurrentPeriodEnd = sub?.CurrentPeriodEnd,
                TrialEndsAtUtc = sub?.TrialEndsAtUtc,
                PriceEgp = sub?.PriceEgp,
                RiskBand = health?.RiskBand,
                HealthScore = health?.Score,
                LastLoginAtUtc = lastLogin,
                OwnerName = hasOwner ? owner.FullName : null,
                OwnerEmail = hasOwner ? owner.Email : null,
                MemberCount = memberUsage?.Count,
                MemberCap = memberUsage?.Cap
            };
        });

        if (statusFilter.Count > 0)
            rows = rows.Where(r => r.Status != null && statusFilter.Contains(r.Status));
        if (tierFilter.Count > 0)
            rows = rows.Where(r => r.PlanTier != null && tierFilter.Contains(r.PlanTier));
        if (riskFilter.Count > 0)
            rows = rows.Where(r => r.RiskBand != null && riskFilter.Contains(r.RiskBand));
        if (searchNorm != null)
        {
            rows = rows.Where(r =>
                r.Name.Contains(searchNorm, StringComparison.OrdinalIgnoreCase) ||
                r.GymCode.Contains(searchNorm, StringComparison.OrdinalIgnoreCase));
        }
        if (renewingBefore.HasValue)
        {
            rows = rows.Where(r => r.CurrentPeriodEnd.HasValue && r.CurrentPeriodEnd.Value <= renewingBefore.Value);
        }
        if (hasSubscription == false)
            rows = rows.Where(r => !tenantIdsWithAnySub.Contains(r.Id));
        else if (hasSubscription == true)
            rows = rows.Where(r => tenantIdsWithAnySub.Contains(r.Id));

        var materialised = rows
            .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new PlatformPagedResult<PlatformTenantListItemDto>
        {
            Items = materialised.Skip((page - 1) * pageSize).Take(pageSize).ToList(),
            TotalCount = materialised.Count,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<PlatformTenantDetailDto?> GetDetailAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var tenant = await LoadTenantAsync(tenantId, cancellationToken);
        if (tenant == null)
            return null;

        var live = await _repo.GetLiveByTenantAsync(tenantId, cancellationToken);
        var sub = live ?? await _db.Subscriptions
            .AsNoTracking()
            .Where(s => s.TenantId == tenantId)
            .OrderByDescending(s => s.UpdatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        SubscriptionStatusDto? subscription = null;
        if (sub != null)
        {
            var pending = await _repo.GetPendingDowngradeTierAsync(sub.Id, cancellationToken);
            subscription = MapStatus(sub, pending);
        }

        var period = CurrentCairoPeriod();
        var now = DateTime.UtcNow;

        var changes = await GetSubscriptionChangesAsync(tenantId, cancellationToken);
        var invoices = await GetInvoicesAsync(tenantId, cancellationToken);

        var usage = await _db.UsageCounters
            .AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.Period == period)
            .OrderBy(c => c.Metric)
            .Select(c => new UsageCounterDto
            {
                Period = c.Period,
                Metric = c.Metric,
                Count = c.Count,
                Cap = c.Cap,
                OverageBilledEgp = c.OverageBilledEgp,
                UpdatedAtUtc = c.UpdatedAtUtc
            })
            .ToListAsync(cancellationToken);

        var health = await _db.TenantHealthScores
            .AsNoTracking()
            .Where(h => h.TenantId == tenantId)
            .Select(h => new TenantHealthScoreDto
            {
                RiskBand = h.RiskBand,
                Score = h.Score,
                ContributingFactorsJson = h.ContributingFactorsJson,
                ComputedAtUtc = h.ComputedAtUtc,
                AssignedPlatformUserId = h.AssignedPlatformUserId
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (health != null)
        {
            health.Summary = TryExtractSummary(health.ContributingFactorsJson);
            health.Confidence = TryExtractConfidence(health.ContributingFactorsJson);
        }

        var overrides = await _db.FeatureOverrides
            .AsNoTracking()
            .Where(o => o.TenantId == tenantId && (o.ExpiresAtUtc == null || o.ExpiresAtUtc > now))
            .OrderBy(o => o.FeatureKey)
            .Select(o => new FeatureOverrideDto
            {
                Id = o.Id,
                TenantId = o.TenantId,
                FeatureKey = o.FeatureKey,
                Enabled = o.Enabled,
                Reason = o.Reason,
                GrantedByPlatformUserId = o.GrantedByPlatformUserId,
                ExpiresAtUtc = o.ExpiresAtUtc,
                CreatedAtUtc = o.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);

        var coupons = await _db.PriceOverrides
            .AsNoTracking()
            .Where(p => p.TenantId == tenantId)
            .OrderByDescending(p => p.CreatedAtUtc)
            .Select(p => new PriceOverrideDto
            {
                Id = p.Id,
                TenantId = p.TenantId,
                DiscountType = p.DiscountType,
                Value = p.Value,
                ExpiresAtUtc = p.ExpiresAtUtc,
                Reason = p.Reason,
                GrantedByPlatformUserId = p.GrantedByPlatformUserId,
                CreatedAtUtc = p.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);

        var auditRows = await _db.PlatformAuditLogs
            .AsNoTracking()
            .Where(a => a.TenantId == tenantId)
            .OrderByDescending(a => a.CreatedAtUtc)
            .Take(RecentAuditLimit)
            .ToListAsync(cancellationToken);

        var actorIds = auditRows.Select(a => a.ActorPlatformUserId).Distinct().ToList();
        var actorNames = await _db.PlatformAdminUsers
            .AsNoTracking()
            .Where(u => actorIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, cancellationToken);

        var audit = auditRows.Select(a => new PlatformAuditLogDto
        {
            Id = a.Id,
            ActorPlatformUserId = a.ActorPlatformUserId,
            ActorName = actorNames.TryGetValue(a.ActorPlatformUserId, out var name) ? name : null,
            Action = a.Action,
            TenantId = a.TenantId,
            BeforeJson = a.BeforeJson,
            AfterJson = a.AfterJson,
            CreatedAtUtc = a.CreatedAtUtc
        }).ToList();

        var lastLogins = await LoadLastLoginByTenantAsync(cancellationToken);
        lastLogins.TryGetValue(tenantId, out var lastLogin);

        var users = await LoadTenantUsersAsync(tenantId, cancellationToken);

        return new PlatformTenantDetailDto
        {
            Id = tenant.Id,
            Name = tenant.Name,
            NameAr = tenant.NameAr,
            GymCode = tenant.GymCode,
            City = tenant.City,
            PhoneNumber = tenant.PhoneNumber,
            Email = tenant.Email,
            IsActive = tenant.IsActive,
            Subscription = subscription,
            SubscriptionChanges = changes.ToList(),
            Invoices = invoices.ToList(),
            UsageCounters = usage,
            Health = health,
            FeatureOverrides = overrides,
            PriceOverrides = coupons,
            RecentAudit = audit,
            LastLoginAtUtc = lastLogin,
            Users = users
        };
    }

    /// <summary>Staff login accounts for the tenant (Member-role accounts excluded, mirroring
    /// AdminService.GetStaffUsersAsync's own filter) — raw SQL against dbo.* since PlatformDbContext
    /// deliberately has no EF model for tenant-side Identity tables.</summary>
    private async Task<List<PlatformTenantUserDto>> LoadTenantUsersAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        if (!_db.Database.IsRelational())
            return new List<PlatformTenantUserDto>();

        var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT u.Id, u.FirstName, u.LastName, u.Email, u.IsActive, u.UpdatedAtUtc, r.Name AS RoleName
            FROM dbo.AspNetUsers u
            LEFT JOIN dbo.AspNetUserRoles ur ON ur.UserId = u.Id
            LEFT JOIN dbo.AspNetRoles r ON r.Id = ur.RoleId
            WHERE u.TenantId = @tenantId
            """;
        var param = command.CreateParameter();
        param.ParameterName = "@tenantId";
        param.Value = tenantId;
        command.Parameters.Add(param);

        var users = new List<PlatformTenantUserDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var role = reader["RoleName"]?.ToString();
            if (string.IsNullOrEmpty(role) || string.Equals(role, "Member", StringComparison.OrdinalIgnoreCase))
                continue;

            users.Add(new PlatformTenantUserDto
            {
                Id = reader.GetGuid(reader.GetOrdinal("Id")),
                FullName = $"{reader["FirstName"]} {reader["LastName"]}".Trim(),
                Email = reader["Email"]?.ToString() ?? string.Empty,
                Role = role,
                IsActive = reader["IsActive"] is bool b && b,
                UpdatedAtUtc = reader["UpdatedAtUtc"] is DateTime dt ? dt : null
            });
        }

        return users.OrderBy(u => u.FullName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<IReadOnlyList<SubscriptionChangeDto>> GetSubscriptionChangesAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        return await _db.SubscriptionChanges
            .AsNoTracking()
            .Where(c => c.TenantId == tenantId)
            .OrderByDescending(c => c.EffectiveAtUtc)
            .ThenByDescending(c => c.CreatedAtUtc)
            .Select(c => new SubscriptionChangeDto
            {
                Id = c.Id,
                TenantId = c.TenantId,
                SubscriptionId = c.SubscriptionId,
                ChangeType = c.ChangeType,
                FromTier = c.FromTier,
                ToTier = c.ToTier,
                EffectiveAtUtc = c.EffectiveAtUtc,
                ProratedAmountEgp = c.ProratedAmountEgp,
                InitiatedBy = c.InitiatedBy,
                PlatformAdminUserId = c.PlatformAdminUserId,
                Reason = c.Reason,
                CreatedAtUtc = c.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PlatformInvoiceDto>> GetInvoicesAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        return await _db.PlatformInvoices
            .AsNoTracking()
            .Where(i => i.TenantId == tenantId)
            .OrderByDescending(i => i.CreatedAtUtc)
            .Select(i => new PlatformInvoiceDto
            {
                Id = i.Id,
                TenantId = i.TenantId,
                SubscriptionId = i.SubscriptionId,
                InvoiceNumber = i.InvoiceNumber,
                PeriodStart = i.PeriodStart,
                PeriodEnd = i.PeriodEnd,
                Subtotal = i.Subtotal,
                VatAmount = i.VatAmount,
                Total = i.Total,
                Currency = i.Currency,
                Status = i.Status,
                DueDate = i.DueDate,
                PaidAtUtc = i.PaidAtUtc,
                PaymentMethod = i.PaymentMethod,
                EtaUuid = i.EtaUuid,
                PdfUrl = i.PdfUrl,
                CreatedAtUtc = i.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// One active Owner-role account per tenant, for the whole tenant list in a single bulk query —
    /// the same "Owner" role relationship PlatformImpersonationService.CreateAsync uses to pick who
    /// to impersonate. A tenant with more than one active Owner account (not expected, not prevented
    /// either) deterministically picks the earliest-created one. Raw SQL against dbo.* since
    /// PlatformDbContext has no EF model for tenant-side Identity tables.
    /// </summary>
    private async Task<Dictionary<Guid, (string FullName, string Email)>> LoadOwnersByTenantAsync(
        CancellationToken cancellationToken)
    {
        if (!_db.Database.IsRelational())
            return new Dictionary<Guid, (string, string)>();

        var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT u.TenantId, u.FirstName, u.LastName, u.Email, u.CreatedAtUtc
            FROM dbo.AspNetUsers u
            JOIN dbo.AspNetUserRoles ur ON ur.UserId = u.Id
            JOIN dbo.AspNetRoles r ON r.Id = ur.RoleId
            WHERE r.Name = 'Owner' AND u.IsActive = 1 AND u.TenantId IS NOT NULL
            """;

        var rows = new List<(Guid TenantId, string FullName, string Email, DateTime CreatedAtUtc)>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add((
                    reader.GetGuid(reader.GetOrdinal("TenantId")),
                    $"{reader["FirstName"]} {reader["LastName"]}".Trim(),
                    reader["Email"]?.ToString() ?? string.Empty,
                    reader.GetDateTime(reader.GetOrdinal("CreatedAtUtc"))));
            }
        }

        return rows
            .GroupBy(r => r.TenantId)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var first = g.OrderBy(r => r.CreatedAtUtc).First();
                    return (first.FullName, first.Email);
                });
    }

    private async Task<Dictionary<Guid, DateTime?>> LoadLastLoginByTenantAsync(CancellationToken cancellationToken)
    {
        if (!_db.Database.IsRelational())
            return new Dictionary<Guid, DateTime?>();

        var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        // Proxy for last activity: max AspNetUsers.UpdatedAtUtc per tenant (no dedicated LastLogin column).
        command.CommandText = """
            SELECT TenantId, MAX(UpdatedAtUtc) AS LastLoginAtUtc
            FROM dbo.AspNetUsers
            WHERE TenantId IS NOT NULL AND IsActive = 1
            GROUP BY TenantId
            """;

        var map = new Dictionary<Guid, DateTime?>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var tenantId = reader.GetGuid(0);
            DateTime? last = reader.IsDBNull(1) ? null : reader.GetDateTime(1);
            map[tenantId] = last;
        }

        return map;
    }

    private static string CurrentCairoPeriod()
    {
        var cairo = TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time");
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, cairo);
        return $"{local.Year:D4}-{local.Month:D2}";
    }

    private static Dictionary<Guid, PlatformSubscription> PickDisplaySubscription(
        IReadOnlyList<PlatformSubscription> all)
    {
        return all
            .GroupBy(s => s.TenantId)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var live = g.FirstOrDefault(s =>
                        s.Status is SubscriptionStatuses.Trialing
                            or SubscriptionStatuses.Active
                            or SubscriptionStatuses.PastDue);
                    return live ?? g.OrderByDescending(s => s.UpdatedAtUtc).First();
                });
    }

    private static SubscriptionStatusDto MapStatus(PlatformSubscription s, string? pendingDowngradeTier) => new()
    {
        Id = s.Id,
        TenantId = s.TenantId,
        PlanTier = s.PlanTier,
        Status = s.Status,
        BillingCycle = s.BillingCycle,
        PriceEgp = s.PriceEgp,
        CurrentPeriodStart = s.CurrentPeriodStart,
        CurrentPeriodEnd = s.CurrentPeriodEnd,
        TrialEndsAtUtc = s.TrialEndsAtUtc,
        CancelAtPeriodEnd = s.CancelAtPeriodEnd,
        CancelledAtUtc = s.CancelledAtUtc,
        SuspendedAtUtc = s.SuspendedAtUtc,
        UpdatedAtUtc = s.UpdatedAtUtc,
        PendingDowngradeTier = pendingDowngradeTier,
        HasPaymentMethodOnFile = !string.IsNullOrWhiteSpace(s.SavedCardToken)
    };

    private async Task<List<TenantRow>> LoadAllTenantsAsync(CancellationToken cancellationToken)
    {
        if (!_db.Database.IsRelational())
            return new List<TenantRow>();

        var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Name, NameAr, GymCode, City, PhoneNumber, Email, IsActive
            FROM dbo.tenants
            WHERE IsDeleted = 0
            """;

        var rows = new List<TenantRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            rows.Add(ReadTenant(reader));

        return rows;
    }

    private async Task<TenantRow?> LoadTenantAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        if (!_db.Database.IsRelational())
            return null;

        var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT TOP (1) Id, Name, NameAr, GymCode, City, PhoneNumber, Email, IsActive
            FROM dbo.tenants
            WHERE Id = @tenantId AND IsDeleted = 0
            """;

        var param = command.CreateParameter();
        param.ParameterName = "@tenantId";
        param.Value = tenantId;
        command.Parameters.Add(param);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return ReadTenant(reader);
    }

    private static TenantRow ReadTenant(System.Data.Common.DbDataReader reader) => new(
        reader.GetGuid(reader.GetOrdinal("Id")),
        reader["Name"]?.ToString() ?? string.Empty,
        reader["NameAr"]?.ToString() ?? string.Empty,
        reader["GymCode"]?.ToString() ?? string.Empty,
        reader["City"]?.ToString() ?? string.Empty,
        reader["PhoneNumber"]?.ToString() ?? string.Empty,
        reader["Email"]?.ToString() ?? string.Empty,
        reader["IsActive"] is bool b && b);

    private static HashSet<string> ParseCsv(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        return raw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string? TryExtractSummary(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("summary", out var s))
                return s.GetString();
        }
        catch { /* ignore */ }
        return null;
    }

    private static decimal? TryExtractConfidence(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("confidence", out var c) &&
                c.TryGetDecimal(out var d))
                return d;
        }
        catch { /* ignore */ }
        return null;
    }

    private sealed record TenantRow(
        Guid Id,
        string Name,
        string NameAr,
        string GymCode,
        string City,
        string PhoneNumber,
        string Email,
        bool IsActive);
}
