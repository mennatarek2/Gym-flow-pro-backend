namespace GMS.Application.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using GMS.Application.Common;
using GMS.Application.DTOs.AccessCards;
using GMS.Application.Interfaces;
using GMS.Core.Constants;
using GMS.Core.Entities;
using GMS.Infrastructure.Persistence;

/// <summary>
/// Physical PVC access-card inventory. Additive to MemberNumber — does not change
/// membership gauntlet or MemberNumber generation.
/// </summary>
public class AccessCardService : IAccessCardService
{
    private const int LowStockThreshold = 10;
    private const int MaxBulkQuantity = 500;

    private readonly GymFlowProDbContext _db;
    private readonly IAuditService _audit;
    private readonly ILogger<AccessCardService> _logger;

    public AccessCardService(
        GymFlowProDbContext db,
        IAuditService audit,
        ILogger<AccessCardService> logger)
    {
        _db = db;
        _audit = audit;
        _logger = logger;
    }

    public async Task<Result<AccessCardInventoryDto>> GetInventoryAsync(Guid tenantId)
    {
        var rows = await _db.AccessCards.AsNoTracking()
            .Where(c => c.TenantId == tenantId)
            .GroupBy(c => c.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync();

        int Count(string status) =>
            rows.FirstOrDefault(r => r.Status.Equals(status, StringComparison.OrdinalIgnoreCase))?.Count ?? 0;

        var available = Count(AccessCardStatuses.Available);
        var dto = new AccessCardInventoryDto
        {
            Available = available,
            Assigned = Count(AccessCardStatuses.Assigned),
            Lost = Count(AccessCardStatuses.Lost),
            Damaged = Count(AccessCardStatuses.Damaged),
            Blocked = Count(AccessCardStatuses.Blocked),
            LowStock = available < LowStockThreshold
        };
        dto.Total = dto.Available + dto.Assigned + dto.Lost + dto.Damaged + dto.Blocked;
        return Result<AccessCardInventoryDto>.Success(dto);
    }

    public async Task<Result<PagedResult<AccessCardDto>>> ListAsync(
        Guid tenantId, string? status, string? search, int page, int pageSize)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _db.AccessCards.AsNoTracking()
            .Include(c => c.Member)
            .Where(c => c.TenantId == tenantId);

        if (!string.IsNullOrWhiteSpace(status) && AccessCardStatuses.IsKnown(status))
            query = query.Where(c => c.Status == NormalizeStatus(status!));

        if (!string.IsNullOrWhiteSpace(search))
        {
            var q = search.Trim();
            query = query.Where(c =>
                c.Code.Contains(q)
                || (c.Member != null && (
                    c.Member.MemberNumber.Contains(q)
                    || c.Member.FullName.Contains(q)
                    || c.Member.FullNameAr.Contains(q))));
        }

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(c => c.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Result<PagedResult<AccessCardDto>>.Success(new PagedResult<AccessCardDto>
        {
            Items = items.Select(Map).ToList(),
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        });
    }

    public async Task<Result<AccessCardDto>> GetByIdAsync(Guid tenantId, Guid cardId)
    {
        var card = await _db.AccessCards.AsNoTracking()
            .Include(c => c.Member)
            .FirstOrDefaultAsync(c => c.Id == cardId && c.TenantId == tenantId);
        if (card == null)
            return Result<AccessCardDto>.Failure("Card not found / الكارنيه غير موجود");
        return Result<AccessCardDto>.Success(Map(card));
    }

    public async Task<Result<AccessCardDto?>> GetAssignedForMemberAsync(Guid tenantId, Guid memberId)
    {
        var card = await _db.AccessCards.AsNoTracking()
            .Include(c => c.Member)
            .FirstOrDefaultAsync(c =>
                c.TenantId == tenantId
                && c.MemberId == memberId
                && c.Status == AccessCardStatuses.Assigned);
        return Result<AccessCardDto?>.Success(card == null ? null : Map(card));
    }

    public async Task<Result<BulkCreateAccessCardsResult>> BulkCreateAsync(
        Guid tenantId, BulkCreateAccessCardsRequest request)
    {
        if (request.Quantity < 1 || request.Quantity > MaxBulkQuantity)
            return Result<BulkCreateAccessCardsResult>.Failure(
                $"Quantity must be 1–{MaxBulkQuantity} / الكمية لازم تكون من 1 إلى {MaxBulkQuantity}");

        var prefix = (request.Prefix ?? "CARD").Trim().ToUpperInvariant();
        if (prefix.Length is < 2 or > 12 || !prefix.All(c => char.IsLetterOrDigit(c) || c == '-'))
            return Result<BulkCreateAccessCardsResult>.Failure(
                "Invalid prefix / بادئة غير صالحة");

        if (request.StartNumber < 1)
            return Result<BulkCreateAccessCardsResult>.Failure(
                "Start number must be >= 1 / رقم البداية لازم يكون ١ أو أكثر");

        var codes = new List<string>(request.Quantity);
        for (var i = 0; i < request.Quantity; i++)
            codes.Add($"{prefix}-{request.StartNumber + i:D4}");

        var existingCardCodes = await _db.AccessCards
            .Where(c => c.TenantId == tenantId && codes.Contains(c.Code))
            .Select(c => c.Code)
            .ToListAsync();
        if (existingCardCodes.Count > 0)
            return Result<BulkCreateAccessCardsResult>.Failure(
                $"Duplicate card code(s): {string.Join(", ", existingCardCodes.Take(5))} / أكواد مكررة");

        // Never mint Available stock that collides with MemberNumber (legacy barcode path).
        var collidingMembers = await _db.GymMembers
            .Where(m => m.TenantId == tenantId && codes.Contains(m.MemberNumber))
            .Select(m => m.MemberNumber)
            .ToListAsync();
        if (collidingMembers.Count > 0)
            return Result<BulkCreateAccessCardsResult>.Failure(
                $"Code(s) collide with MemberNumber: {string.Join(", ", collidingMembers.Take(5))} / يتعارض مع رقم العضو");

        var batchId = Guid.NewGuid().ToString("N");
        var now = DateTime.UtcNow;
        var entities = codes.Select(code => new AccessCard
        {
            TenantId = tenantId,
            Code = code,
            Status = AccessCardStatuses.Available,
            BatchId = batchId,
            CreatedAtUtc = now
        }).ToList();

        _db.AccessCards.AddRange(entities);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Bulk create access cards failed for tenant {TenantId}", tenantId);
            return Result<BulkCreateAccessCardsResult>.Failure(
                "Could not create cards (duplicate?) / تعذر إنشاء الكروت (تكرار؟)");
        }

        await _audit.LogAsync("access_card.bulk_created", "AccessCard", null,
            after: new { batchId, count = entities.Count, prefix, start = request.StartNumber },
            tenantIdOverride: tenantId);

        return Result<BulkCreateAccessCardsResult>.Success(new BulkCreateAccessCardsResult
        {
            BatchId = batchId,
            Created = entities.Count,
            Codes = codes
        });
    }

