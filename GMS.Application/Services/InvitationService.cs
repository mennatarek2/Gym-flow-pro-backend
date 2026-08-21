namespace GMS.Application.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using GMS.Application.Common;
using GMS.Application.DTOs.Invitation;
using GMS.Application.Interfaces;
using GMS.Application.Utilities;
using GMS.Core.Constants;
using GMS.Core.Entities;
using GMS.Core.Interfaces;
using GMS.Core.Utilities;
using GMS.Infrastructure.Persistence;

/// <summary>
/// Invitation product: consume 1 quota on create against the covering membership.
/// Backend is the source of truth for quota, tenant, and member ownership.
/// </summary>
public class InvitationService : IInvitationService
{
    private readonly GymFlowProDbContext _dbContext;
    private readonly IEncryptionService _encryption;
    private readonly IAuditService _auditService;
    private readonly ILogger<InvitationService> _logger;

    public InvitationService(
        GymFlowProDbContext dbContext,
        IEncryptionService encryption,
        IAuditService auditService,
        ILogger<InvitationService> logger)
    {
        _dbContext = dbContext;
        _encryption = encryption;
        _auditService = auditService;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<Result<SendInvitationResponse>> SendInvitationAsync(
        SendInvitationRequest request, Guid identityUserId, Guid tenantId)
    {
        var member = await ResolveGymMemberAsync(identityUserId, tenantId);
        if (member == null)
            return Result<SendInvitationResponse>.Failure(
                "العضو غير موجود / Member not found");

        return await CreateInvitationForMemberAsync(request, member, tenantId, staffCreated: false);
    }

    /// <inheritdoc/>
    public async Task<Result<SendInvitationResponse>> SendInvitationForMemberAsync(
        SendInvitationRequest request, Guid gymMemberId, Guid tenantId)
    {
        var member = await _dbContext.GymMembers
            .FirstOrDefaultAsync(m => m.Id == gymMemberId && m.TenantId == tenantId);
        if (member == null)
            return Result<SendInvitationResponse>.Failure(
                "العضو غير موجود / Member not found");

        return await CreateInvitationForMemberAsync(request, member, tenantId, staffCreated: true);
    }

    private async Task<Result<SendInvitationResponse>> CreateInvitationForMemberAsync(
        SendInvitationRequest request, GymMember member, Guid tenantId, bool staffCreated)
    {
        var normalizedPhone = PhoneNormalizer.Normalize(request.ResolvedPhone);
        if (normalizedPhone == null)
            return Result<SendInvitationResponse>.Failure(
                "رقم الهاتف غير صالح / Invalid phone number");

        var alreadyMember = await _dbContext.GymMembers
            .AsNoTracking()
            .AnyAsync(m => m.TenantId == tenantId && m.PhoneNumber == normalizedPhone);
        if (alreadyMember)
            return Result<SendInvitationResponse>.Failure(
                "هذا الشخص عضو بالفعل / This person is already a member.");

        var existing = await _dbContext.MemberInvitations
            .Include(i => i.InvitingMember)
            .Where(i => i.TenantId == tenantId
                     && i.InvitationType == InvitationTypes.Invitation
                     && i.GuestPhoneNumber == normalizedPhone)
            .OrderBy(i => i.CreatedAtUtc)
            .FirstOrDefaultAsync();

        var today = MembershipOperational.TodayCairo();
        var covering = await LoadCoveringMembershipAsync(member.Id, tenantId, today);
        var quota = await BuildQuotaDtoAsync(member.Id, tenantId, covering);

        if (existing != null)
        {
            return Result<SendInvitationResponse>.Success(ToSendResponse(
                existing, quota, alreadyExisted: true,
                "Invitation already exists",
                "هذه الدعوة موجودة بالفعل"));
        }

        if (!IsInviteEligible(covering, today))
            return Result<SendInvitationResponse>.Failure(
                "لا توجد عضوية نشطة / No active membership");

        var total = covering!.Plan!.ReferralInviteQuota;
        if (total <= 0 || quota.Remaining <= 0)
            return Result<SendInvitationResponse>.Failure(
                "لقد استخدمت كل الدعوات في عضويتك الحالية / You've used all Invitations included in your current Membership.");

        var nidPlain = string.IsNullOrWhiteSpace(request.NationalId)
            ? null
            : request.NationalId.Trim();
        var notes = string.IsNullOrWhiteSpace(request.Notes)
            ? null
            : request.Notes.Trim();

        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction = null;
        if (_dbContext.Database.IsRelational())
            transaction = await _dbContext.Database.BeginTransactionAsync();

        try
        {
            var usedNow = await CountUsedAsync(member.Id, tenantId, covering.Id);
            if (usedNow >= total)
            {
                if (transaction != null)
                    await transaction.RollbackAsync();
                return Result<SendInvitationResponse>.Failure(
                    "لقد استخدمت كل الدعوات في عضويتك الحالية / You've used all Invitations included in your current Membership.");
            }

            var now = DateTime.UtcNow;
            var invitation = new MemberInvitation
            {
                TenantId = tenantId,
                InvitingMemberId = member.Id,
                CoveringMembershipId = covering.Id,
                InvitationType = InvitationTypes.Invitation,
                GuestName = request.ResolvedName,
                GuestPhoneNumber = normalizedPhone,
                NationalIdEncrypted = nidPlain == null ? null : _encryption.Encrypt(nidPlain),
                Notes = notes,
                Status = InvitationStatuses.New,
                QuotaPeriod = string.Empty,
                SentAtUtc = now,
                CreatedAtUtc = now
            };

            await _dbContext.MemberInvitations.AddAsync(invitation);
            await _dbContext.SaveChangesAsync();

            if (transaction != null)
                await transaction.CommitAsync();

            invitation.InvitingMember = member;
            var usedAfter = usedNow + 1;
            var remainingAfter = Math.Max(0, total - usedAfter);
            var afterQuota = new InvitationQuotaDto
            {
                MemberId = member.Id,
                MembershipId = covering.Id,
                PlanId = covering.PlanId,
                PlanName = covering.Plan.Name,
                Total = total,
                Used = usedAfter,
                Remaining = remainingAfter,
                MembershipStatus = "active"
            };

            await _auditService.LogAsync(
                "invitation.create",
                "MemberInvitation",
                invitation.Id,
                null,
                new
                {
                    invitation.GuestName,
                    invitation.GuestPhoneNumber,
                    invitation.InvitingMemberId,
                    covering.Id,
                    QuotaUsed = usedAfter,
                    QuotaRemaining = remainingAfter,
                    Source = staffCreated ? "staff" : "member"
                });

            return Result<SendInvitationResponse>.Success(ToSendResponse(
                invitation, afterQuota, alreadyExisted: false,
                "Invitation sent",
                "تم إرسال الدعوة"));
        }
        catch (Exception ex)
        {
            if (transaction != null)
                await transaction.RollbackAsync();

            var raced = await _dbContext.MemberInvitations
                .Include(i => i.InvitingMember)
                .Where(i => i.TenantId == tenantId
                         && i.InvitationType == InvitationTypes.Invitation
                         && i.GuestPhoneNumber == normalizedPhone)
                .OrderBy(i => i.CreatedAtUtc)
                .FirstOrDefaultAsync();
            if (raced != null)
            {
                var q = await BuildQuotaDtoAsync(member.Id, tenantId, covering);
                return Result<SendInvitationResponse>.Success(ToSendResponse(
                    raced, q, alreadyExisted: true,
                    "Invitation already exists",
                    "هذه الدعوة موجودة بالفعل"));
            }

            _logger.LogError(ex, "Error creating invitation for member {MemberId} (staff={StaffCreated})",
                member.Id, staffCreated);
            return Result<SendInvitationResponse>.Failure(
                "حدث خطأ أثناء إرسال الدعوة / An error occurred while sending the invitation");
        }
        finally
        {
            if (transaction != null)
                await transaction.DisposeAsync();
        }
    }

    /// <inheritdoc/>
    public async Task<Result<List<InvitationHistoryResponse>>> GetMemberInvitationsAsync(
        Guid identityUserId, Guid tenantId)
    {
        var member = await ResolveGymMemberAsync(identityUserId, tenantId);
        if (member == null)
            return Result<List<InvitationHistoryResponse>>.Failure(
                "العضو غير موجود / Member not found");

        var rows = await LoadInvitationsQuery(tenantId)
            .Where(i => i.InvitingMemberId == member.Id)
            .OrderByDescending(i => i.CreatedAtUtc)
            .ToListAsync();

        return Result<List<InvitationHistoryResponse>>.Success(
            rows.Select(MapItem).ToList());
    }

    /// <inheritdoc/>
    public async Task<Result<InvitationQuotaDto>> GetMyInvitationSummaryAsync(
        Guid identityUserId, Guid tenantId)
    {
        var member = await ResolveGymMemberAsync(identityUserId, tenantId);
        if (member == null)
            return Result<InvitationQuotaDto>.Failure(
                "العضو غير موجود / Member not found");

        var today = MembershipOperational.TodayCairo();
        var covering = await LoadCoveringMembershipAsync(member.Id, tenantId, today);
        var dto = await BuildQuotaDtoAsync(member.Id, tenantId, covering);
        return Result<InvitationQuotaDto>.Success(dto);
    }

    /// <inheritdoc/>
    public async Task<Result<InvitationMemberSummaryDto>> GetMemberInvitation360Async(
        Guid gymMemberId, Guid tenantId)
    {
        var member = await _dbContext.GymMembers
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == gymMemberId && m.TenantId == tenantId);
        if (member == null)
            return Result<InvitationMemberSummaryDto>.Failure(
                "العضو غير موجود / Member not found");

        var today = MembershipOperational.TodayCairo();
        var covering = await LoadCoveringMembershipAsync(gymMemberId, tenantId, today);
        var quota = await BuildQuotaDtoAsync(gymMemberId, tenantId, covering);

        var rows = await LoadInvitationsQuery(tenantId)
            .Where(i => i.InvitingMemberId == gymMemberId)
            .OrderByDescending(i => i.CreatedAtUtc)
            .ToListAsync();

        return Result<InvitationMemberSummaryDto>.Success(Build360(quota, rows.Select(MapItem).ToList()));
    }

