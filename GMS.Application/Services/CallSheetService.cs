namespace GMS.Application.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using GMS.Application.Common;
using GMS.Application.DTOs.CallSheet;
using GMS.Application.Interfaces;
using GMS.Core.Constants;
using GMS.Core.Entities;
using GMS.Core.Utilities;
using GMS.Infrastructure.Persistence;

/// <summary>
/// Daily follow-up queue. Syncs system rows from existing membership / trial / outstanding /
/// attendance data. Does not write those domains.
/// </summary>
public class CallSheetService : ICallSheetService
{
    private const int RenewalLookaheadDays = 7;
    private const int RenewalLookbackDays = 7;
    private const int TrialWindowDays = 3;
    private const int WelcomeDays = 3;
    private const int InactiveDays = 14;

    private readonly GymFlowProDbContext _db;
    private readonly ILogger<CallSheetService> _logger;

    public CallSheetService(GymFlowProDbContext db, ILogger<CallSheetService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<Result<FollowUpListDto>> GetQueueAsync(
        Guid tenantId, Guid? currentAppUserId, string? date, string? reason, string? priority,
        string? status, string? assignee, string? q)
    {
        try
        {
            await SyncSystemFollowUpsAsync(tenantId);

            Guid? meId = null;
            if (currentAppUserId.HasValue)
                meId = (await ResolveStaffAsync(tenantId, currentAppUserId.Value))?.Id;

            var today = MembershipOperational.TodayCairo();
            var items = await LoadFollowUpsAsync(tenantId);

            items = ApplyFilters(items, today, meId, date, reason, priority, status, assignee, q);

            return Result<FollowUpListDto>.Success(new FollowUpListDto
            {
                Summary = await BuildSummaryAsync(tenantId, today),
                Items = items
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading call sheet for tenant {TenantId}", tenantId);
            return Result<FollowUpListDto>.Failure("Failed to load follow-ups / فشل تحميل المتابعات", ex.Message);
        }
    }

    public async Task<Result<FollowUpSummaryDto>> GetSummaryAsync(Guid tenantId)
    {
        try
        {
            await SyncSystemFollowUpsAsync(tenantId);
            var summary = await BuildSummaryAsync(tenantId, MembershipOperational.TodayCairo());
            return Result<FollowUpSummaryDto>.Success(summary);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading call sheet summary for tenant {TenantId}", tenantId);
            return Result<FollowUpSummaryDto>.Failure("Failed to load summary / فشل تحميل الملخص", ex.Message);
        }
    }

    public async Task<Result<FollowUpDetailDto>> GetByIdAsync(Guid followUpId, Guid tenantId)
    {
        try
        {
            var row = await _db.MemberFollowUps
                .Include(f => f.Member)
                .Include(f => f.AssignedToUser)
                .FirstOrDefaultAsync(f => f.Id == followUpId && f.TenantId == tenantId);
            if (row == null)
                return Result<FollowUpDetailDto>.Failure($"{CallSheetFailureReasons.FollowUpNotFound}|Follow-up not found / المتابعة غير موجودة");

            var dto = await MapDetailAsync(row, tenantId);
            return Result<FollowUpDetailDto>.Success(dto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading follow-up {FollowUpId}", followUpId);
            return Result<FollowUpDetailDto>.Failure("Failed to load follow-up / فشل تحميل المتابعة", ex.Message);
        }
    }

    public async Task<Result<FollowUpDto>> CreateAsync(Guid tenantId, Guid staffUserId, CreateFollowUpRequest request)
    {
        try
        {
            var reason = (request.Reason ?? "custom").Trim().ToLowerInvariant();
            if (!CallSheetVocab.Reasons.Contains(reason))
                return Result<FollowUpDto>.Failure($"{CallSheetFailureReasons.InvalidReason}|Invalid reason / سبب غير صالح");

            var priority = (request.Priority ?? "medium").Trim().ToLowerInvariant();
            if (!CallSheetVocab.Priorities.Contains(priority))
                return Result<FollowUpDto>.Failure($"{CallSheetFailureReasons.InvalidPriority}|Invalid priority / أولوية غير صالحة");

            var member = await _db.GymMembers.FirstOrDefaultAsync(m => m.Id == request.MemberId && m.TenantId == tenantId);
            if (member == null)
                return Result<FollowUpDto>.Failure($"{CallSheetFailureReasons.MemberNotFound}|Member not found / العضو غير موجود");

            var staff = await ResolveStaffAsync(tenantId, staffUserId);
            if (staff == null)
                return Result<FollowUpDto>.Failure($"{CallSheetFailureReasons.StaffUserNotFound}|Staff user not found / المستخدم غير موجود");

            Guid? assigned = request.AssignedToUserId;
            if (assigned.HasValue)
            {
                var exists = await _db.AppUsers.AnyAsync(u => u.Id == assigned.Value && u.TenantId == tenantId);
                if (!exists) assigned = null;
            }

            var row = new MemberFollowUp
            {
                TenantId = tenantId,
                MemberId = member.Id,
                MembershipId = request.MembershipId,
                Reason = reason,
                Source = CallSheetVocab.SourceManual,
                SourceKey = $"manual:{Guid.NewGuid():N}",
                Priority = priority,
                Status = "pending",
                AssignedToUserId = assigned,
                DueAtUtc = request.DueAtUtc?.ToUniversalTime() ?? TodayTenUtc(),
                Why = string.IsNullOrWhiteSpace(request.Why) ? WhyForReason(reason) : request.Why.Trim(),
                Notes = Truncate(request.Notes, 500),
                RelatedType = request.RelatedType,
                RelatedId = request.RelatedId
            };
            _db.MemberFollowUps.Add(row);
            await _db.SaveChangesAsync();

            row.Member = member;
            return Result<FollowUpDto>.Success(await MapOneAsync(row, tenantId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating follow-up for tenant {TenantId}", tenantId);
            return Result<FollowUpDto>.Failure("Failed to create follow-up / فشل إنشاء المتابعة", ex.Message);
        }
    }

    public async Task<Result<bool>> RecordOutcomeAsync(
        Guid followUpId, Guid tenantId, Guid staffUserId, RecordCallOutcomeRequest request)
    {
        try
        {
            var outcome = CallSheetVocab.NormalizeOutcome(request.Outcome ?? "");
            if (!CallSheetVocab.Outcomes.Contains(outcome) && !CallSheetVocab.Outcomes.Contains(request.Outcome ?? ""))
                return Fail(CallSheetFailureReasons.InvalidOutcome, "Invalid outcome / نتيجة غير صالحة");
            if (!CallSheetVocab.Outcomes.Contains(outcome))
                outcome = (request.Outcome ?? "").Trim().ToLowerInvariant();

            var next = string.IsNullOrWhiteSpace(request.NextAction)
                ? null
                : request.NextAction.Trim().ToLowerInvariant();
            if (next != null && !CallSheetVocab.NextActions.Contains(next))
                return Fail(CallSheetFailureReasons.InvalidNextAction, "Invalid next action / إجراء تالٍ غير صالح");

            var follow = await _db.MemberFollowUps
                .FirstOrDefaultAsync(f => f.Id == followUpId && f.TenantId == tenantId);
            if (follow == null)
                return Fail(CallSheetFailureReasons.FollowUpNotFound, "Follow-up not found / المتابعة غير موجودة");

            var staff = await ResolveStaffAsync(tenantId, staffUserId);
            if (staff == null)
                return Fail(CallSheetFailureReasons.StaffUserNotFound, "Staff user not found / المستخدم غير موجود");

            var nextAt = ResolveNextAt(next, request.NextActionAtUtc);
            ApplyOutcomeToFollowUp(follow, outcome, next, nextAt, staff.Id);

            _db.CallOutcomes.Add(new CallOutcome
            {
                TenantId = tenantId,
                FollowUpId = follow.Id,
                MemberId = follow.MemberId,
                MembershipId = follow.MembershipId,
                UserId = staff.Id,
                Outcome = outcome,
                Note = Truncate(request.Note, 300),
                NextAction = next,
                NextActionAtUtc = nextAt
            });

            await _db.SaveChangesAsync();
            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recording call outcome for follow-up {FollowUpId}", followUpId);
            return Result<bool>.Failure("Failed to record call outcome / فشل تسجيل نتيجة الاتصال", ex.Message);
        }
    }

    public async Task<Result<bool>> CompleteAsync(Guid followUpId, Guid tenantId, Guid staffUserId, string? note)
    {
        try
        {
            var follow = await _db.MemberFollowUps
                .FirstOrDefaultAsync(f => f.Id == followUpId && f.TenantId == tenantId);
            if (follow == null)
                return Fail(CallSheetFailureReasons.FollowUpNotFound, "Follow-up not found / المتابعة غير موجودة");

            var staff = await ResolveStaffAsync(tenantId, staffUserId);
            if (staff == null)
                return Fail(CallSheetFailureReasons.StaffUserNotFound, "Staff user not found / المستخدم غير موجود");

            follow.Status = "completed";
            follow.CompletedAtUtc = DateTime.UtcNow;
            follow.CompletedByUserId = staff.Id;
            follow.NextAction = "completed";
            if (!string.IsNullOrWhiteSpace(note))
                follow.Notes = Truncate(note, 500);

            _db.CallOutcomes.Add(new CallOutcome
            {
                TenantId = tenantId,
                FollowUpId = follow.Id,
                MemberId = follow.MemberId,
                MembershipId = follow.MembershipId,
                UserId = staff.Id,
                Outcome = "reached",
                Note = Truncate(note, 300),
                NextAction = "completed"
            });

            await _db.SaveChangesAsync();
            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error completing follow-up {FollowUpId}", followUpId);
            return Result<bool>.Failure("Failed to complete follow-up / فشل إكمال المتابعة", ex.Message);
        }
    }

    public async Task<Result<List<CallSheetEntryDto>>> GetExpiringAsync(Guid tenantId, int days)
    {
        try
        {
            var today = MembershipOperational.TodayCairo();
            var fromDate = today.AddDays(-RenewalLookbackDays);
            var toDate = today.AddDays(days <= 0 ? RenewalLookaheadDays : days);

            var memberships = await _db.Memberships
                .Include(m => m.Member)
                .Include(m => m.Plan)
                .Where(m => m.TenantId == tenantId
                         && m.EndDate >= fromDate && m.EndDate <= toDate
                         && (m.Status == "active" || m.Status == "expired" || m.Status == "frozen"))
                .OrderBy(m => m.EndDate)
                .ToListAsync();

            if (memberships.Count == 0)
                return Result<List<CallSheetEntryDto>>.Success(new List<CallSheetEntryDto>());

            var memberIds = memberships.Select(m => m.MemberId).Distinct().ToList();
            var membershipIds = memberships.Select(m => m.Id).ToList();

            var lastVisits = await _db.GymAttendances
                .Where(a => a.TenantId == tenantId && memberIds.Contains(a.MemberId))
                .GroupBy(a => a.MemberId)
                .Select(g => new { MemberId = g.Key, LastVisitAt = g.Max(a => a.CheckInAtUtc) })
                .ToDictionaryAsync(g => g.MemberId, g => g.LastVisitAt);

            var lastOutcomeByMembership = (await _db.CallOutcomes
                    .Where(c => c.TenantId == tenantId && c.MembershipId != null && membershipIds.Contains(c.MembershipId.Value))
                    .ToListAsync())
                .GroupBy(c => c.MembershipId!.Value)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(c => c.CreatedAtUtc).First().Outcome);

            var entries = memberships.Select(m => new CallSheetEntryDto
            {
                MembershipId = m.Id,
                MemberId = m.MemberId,
                FullName = m.Member?.FullName ?? string.Empty,
                PhoneNumber = m.Member?.PhoneNumber ?? string.Empty,
                PlanName = m.Plan?.Name ?? string.Empty,
                EndDate = m.EndDate,
                LastVisitAt = lastVisits.TryGetValue(m.MemberId, out var lastVisit) ? lastVisit : null,
                LastCallOutcome = lastOutcomeByMembership.TryGetValue(m.Id, out var outcome) ? outcome : null
            }).ToList();

            return Result<List<CallSheetEntryDto>>.Success(entries);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving call sheet for tenant {TenantId}", tenantId);
            return Result<List<CallSheetEntryDto>>.Failure("Failed to retrieve call sheet / فشل جلب قائمة الاتصال", ex.Message);
        }
    }

    public async Task<Result<List<RenewalRateDto>>> GetRenewalRateAsync(
        Guid tenantId, DateOnly from, DateOnly to, Guid? staffUserId)
    {
        try
        {
            var (fromUtc, toUtcExcl) = MembershipOperational.CairoInclusiveRangeUtc(from, to);

            var query = _db.CallOutcomes
                .Include(c => c.User)
                .Where(c => c.TenantId == tenantId && c.CreatedAtUtc >= fromUtc && c.CreatedAtUtc < toUtcExcl);

            if (staffUserId.HasValue)
                query = query.Where(c => c.UserId == staffUserId.Value);

            var outcomes = await query.ToListAsync();

            var result = outcomes
                .GroupBy(c => c.UserId)
                .Select(g =>
                {
                    var totalCalled = g.Count();
                    var renewed = g.Count(c => c.Outcome == "renewed");
                    var staffName = g.First().User is { } user ? $"{user.FirstName} {user.LastName}" : string.Empty;

                    return new RenewalRateDto
                    {
                        StaffUserId = g.Key,
                        StaffName = staffName,
                        TotalCalled = totalCalled,
                        Renewed = renewed,
                        RenewalRatePercent = totalCalled == 0 ? 0m : Math.Round((decimal)renewed / totalCalled * 100, 2)
                    };
                })
                .OrderByDescending(r => r.RenewalRatePercent)
                .ToList();

            return Result<List<RenewalRateDto>>.Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error computing renewal rate for tenant {TenantId}", tenantId);
            return Result<List<RenewalRateDto>>.Failure("Failed to compute renewal rate / فشل حساب معدل التجديد", ex.Message);
        }
    }

    private async Task SyncSystemFollowUpsAsync(Guid tenantId)
    {
        var today = MembershipOperational.TodayCairo();
        var needed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var existing = await _db.MemberFollowUps
            .Where(f => f.TenantId == tenantId && f.Source == CallSheetVocab.SourceSystem)
            .ToListAsync();
        var byKey = existing
            .GroupBy(f => f.SourceKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.CreatedAtUtc).First(), StringComparer.OrdinalIgnoreCase);

        await SyncRenewalsAsync(tenantId, today, needed, byKey);
        await SyncTrialsAsync(tenantId, today, needed, byKey);
        await SyncPaymentsAsync(tenantId, needed, byKey);
        await SyncWelcomeAsync(tenantId, today, needed, byKey);
        await SyncInactiveAsync(tenantId, today, needed, byKey);

        foreach (var row in existing.Where(f => CallSheetVocab.IsOpen(f.Status)))
        {
            if (needed.Contains(row.SourceKey)) continue;
            row.Status = "cancelled";
            row.CompletedAtUtc = DateTime.UtcNow;
            row.CompletedByUserId = null;
            row.NextAction = "completed";
        }

        await _db.SaveChangesAsync();
    }

    private async Task SyncRenewalsAsync(Guid tenantId, DateOnly today, HashSet<string> needed, Dictionary<string, MemberFollowUp> byKey)
    {
        var fromDate = today.AddDays(-RenewalLookbackDays);
        var toDate = today.AddDays(RenewalLookaheadDays);

        var memberships = await _db.Memberships
            .Include(m => m.Plan)
            .Where(m => m.TenantId == tenantId
                     && m.EndDate >= fromDate && m.EndDate <= toDate
                     && (m.Status == "active" || m.Status == "expired" || m.Status == "frozen"))
            .ToListAsync();

        var memberIds = memberships.Select(m => m.MemberId).Distinct().ToList();
        var allForMembers = await _db.Memberships
            .Where(m => m.TenantId == tenantId && memberIds.Contains(m.MemberId))
            .ToListAsync();
        var byMember = allForMembers.GroupBy(m => m.MemberId).ToDictionary(g => g.Key, g => g.ToList());

        foreach (var m in memberships)
        {
            var covering = byMember.TryGetValue(m.MemberId, out var list)
                ? MembershipOperational.SelectCoveringToday(list, today)
                : null;

            if (covering != null && covering.Id != m.Id)
            {
                if (covering.EndDate > toDate)
                    continue;
            }

            var effective = MembershipOperational.GetEffectiveStatus(m, today);
            if (effective is "cancelled" or "pending" or "scheduled")
                continue;

            var days = m.EndDate.DayNumber - today.DayNumber;
            var why = days < 0
                ? $"Membership expired {Math.Abs(days)} day{(Math.Abs(days) == 1 ? "" : "s")} ago"
                : days == 0
                    ? "Membership expires today"
                    : $"Membership expires in {days} day{(days == 1 ? "" : "s")}";
            var priority = days <= 2 ? "high" : "medium";
            var key = $"renewal:{m.Id:N}";
            needed.Add(key);
            UpsertSystem(tenantId, m.MemberId, m.Id, "renewal", key, priority, why, "membership", m.Id, byKey);
        }
    }

    private async Task SyncTrialsAsync(Guid tenantId, DateOnly today, HashSet<string> needed, Dictionary<string, MemberFollowUp> byKey)
    {
        var fromDate = today.AddDays(-TrialWindowDays);
        var toDate = today.AddDays(TrialWindowDays);

        var trials = await _db.GymMembers
            .Where(m => m.TenantId == tenantId && m.IsTrial && m.TrialOutcome == "active_trial" && m.IsActive)
            .ToListAsync();
        if (trials.Count == 0) return;

        var ids = trials.Select(t => t.Id).ToList();
        var memberships = await _db.Memberships
            .Include(m => m.Plan)
            .Where(m => m.TenantId == tenantId && ids.Contains(m.MemberId))
            .ToListAsync();

        foreach (var member in trials)
        {
            var covering = MembershipOperational.SelectCoveringToday(
                memberships.Where(x => x.MemberId == member.Id), today);
            var operational = covering ?? MembershipOperational.SelectOperational(
                memberships.Where(x => x.MemberId == member.Id), today);
            if (operational == null) continue;
            if (operational.EndDate < fromDate || operational.EndDate > toDate) continue;

            var days = operational.EndDate.DayNumber - today.DayNumber;
            var why = days < 0 ? "Trial ended recently" : days == 0 ? "Trial ends today" : $"Trial ends in {days} days";
            var key = $"trial:{member.Id:N}";
            needed.Add(key);
            UpsertSystem(tenantId, member.Id, operational.Id, "trial", key,
                days <= 0 ? "high" : "medium", why, "membership", operational.Id, byKey);
        }
    }

    private async Task SyncPaymentsAsync(Guid tenantId, HashSet<string> needed, Dictionary<string, MemberFollowUp> byKey)
    {
        var sales = await _db.Sales
            .Where(s => s.TenantId == tenantId && s.MemberId != null
                     && s.Status == "partially_paid" && s.AmountDue > 0)
            .ToListAsync();

        foreach (var sale in sales)
        {
            var key = $"payment:{sale.Id:N}";
            needed.Add(key);
            var why = $"Outstanding EGP {sale.AmountDue:0.##}";
            UpsertSystem(tenantId, sale.MemberId!.Value, null, "payment", key, "high", why, "sale", sale.Id, byKey);
        }
    }

    private async Task SyncWelcomeAsync(Guid tenantId, DateOnly today, HashSet<string> needed, Dictionary<string, MemberFollowUp> byKey)
    {
        var fromDate = today.AddDays(-(WelcomeDays - 1));
        var memberships = await _db.Memberships
            .Include(m => m.Plan)
            .Where(m => m.TenantId == tenantId
                     && m.StartDate >= fromDate && m.StartDate <= today
                     && (m.Status == "active" || m.Status == "frozen"))
            .ToListAsync();

        foreach (var m in memberships)
        {
            if (!MembershipOperational.IsCoveringToday(m, today)) continue;
            if (string.Equals(m.Plan?.PlanType, "trial", StringComparison.OrdinalIgnoreCase)) continue;

            var key = $"welcome:{m.Id:N}";
            needed.Add(key);
            var daysAgo = today.DayNumber - m.StartDate.DayNumber;
            var why = daysAgo <= 0 ? "Joined today — welcome" : $"Joined {daysAgo} day{(daysAgo == 1 ? "" : "s")} ago — welcome";
            UpsertSystem(tenantId, m.MemberId, m.Id, "welcome", key, "low", why, "membership", m.Id, byKey);
        }
    }

    private async Task SyncInactiveAsync(Guid tenantId, DateOnly today, HashSet<string> needed, Dictionary<string, MemberFollowUp> byKey)
    {
        var covering = await _db.Memberships
            .Include(m => m.Plan)
            .Where(m => m.TenantId == tenantId && (m.Status == "active" || m.Status == "frozen")
                     && m.StartDate <= today && m.EndDate >= today)
            .ToListAsync();

        covering = covering.Where(m => MembershipOperational.IsCoveringToday(m, today)
            && !string.Equals(m.Plan?.PlanType, "trial", StringComparison.OrdinalIgnoreCase)).ToList();
        if (covering.Count == 0) return;

        var memberIds = covering.Select(m => m.MemberId).Distinct().ToList();
        var lastVisits = await _db.GymAttendances
            .Where(a => a.TenantId == tenantId && memberIds.Contains(a.MemberId))
            .GroupBy(a => a.MemberId)
            .Select(g => new { MemberId = g.Key, Last = g.Max(a => a.CheckInAtUtc) })
            .ToDictionaryAsync(x => x.MemberId, x => x.Last);

        var cutoffUtc = MembershipOperational.CairoInclusiveRangeUtc(today.AddDays(-InactiveDays), today.AddDays(-InactiveDays)).UtcStart;

        foreach (var group in covering.GroupBy(m => m.MemberId))
        {
            var membership = MembershipOperational.SelectCoveringToday(group, today);
            if (membership == null) continue;
            if (membership.StartDate > today.AddDays(-InactiveDays)) continue;

            lastVisits.TryGetValue(group.Key, out var last);
            if (last != default && last >= cutoffUtc) continue;

            string why;
            if (last == default)
                why = $"No visit in {InactiveDays}+ days";
            else
            {
                var lastCairo = MembershipOperational.ToCairoDate(last);
                var days = today.DayNumber - lastCairo.DayNumber;
                why = $"No visit in {days} days";
            }

            var key = $"inactive:{group.Key:N}";
            needed.Add(key);
            UpsertSystem(tenantId, group.Key, membership.Id, "inactive", key, "medium", why, "membership", membership.Id, byKey);
        }
    }

    private void UpsertSystem(
        Guid tenantId, Guid memberId, Guid? membershipId, string reason, string sourceKey,
        string priority, string why, string relatedType, Guid relatedId,
        Dictionary<string, MemberFollowUp> byKey)
    {
        if (byKey.TryGetValue(sourceKey, out var existing))
        {
            if (CallSheetVocab.IsOpen(existing.Status))
            {
                existing.Why = why;
                existing.RelatedType = relatedType;
                existing.RelatedId = relatedId;
                existing.MembershipId = membershipId ?? existing.MembershipId;
                if (existing.Status == "pending")
                    existing.Priority = priority;
                return;
            }

            if (existing.Status == "completed")
                return;

            if (existing.Status == "cancelled" && existing.CompletedByUserId == null)
            {
                existing.Status = "pending";
                existing.Priority = priority;
                existing.Why = why;
                existing.DueAtUtc = TodayTenUtc();
                existing.CompletedAtUtc = null;
                existing.NextAction = null;
                existing.NextActionAtUtc = null;
                existing.RelatedType = relatedType;
                existing.RelatedId = relatedId;
                existing.MembershipId = membershipId ?? existing.MembershipId;
                return;
            }

            return;
        }

        var created = new MemberFollowUp
        {
            TenantId = tenantId,
            MemberId = memberId,
            MembershipId = membershipId,
            Reason = reason,
            Source = CallSheetVocab.SourceSystem,
            SourceKey = sourceKey,
            Priority = priority,
            Status = "pending",
            DueAtUtc = TodayTenUtc(),
            Why = why,
            RelatedType = relatedType,
            RelatedId = relatedId
        };
        _db.MemberFollowUps.Add(created);
        byKey[sourceKey] = created;
    }

    private async Task<List<FollowUpDto>> LoadFollowUpsAsync(Guid tenantId)
    {
        var rows = await _db.MemberFollowUps
            .Include(f => f.Member)
            .Include(f => f.AssignedToUser)
            .Where(f => f.TenantId == tenantId)
            .ToListAsync();

        var ids = rows.Select(r => r.Id).ToList();
        var lastByFollow = (await _db.CallOutcomes
                .Where(c => c.TenantId == tenantId && c.FollowUpId != null && ids.Contains(c.FollowUpId.Value))
                .ToListAsync())
            .GroupBy(c => c.FollowUpId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(c => c.CreatedAtUtc).First());

        return rows.Select(r => MapRow(r, lastByFollow.TryGetValue(r.Id, out var last) ? last : null)).ToList();
    }

    private static List<FollowUpDto> ApplyFilters(
        List<FollowUpDto> items, DateOnly today, Guid? currentAppUserId,
        string? date, string? reason, string? priority, string? status, string? assignee, string? q)
    {
        IEnumerable<FollowUpDto> query = items;

        var dateKey = (date ?? "today").Trim().ToLowerInvariant();
        query = dateKey switch
        {
            "tomorrow" => query.Where(i => CairoDate(i.DueAtUtc) == today.AddDays(1) && CallSheetVocab.IsOpen(i.Status)),
            "upcoming" => query.Where(i => CairoDate(i.DueAtUtc) > today && CallSheetVocab.IsOpen(i.Status)),
            "overdue" => query.Where(i => CairoDate(i.DueAtUtc) < today && CallSheetVocab.IsOpen(i.Status)),
            "all" => query.Where(i => i.Status != "cancelled"),
            _ => query.Where(i =>
                (CallSheetVocab.IsOpen(i.Status) && CairoDate(i.DueAtUtc) <= today)
                || (i.Status == "completed" && i.CompletedAtUtc != null && CairoDate(i.CompletedAtUtc.Value) == today))
        };

        if (!string.IsNullOrWhiteSpace(reason))
            query = query.Where(i => i.Reason == reason.Trim().ToLowerInvariant());
        if (!string.IsNullOrWhiteSpace(priority))
            query = query.Where(i => i.Priority == priority.Trim().ToLowerInvariant());
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(i => i.Status == status.Trim().ToLowerInvariant());

        var asg = (assignee ?? "").Trim().ToLowerInvariant();
        if (asg == "me" && currentAppUserId.HasValue)
            query = query.Where(i => i.AssignedToUserId == currentAppUserId.Value);
        else if (asg == "unassigned")
            query = query.Where(i => i.AssignedToUserId == null);
        else if (Guid.TryParse(assignee, out var staffId))
            query = query.Where(i => i.AssignedToUserId == staffId);

        var term = (q ?? "").Trim();
        if (term.Length >= 1)
        {
            var t = term.ToLowerInvariant();
            query = query.Where(i =>
                (i.FullName ?? "").ToLowerInvariant().Contains(t)
                || (i.PhoneNumber ?? "").Replace(" ", "").Contains(t.Replace(" ", ""))
                || (i.MemberNumber ?? "").ToLowerInvariant().Contains(t));
        }

        return query
            .OrderBy(i => i.Status == "completed" || i.Status == "cancelled" ? 1 : 0)
            .ThenBy(i => i.Priority == "high" ? 0 : i.Priority == "medium" ? 1 : 2)
            .ThenBy(i => i.DueAtUtc)
            .ThenBy(i => i.FullName)
            .ToList();
    }

    private async Task<FollowUpSummaryDto> BuildSummaryAsync(Guid tenantId, DateOnly today)
    {
        var open = await _db.MemberFollowUps
            .Where(f => f.TenantId == tenantId && CallSheetVocab.OpenStatuses.Contains(f.Status))
            .ToListAsync();

        var (dayStart, dayEnd) = MembershipOperational.CairoInclusiveRangeUtc(today, today);
        var todayOutcomes = await _db.CallOutcomes
            .Where(c => c.TenantId == tenantId && c.CreatedAtUtc >= dayStart && c.CreatedAtUtc < dayEnd)
            .Select(c => c.Outcome)
            .ToListAsync();

        return new FollowUpSummaryDto
        {
            ToCallToday = open.Count(f => MembershipOperational.ToCairoDate(f.DueAtUtc) <= today),
            HighPriority = open.Count(f => f.Priority == "high"),
            Pending = open.Count(f => f.Status == "pending"),
            ContactedToday = todayOutcomes.Count(o => o is "reached" or "contacted" or "will_visit" or "renewed"),
            NoAnswerToday = todayOutcomes.Count(o => o is "no_answer" or "busy"),
            Overdue = open.Count(f => MembershipOperational.ToCairoDate(f.DueAtUtc) < today)
        };
    }

    private async Task<FollowUpDetailDto> MapDetailAsync(MemberFollowUp row, Guid tenantId)
    {
        var history = await _db.CallOutcomes
            .Include(c => c.User)
            .Where(c => c.TenantId == tenantId && (c.FollowUpId == row.Id
                || (row.MembershipId != null && c.MembershipId == row.MembershipId)))
            .OrderByDescending(c => c.CreatedAtUtc)
            .Take(40)
            .ToListAsync();

        var last = history.FirstOrDefault();
        var dto = MapRow(row, last);
        return new FollowUpDetailDto
        {
            Id = dto.Id,
            MemberId = dto.MemberId,
            MembershipId = dto.MembershipId,
            FullName = dto.FullName,
            MemberNumber = dto.MemberNumber,
            PhoneNumber = dto.PhoneNumber,
            ProfilePhotoUrl = dto.ProfilePhotoUrl,
            Reason = dto.Reason,
            Source = dto.Source,
            Priority = dto.Priority,
            Status = dto.Status,
            AssignedToUserId = dto.AssignedToUserId,
            AssignedToName = dto.AssignedToName,
            DueAtUtc = dto.DueAtUtc,
            NextAction = dto.NextAction,
            NextActionAtUtc = dto.NextActionAtUtc,
            Why = dto.Why,
            RelatedType = dto.RelatedType,
            RelatedId = dto.RelatedId,
            LastContactAtUtc = dto.LastContactAtUtc,
            LastOutcome = dto.LastOutcome,
            CreatedAtUtc = dto.CreatedAtUtc,
            CompletedAtUtc = dto.CompletedAtUtc,
            Notes = row.Notes,
            History = history.Select(h => new FollowUpHistoryDto
            {
                Id = h.Id,
                AtUtc = h.CreatedAtUtc,
                Outcome = h.Outcome,
                Note = h.Note,
                NextAction = h.NextAction,
                NextActionAtUtc = h.NextActionAtUtc,
                StaffName = h.User is { } u ? $"{u.FirstName} {u.LastName}".Trim() : null
            }).ToList()
        };
    }

    private async Task<FollowUpDto> MapOneAsync(MemberFollowUp row, Guid tenantId)
    {
        var last = await _db.CallOutcomes
            .Where(c => c.TenantId == tenantId && c.FollowUpId == row.Id)
            .OrderByDescending(c => c.CreatedAtUtc)
            .FirstOrDefaultAsync();
        return MapRow(row, last);
    }

    private static FollowUpDto MapRow(MemberFollowUp row, CallOutcome? last) => new()
    {
        Id = row.Id,
        MemberId = row.MemberId,
        MembershipId = row.MembershipId,
        FullName = row.Member?.FullName ?? string.Empty,
        MemberNumber = row.Member?.MemberNumber ?? string.Empty,
        PhoneNumber = row.Member?.PhoneNumber ?? string.Empty,
        ProfilePhotoUrl = string.IsNullOrWhiteSpace(row.Member?.ProfilePhotoUrl) ? null : row.Member!.ProfilePhotoUrl,
        Reason = row.Reason,
        Source = row.Source,
        Priority = row.Priority,
        Status = row.Status,
        AssignedToUserId = row.AssignedToUserId,
        AssignedToName = row.AssignedToUser is { } u ? $"{u.FirstName} {u.LastName}".Trim() : null,
        DueAtUtc = row.DueAtUtc,
        NextAction = row.NextAction,
        NextActionAtUtc = row.NextActionAtUtc,
        Why = row.Why,
        RelatedType = row.RelatedType,
        RelatedId = row.RelatedId,
        LastContactAtUtc = last?.CreatedAtUtc,
        LastOutcome = last?.Outcome,
        CreatedAtUtc = row.CreatedAtUtc,
        CompletedAtUtc = row.CompletedAtUtc
    };

    private async Task<AppUser?> ResolveStaffAsync(Guid tenantId, Guid staffUserId)
    {
        var asString = staffUserId.ToString();
        return await _db.AppUsers.FirstOrDefaultAsync(u =>
            u.TenantId == tenantId && (u.Id == staffUserId || u.UserId == asString));
    }

    private static void ApplyOutcomeToFollowUp(
        MemberFollowUp follow, string outcome, string? next, DateTime? nextAt, Guid staffId)
    {
        follow.NextAction = next;
        follow.NextActionAtUtc = nextAt;

        var terminalNext = next is "completed" or "member_renewed" or "not_interested" or "wrong_number";
        var terminalOutcome = outcome is "renewed" or "not_interested" or "wrong_number";

        if (terminalNext || (terminalOutcome && next is null or "completed"))
        {
            follow.Status = "completed";
            follow.CompletedAtUtc = DateTime.UtcNow;
            follow.CompletedByUserId = staffId;
            return;
        }

        follow.Status = outcome is "no_answer" or "busy" ? "no_answer" : "contacted";
        follow.CompletedAtUtc = null;
        follow.CompletedByUserId = null;
        if (nextAt.HasValue)
            follow.DueAtUtc = nextAt.Value;
    }

    private static DateTime? ResolveNextAt(string? next, DateTime? requested)
    {
        if (requested.HasValue)
            return DateTime.SpecifyKind(requested.Value, DateTimeKind.Utc);

        var today = MembershipOperational.TodayCairo();
        return next switch
        {
            "call_tomorrow" => CairoTenUtc(today.AddDays(1)),
            "call_in_3_days" => CairoTenUtc(today.AddDays(3)),
            "member_will_visit" => CairoTenUtc(today.AddDays(1)),
            _ => null
        };
    }

    private static DateTime TodayTenUtc() => CairoTenUtc(MembershipOperational.TodayCairo());

    private static DateTime CairoTenUtc(DateOnly day)
    {
        var cairo = DateTime.SpecifyKind(day.ToDateTime(new TimeOnly(10, 0)), DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(cairo, TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time"));
    }

    private static DateOnly CairoDate(DateTime utc) => MembershipOperational.ToCairoDate(utc);

    private static string WhyForReason(string reason) => reason switch
    {
        "renewal" => "Renewal follow-up",
        "trial" => "Trial follow-up",
        "payment" => "Payment follow-up",
        "welcome" => "New member welcome",
        "inactive" => "Inactive member",
        "offer" => "Offer / promotion",
        _ => "Custom follow-up"
    };

    private static string? Truncate(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var t = value.Trim();
        return t.Length <= max ? t : t[..max];
    }

    private static Result<bool> Fail(string code, string message) => Result<bool>.Failure($"{code}|{message}");
}