    public async Task<Result<AccessCardDto>> AssignAsync(Guid tenantId, AssignAccessCardRequest request)
    {
        var member = await _db.GymMembers
            .FirstOrDefaultAsync(m => m.Id == request.MemberId && m.TenantId == tenantId);
        if (member == null)
            return Result<AccessCardDto>.Failure("Member not found / العضو غير موجود");

        var already = await _db.AccessCards.AnyAsync(c =>
            c.TenantId == tenantId
            && c.MemberId == member.Id
            && c.Status == AccessCardStatuses.Assigned);
        if (already)
            return Result<AccessCardDto>.Failure(
                "Member already has an assigned card — use Replace / العضو عنده كارنيه بالفعل — استخدم الاستبدال");

        var card = await ResolveAvailableCardAsync(tenantId, request.CardId, request.Code);
        if (card == null)
            return Result<AccessCardDto>.Failure(
                "Available card not found / الكارنيه المتاح غير موجود");

        var now = DateTime.UtcNow;
        var rows = await TryAssignAvailableAsync(card.Id, tenantId, member.Id, now);
        if (rows == 0)
            return Result<AccessCardDto>.Failure(
                "Card is no longer available / الكارنيه لم يعد متاحاً");

        await _audit.LogAsync("access_card.assigned", "AccessCard", card.Id,
            after: new { card.Code, memberId = member.Id, member.MemberNumber },
            tenantIdOverride: tenantId);

        return await GetByIdAsync(tenantId, card.Id);
    }