    /// <inheritdoc/>
    public async Task<Result<List<InvitationHistoryResponse>>> GetStaffInvitationsAsync(
        Guid tenantId, string? status, string? query)
    {
        var q = (query ?? string.Empty).Trim();
        var statusKey = (status ?? string.Empty).Trim().ToLowerInvariant();
        var phoneGuess = q.Length >= 2 ? PhoneNormalizer.Normalize(q) : null;

        var rowsQuery = LoadInvitationsQuery(tenantId);

        if (InvitationStatuses.IsValid(statusKey))
            rowsQuery = rowsQuery.Where(i => i.Status == statusKey);

        if (q.Length >= 2)
        {
            rowsQuery = rowsQuery.Where(i =>
                i.GuestName.Contains(q)
                || i.GuestPhoneNumber.Contains(q)
                || (i.InvitingMember != null && i.InvitingMember.FullName.Contains(q))
                || (phoneGuess != null && i.GuestPhoneNumber == phoneGuess));
        }

        var rows = await rowsQuery
            .OrderByDescending(i => i.CreatedAtUtc)
            .Take(300)
            .ToListAsync();

        return Result<List<InvitationHistoryResponse>>.Success(rows.Select(MapItem).ToList());
    }

    /// <inheritdoc/>
    public async Task<Result<InvitationHistoryResponse>> UpdateInvitationStatusAsync(
        Guid invitationId, Guid tenantId, string status)
    {
        var next = (status ?? string.Empty).Trim().ToLowerInvariant();
        if (!InvitationStatuses.IsValid(next))
            return Result<InvitationHistoryResponse>.Failure(
                "حالة غير صالحة / Invalid status");

        var invitation = await LoadInvitationsQuery(tenantId)
            .FirstOrDefaultAsync(i => i.Id == invitationId && i.TenantId == tenantId);

        if (invitation == null)
            return Result<InvitationHistoryResponse>.Failure(
                "الدعوة غير موجودة / Invitation not found");

        var now = DateTime.UtcNow;
        invitation.Status = next;
        invitation.UpdatedAtUtc = now;
        if (next == InvitationStatuses.Contacted && invitation.ContactedAtUtc == null)
            invitation.ContactedAtUtc = now;
        if (next == InvitationStatuses.Converted && invitation.ConvertedAtUtc == null)
            invitation.ConvertedAtUtc = now;

        await _dbContext.SaveChangesAsync();

        await _auditService.LogAsync(
            "invitation.status",
            "MemberInvitation",
            invitation.Id,
            null,
            new { Status = next });

        return Result<InvitationHistoryResponse>.Success(MapItem(invitation));
    }