    public async Task<Result<AccessCardDto>> ReplaceAsync(Guid tenantId, ReplaceAccessCardRequest request)
    {
        var member = await _db.GymMembers
            .FirstOrDefaultAsync(m => m.Id == request.MemberId && m.TenantId == tenantId);
        if (member == null)
            return Result<AccessCardDto>.Failure("Member not found / العضو غير موجود");

        var oldStatus = NormalizeStatus(string.IsNullOrWhiteSpace(request.OldStatus)
            ? AccessCardStatuses.Lost
            : request.OldStatus);
        if (oldStatus is not (AccessCardStatuses.Lost or AccessCardStatuses.Damaged))
            return Result<AccessCardDto>.Failure(
                "Old card status must be Lost or Damaged / حالة الكارت القديم Lost أو Damaged");

        await using var tx = _db.Database.IsRelational()
            ? await _db.Database.BeginTransactionAsync()
            : null;

        var oldCard = await _db.AccessCards
            .FirstOrDefaultAsync(c =>
                c.TenantId == tenantId
                && c.MemberId == member.Id
                && c.Status == AccessCardStatuses.Assigned);
        if (oldCard == null)
            return Result<AccessCardDto>.Failure(
                "No assigned card to replace / لا يوجد كارنيه مُعيَّن للاستبدال");

        var newCard = await ResolveAvailableCardAsync(tenantId, request.NewCardId, request.NewCode);
        if (newCard == null)
            return Result<AccessCardDto>.Failure(
                "Available replacement card not found / كارنيه الاستبدال غير موجود");

        var now = DateTime.UtcNow;
        oldCard.Status = oldStatus;
        oldCard.LostAtUtc = now;
        oldCard.Reason = string.IsNullOrWhiteSpace(request.Reason)
            ? $"Replaced by {newCard.Code}"
            : request.Reason.Trim();
        oldCard.UpdatedAtUtc = now;
        // Keep MemberId on old card for history; unique Assigned index allows this.

        await _db.SaveChangesAsync();

        var rows = await TryAssignAvailableAsync(newCard.Id, tenantId, member.Id, now);
        if (rows == 0)
        {
            if (tx != null)
                await tx.RollbackAsync();
            return Result<AccessCardDto>.Failure(
                "Replacement card is no longer available / كارنيه الاستبدال لم يعد متاحاً");
        }

        if (tx != null)
            await tx.CommitAsync();

        await _audit.LogAsync("access_card.replaced", "AccessCard", newCard.Id,
            before: new { oldCardId = oldCard.Id, oldCode = oldCard.Code, oldStatus },
            after: new { newCardId = newCard.Id, newCode = newCard.Code, memberId = member.Id },
            tenantIdOverride: tenantId);

        return await GetByIdAsync(tenantId, newCard.Id);
    }

    public Task<Result<AccessCardDto>> MarkLostAsync(
        Guid tenantId, Guid cardId, MarkAccessCardRequest request) =>
        MarkTerminalAsync(tenantId, cardId, AccessCardStatuses.Lost, request.Reason, "access_card.lost");

    public Task<Result<AccessCardDto>> MarkDamagedAsync(
        Guid tenantId, Guid cardId, MarkAccessCardRequest request) =>
        MarkTerminalAsync(tenantId, cardId, AccessCardStatuses.Damaged, request.Reason, "access_card.damaged");

    public Task<Result<AccessCardDto>> BlockAsync(
        Guid tenantId, Guid cardId, MarkAccessCardRequest request) =>
        MarkTerminalAsync(tenantId, cardId, AccessCardStatuses.Blocked, request.Reason, "access_card.blocked");

    public async Task<Result<AccessCardDto>> UnassignAsync(
        Guid tenantId, Guid cardId, UnassignAccessCardRequest request)
    {
        var card = await _db.AccessCards
            .FirstOrDefaultAsync(c => c.Id == cardId && c.TenantId == tenantId);
        if (card == null)
            return Result<AccessCardDto>.Failure("Card not found / الكارنيه غير موجود");
        if (card.Status != AccessCardStatuses.Assigned)
            return Result<AccessCardDto>.Failure(
                "Only Assigned cards can be unassigned / فقط الكروت المعيَّنة يمكن إلغاء تعيينها");

        var before = new { card.Code, card.MemberId, card.Status };
        card.Status = AccessCardStatuses.Available;
        card.MemberId = null;
        card.AssignedAtUtc = null;
        card.Reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
        card.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await _audit.LogAsync("access_card.unassigned", "AccessCard", card.Id,
            before: before,
            after: new { card.Code, card.Status },
            tenantIdOverride: tenantId);

        return await GetByIdAsync(tenantId, card.Id);
    }