    /// <inheritdoc/>
    public async Task<int> ExpireOverdueGuestPassesAsync(CancellationToken ct = default)
    {
        var today = MembershipOperational.TodayCairo();
        var now = DateTime.UtcNow;

        var overdue = await _dbContext.MemberInvitations
            .IgnoreQueryFilters()
            .Where(i => !i.IsDeleted
                     && i.InvitationType == InvitationTypes.GuestPass
                     && i.Status == "pending"
                     && i.VisitDate != null
                     && i.VisitDate < today)
            .ToListAsync(ct);

        foreach (var row in overdue)
        {
            row.Status = "expired";
            row.UpdatedAtUtc = now;
        }

        if (overdue.Count > 0)
            await _dbContext.SaveChangesAsync(ct);

        return overdue.Count;
    }

    private IQueryable<MemberInvitation> LoadInvitationsQuery(Guid tenantId)
        => _dbContext.MemberInvitations
            .Include(i => i.InvitingMember)
            .Where(i => i.TenantId == tenantId
                     && i.InvitationType == InvitationTypes.Invitation);

    private async Task<GymMember?> ResolveGymMemberAsync(Guid identityUserId, Guid tenantId)
    {
        var identityKey = identityUserId.ToString();
        var appUser = await _dbContext.AppUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.TenantId == tenantId && u.UserId == identityKey);

        if (appUser == null)
            return null;

        return await _dbContext.GymMembers
            .FirstOrDefaultAsync(m => m.TenantId == tenantId && m.AppUserId == appUser.Id);
    }

    private async Task<Membership?> LoadCoveringMembershipAsync(
        Guid gymMemberId, Guid tenantId, DateOnly today)
    {
        var memberships = await _dbContext.Memberships
            .Include(m => m.Plan)
            .Where(m => m.MemberId == gymMemberId && m.TenantId == tenantId)
            .ToListAsync();

        return MembershipOperational.SelectCoveringToday(memberships, today);
    }

    private static bool IsInviteEligible(Membership? covering, DateOnly today)
        => covering?.Plan != null && MembershipOperational.IsCheckinEligible(covering, today);

    private async Task<int> CountUsedAsync(Guid gymMemberId, Guid tenantId, Guid membershipId)
        => await _dbContext.MemberInvitations.CountAsync(i =>
            i.TenantId == tenantId
            && i.InvitingMemberId == gymMemberId
            && i.InvitationType == InvitationTypes.Invitation
            && i.CoveringMembershipId == membershipId);

    private async Task<InvitationQuotaDto> BuildQuotaDtoAsync(
        Guid gymMemberId, Guid tenantId, Membership? covering)
    {
        var today = MembershipOperational.TodayCairo();
        var dto = new InvitationQuotaDto
        {
            MemberId = gymMemberId,
            MembershipId = covering?.Id,
            PlanId = covering?.PlanId,
            PlanName = covering?.Plan?.Name,
            MembershipStatus = covering == null
                ? "none"
                : MembershipOperational.GetEffectiveStatus(covering, today)
        };

        if (covering?.Plan == null)
            return dto;

        dto.Total = Math.Max(0, covering.Plan.ReferralInviteQuota);
        dto.Used = await CountUsedAsync(gymMemberId, tenantId, covering.Id);
        dto.Remaining = IsInviteEligible(covering, today)
            ? Math.Max(0, dto.Total - dto.Used)
            : 0;
        return dto;
    }

    private InvitationHistoryResponse MapItem(MemberInvitation i)
        => new()
        {
            Id = i.Id,
            Name = i.GuestName,
            PhoneNumber = i.GuestPhoneNumber,
            NationalId = TryDecrypt(i.NationalIdEncrypted),
            Notes = i.Notes,
            Status = i.Status,
            CreatedAtUtc = i.CreatedAtUtc == default ? i.SentAtUtc : i.CreatedAtUtc,
            ContactedAtUtc = i.ContactedAtUtc,
            ConvertedAtUtc = i.ConvertedAtUtc,
            InvitedByMemberId = i.InvitingMemberId,
            InvitedByName = i.InvitingMember?.FullName ?? string.Empty
        };

    private static InvitationMemberSummaryDto Build360(
        InvitationQuotaDto quota, List<InvitationHistoryResponse> items)
    {
        static int Count(IEnumerable<InvitationHistoryResponse> src, string status)
            => src.Count(i => string.Equals(i.Status, status, StringComparison.OrdinalIgnoreCase));

        return new InvitationMemberSummaryDto
        {
            Quota = quota,
            Total = items.Count,
            New = Count(items, InvitationStatuses.New),
            Contacted = Count(items, InvitationStatuses.Contacted),
            Interested = Count(items, InvitationStatuses.Interested),
            NotInterested = Count(items, InvitationStatuses.NotInterested),
            Converted = Count(items, InvitationStatuses.Converted),
            Items = items
        };
    }

    private static SendInvitationResponse ToSendResponse(
        MemberInvitation invitation, InvitationQuotaDto quota, bool alreadyExisted,
        string message, string messageAr)
        => new()
        {
            InvitationId = invitation.Id,
            Name = invitation.GuestName,
            PhoneNumber = invitation.GuestPhoneNumber,
            Status = invitation.Status,
            AlreadyExisted = alreadyExisted,
            QuotaTotal = quota.Total,
            QuotaUsed = quota.Used,
            QuotaRemaining = quota.Remaining,
            Message = message,
            MessageAr = messageAr
        };

    private string? TryDecrypt(string? encrypted)
    {
        if (string.IsNullOrWhiteSpace(encrypted))
            return null;
        try
        {
            return _encryption.Decrypt(encrypted);
        }
        catch
        {
            return null;
        }
    }
}