    private async Task<Result<AccessCardDto>> MarkTerminalAsync(
        Guid tenantId, Guid cardId, string status, string? reason, string auditAction)
    {
        var card = await _db.AccessCards
            .FirstOrDefaultAsync(c => c.Id == cardId && c.TenantId == tenantId);
        if (card == null)
            return Result<AccessCardDto>.Failure("Card not found / الكارنيه غير موجود");
        if (card.Status is AccessCardStatuses.Lost or AccessCardStatuses.Damaged or AccessCardStatuses.Blocked)
            return Result<AccessCardDto>.Failure(
                "Card is already terminal / الكارنيه بالفعل في حالة نهائية");
        if (card.Status == AccessCardStatuses.Available && status != AccessCardStatuses.Blocked)
            return Result<AccessCardDto>.Failure(
                "Available cards cannot be marked lost/damaged — use Block / الكارت المتاح لا يُعلَّم مفقود/تالف");

        var before = new { card.Status, card.MemberId };
        var now = DateTime.UtcNow;
        card.Status = status;
        card.LostAtUtc = now;
        card.Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        card.UpdatedAtUtc = now;
        await _db.SaveChangesAsync();

        await _audit.LogAsync(auditAction, "AccessCard", card.Id,
            before: before,
            after: new { card.Status, card.Reason },
            tenantIdOverride: tenantId);

        return await GetByIdAsync(tenantId, card.Id);
    }

    private async Task<AccessCard?> ResolveAvailableCardAsync(
        Guid tenantId, Guid? cardId, string? code)
    {
        if (cardId.HasValue && cardId.Value != Guid.Empty)
        {
            return await _db.AccessCards.FirstOrDefaultAsync(c =>
                c.Id == cardId.Value
                && c.TenantId == tenantId
                && c.Status == AccessCardStatuses.Available);
        }

        var trimmed = (code ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(trimmed))
            return null;

        return await _db.AccessCards.FirstOrDefaultAsync(c =>
            c.TenantId == tenantId
            && c.Code == trimmed
            && c.Status == AccessCardStatuses.Available);
    }

    /// <summary>
    /// Conditional assign — one winner under concurrent receptionists.
    /// Uses ExecuteUpdate when relational; otherwise tracked update for InMemory tests.
    /// </summary>
    private async Task<int> TryAssignAvailableAsync(
        Guid cardId, Guid tenantId, Guid memberId, DateTime assignedAtUtc)
    {
        if (_db.Database.IsRelational())
        {
            return await _db.AccessCards
                .Where(c =>
                    c.Id == cardId
                    && c.TenantId == tenantId
                    && c.Status == AccessCardStatuses.Available
                    && !c.IsDeleted)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(c => c.Status, AccessCardStatuses.Assigned)
                    .SetProperty(c => c.MemberId, memberId)
                    .SetProperty(c => c.AssignedAtUtc, assignedAtUtc)
                    .SetProperty(c => c.UpdatedAtUtc, assignedAtUtc)
                    .SetProperty(c => c.Reason, (string?)null)
                    .SetProperty(c => c.LostAtUtc, (DateTime?)null));
        }

        var card = await _db.AccessCards.FirstOrDefaultAsync(c =>
            c.Id == cardId
            && c.TenantId == tenantId
            && c.Status == AccessCardStatuses.Available);
        if (card == null)
            return 0;

        card.Status = AccessCardStatuses.Assigned;
        card.MemberId = memberId;
        card.AssignedAtUtc = assignedAtUtc;
        card.UpdatedAtUtc = assignedAtUtc;
        card.Reason = null;
        card.LostAtUtc = null;
        await _db.SaveChangesAsync();
        return 1;
    }

    private static string NormalizeStatus(string status)
    {
        foreach (var s in AccessCardStatuses.All)
        {
            if (s.Equals(status, StringComparison.OrdinalIgnoreCase))
                return s;
        }
        return status.Trim();
    }

    private static AccessCardDto Map(AccessCard c) => new()
    {
        Id = c.Id,
        Code = c.Code,
        Status = c.Status,
        MemberId = c.MemberId,
        MemberNumber = c.Member?.MemberNumber,
        MemberName = c.Member?.FullName,
        AssignedAtUtc = c.AssignedAtUtc,
        LostAtUtc = c.LostAtUtc,
        Reason = c.Reason,
        BatchId = c.BatchId,
        CreatedAtUtc = c.CreatedAtUtc
    };
}
