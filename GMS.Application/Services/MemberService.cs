namespace GMS.Application.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using GMS.Application.Common;
using GMS.Application.DTOs.Members;
using GMS.Application.Interfaces;
using GMS.Application.Utilities;
using GMS.Core.Constants;
using GMS.Core.Entities;
using GMS.Core.Interfaces;
using GMS.Core.Utilities;
using GMS.Infrastructure.Persistence;

/// <summary>
/// Member management service with full CRUD, search, freeze/unfreeze.
/// NationalId encrypted via IEncryptionService (AES-256).
/// MemberNumber auto-generated: GYM-{sequence:D3}.
/// </summary>
public class MemberService : IMemberService
{
    private readonly GymFlowProDbContext _dbContext;
    private readonly IMemberRepository _memberRepo;
    private readonly IEncryptionService _encryption;
    private readonly ITierEnforcementService _tierEnforcement;
    private readonly IReferralAttributionService _referralAttribution;
    private readonly IMemberAppActivationService _memberAppActivation;
    private readonly ILogger<MemberService> _logger;

    public MemberService(
        GymFlowProDbContext dbContext,
        IMemberRepository memberRepo,
        IEncryptionService encryption,
        ITierEnforcementService tierEnforcement,
        IReferralAttributionService referralAttribution,
        IMemberAppActivationService memberAppActivation,
        ILogger<MemberService> logger)
    {
        _dbContext = dbContext;
        _memberRepo = memberRepo;
        _encryption = encryption;
        _tierEnforcement = tierEnforcement;
        _referralAttribution = referralAttribution;
        _memberAppActivation = memberAppActivation;
        _logger = logger;
    }

    public async Task<Result<PagedResult<MemberListItemDto>>> GetMembersAsync(
        Guid tenantId, string? search, string? status, int page, int pageSize)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var (items, totalCount) = await _memberRepo.GetPagedAsync(
            tenantId, search, status, page, pageSize);

        var dtoItems = items.Select(m =>
        {
            var activeMembership = MembershipOperational.SelectOperational(m.Memberships);
            var effective = activeMembership != null
                ? MembershipOperational.GetEffectiveStatus(activeMembership)
                : null;

            return new MemberListItemDto
            {
                Id = m.Id,
                MemberNumber = m.MemberNumber,
                FullName = m.FullName,
                FullNameAr = m.FullNameAr,
                Phone = m.PhoneNumber,
                IsActive = m.IsActive,
                ActivePlan = activeMembership?.Plan?.Name,
                ActivePlanAr = activeMembership?.Plan?.NameAr,
                ExpiryDate = activeMembership?.EndDate,
                MembershipStatus = effective,
                PlanType = activeMembership?.Plan?.PlanType,
                SessionsRemaining = activeMembership?.SessionsRemaining
            };
        }).ToList();

        var result = new PagedResult<MemberListItemDto>
        {
            Items = dtoItems,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };

        return Result<PagedResult<MemberListItemDto>>.Success(result);
    }

    public async Task<Result<MemberDetailDto>> GetMemberByIdAsync(Guid id)
    {
        var member = await _memberRepo.GetByIdWithDetailsAsync(id);
        if (member == null)
            return Result<MemberDetailDto>.Failure("Member not found / العضو غير موجود");

        var dto = MapToDetailDto(member);
        dto.InvitationQuotaRemaining = await ComputeInvitationQuotaRemainingAsync(member);
        dto.MemberApp = await _memberAppActivation.GetStatusAsync(
            member.Id, member.TenantId, member.AppUserId);
        return Result<MemberDetailDto>.Success(dto);
    }

    public async Task<Result<MemberDetailDto>> CreateMemberAsync(Guid tenantId, CreateMemberRequest request)
    {
        var normalizedPhone = PhoneNormalizer.Normalize(request.Phone);
        if (normalizedPhone == null)
            return Result<MemberDetailDto>.Failure(
                "Phone must be a valid Egyptian mobile / الرقم لازم يكون موبايل مصري صحيح");

        // Check duplicate phone within tenant
        var existing = await _memberRepo.GetByPhoneAsync(normalizedPhone, tenantId);
        if (existing != null)
            return Result<MemberDetailDto>.Failure(
                "Phone number already registered / رقم الهاتف مسجل بالفعل");

        // Soft cap only — never block create; surface PLAN_SOFT_CAP via Result.Message + controller header.
        var capCheck = await _tierEnforcement.CheckCapAsync(tenantId, "active_members");
        var softCapMessage = capCheck.SoftWarning ? "PLAN_SOFT_CAP:active_members" : null;

        var hasReferral = !string.IsNullOrWhiteSpace(request.ReferralCode) || request.ReferringMemberId.HasValue;
        if (hasReferral)
        {
            var pre = await _referralAttribution.ResolveReferrerAsync(
                tenantId, request.ReferralCode, request.ReferringMemberId,
                normalizedPhone, request.NationalId);
            if (!pre.IsSuccess)
                return Result<MemberDetailDto>.Failure(pre.Error!);
        }

        // Auto-generate MemberNumber
        var sequence = await _memberRepo.GetNextMemberSequenceAsync(tenantId);
        var memberNumber = $"GYM-{sequence:D3}";

        var member = new GymMember
        {
            TenantId = tenantId,
            MemberNumber = memberNumber,
            FullName = request.FullName.Trim(),
            FullNameAr = request.FullNameAr.Trim(),
            PhoneNumber = normalizedPhone,
            DateOfBirth = request.DateOfBirth,
            Email = request.Email?.Trim() ?? string.Empty,
            Notes = request.Notes,
            NationalIdEncrypted = !string.IsNullOrEmpty(request.NationalId)
                ? _encryption.Encrypt(request.NationalId)
                : string.Empty,
            IsActive = true,
            InvitationQuotaRemaining = 0,
            ReferralCode = await AllocateReferralCodeAsync(tenantId)
        };

        await _memberRepo.AddAsync(member);

        if (hasReferral)
        {
            var attach = await _referralAttribution.AttachPendingAsync(
                tenantId, member.Id, request.ReferralCode, request.ReferringMemberId);
            if (!attach.IsSuccess)
            {
                member.IsDeleted = true;
                member.IsActive = false;
                member.UpdatedAtUtc = DateTime.UtcNow;
                await _memberRepo.UpdateAsync(member);
                return Result<MemberDetailDto>.Failure(attach.Error!);
            }
        }

        _logger.LogInformation(
            "Member created: {MemberNumber} ({Name}) in tenant {TenantId}",
            memberNumber, member.FullName, tenantId);

        // Reload with details (falls back to the in-memory entity if ambient tenant filter hides it)
        var created = await _memberRepo.GetByIdWithDetailsAsync(member.Id);
        var createdDto = MapToDetailDto(created ?? member);
        createdDto.InvitationQuotaRemaining = await ComputeInvitationQuotaRemainingAsync(created ?? member);
        return Result<MemberDetailDto>.Success(createdDto, softCapMessage);
    }

    public async Task<Result<MemberDetailDto>> UpdateMemberAsync(Guid id, UpdateMemberRequest request)
    {
        var member = await _memberRepo.GetByIdAsync(id);
        if (member == null)
            return Result<MemberDetailDto>.Failure("Member not found / العضو غير موجود");

        // Update only provided fields
        if (!string.IsNullOrEmpty(request.FullName))
            member.FullName = request.FullName.Trim();

        if (!string.IsNullOrEmpty(request.FullNameAr))
            member.FullNameAr = request.FullNameAr.Trim();

        if (!string.IsNullOrEmpty(request.Phone))
        {
            var normalizedPhone = PhoneNormalizer.Normalize(request.Phone);
            if (normalizedPhone == null)
                return Result<MemberDetailDto>.Failure(
                    "Phone must be a valid Egyptian mobile / الرقم لازم يكون موبايل مصري صحيح");

            // Check duplicate phone (exclude current member)
            var existing = await _memberRepo.GetByPhoneAsync(normalizedPhone, member.TenantId);
            if (existing != null && existing.Id != id)
                return Result<MemberDetailDto>.Failure(
                    "Phone number already registered / رقم الهاتف مسجل بالفعل");
            member.PhoneNumber = normalizedPhone;
        }

        if (request.DateOfBirth.HasValue)
            member.DateOfBirth = request.DateOfBirth.Value;

        if (!string.IsNullOrEmpty(request.NationalId))
            member.NationalIdEncrypted = _encryption.Encrypt(request.NationalId);

        if (!string.IsNullOrEmpty(request.Email))
            member.Email = request.Email.Trim();

        if (request.Notes != null)
            member.Notes = request.Notes;

        member.UpdatedAtUtc = DateTime.UtcNow;
        await _memberRepo.UpdateAsync(member);

        _logger.LogInformation("Member updated: {MemberNumber}", member.MemberNumber);

        var updated = await _memberRepo.GetByIdWithDetailsAsync(member.Id);
        var updatedDto = MapToDetailDto(updated!);
        updatedDto.InvitationQuotaRemaining = await ComputeInvitationQuotaRemainingAsync(updated!);
        return Result<MemberDetailDto>.Success(updatedDto);
    }

    public async Task<Result<string>> DeactivateMemberAsync(Guid id)
    {
        var member = await _memberRepo.GetByIdAsync(id);
        if (member == null)
            return Result<string>.Failure("Member not found / العضو غير موجود");

        if (!member.IsActive)
            return Result<string>.Success($"Member {member.MemberNumber} is already inactive / العضو غير نشط بالفعل");

        await _memberRepo.DeactivateAsync(id);

        _logger.LogInformation("Member deactivated: {MemberNumber}", member.MemberNumber);
        return Result<string>.Success($"Member {member.MemberNumber} deactivated / تم تعطيل العضو");
    }

    public async Task<Result<string>> ReactivateMemberAsync(Guid id)
    {
        var member = await _memberRepo.GetByIdAsync(id);
        if (member == null)
            return Result<string>.Failure("Member not found / العضو غير موجود");

        if (member.IsActive)
            return Result<string>.Success($"Member {member.MemberNumber} is already active / العضو نشط بالفعل");

        await _memberRepo.ReactivateAsync(id);

        _logger.LogInformation("Member reactivated: {MemberNumber}", member.MemberNumber);
        return Result<string>.Success($"Member {member.MemberNumber} reactivated / تم إعادة تفعيل العضو");
    }

    public async Task<Result<PagedResult<AttendanceSummaryDto>>> GetMemberAttendanceAsync(
        Guid memberId, int page, int pageSize)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 50);

        var member = await _memberRepo.GetByIdAsync(memberId);
        if (member == null)
            return Result<PagedResult<AttendanceSummaryDto>>.Failure("Member not found");

        var query = _dbContext.GymAttendances
            .Where(a => a.MemberId == memberId)
            .OrderByDescending(a => a.CheckInAtUtc);

        var totalCount = await query.CountAsync();

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new AttendanceSummaryDto
            {
                Id = a.Id,
                CheckInAtUtc = a.CheckInAtUtc,
                CheckOutAtUtc = a.CheckOutAtUtc,
                EntryMethod = a.EntryMethod
            })
            .ToListAsync();

        return Result<PagedResult<AttendanceSummaryDto>>.Success(new PagedResult<AttendanceSummaryDto>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        });
    }

    public async Task<Result<MembershipSummaryDto>> GetCurrentMembershipAsync(Guid memberId)
    {
        var membership = await _dbContext.Memberships
            .Include(m => m.Plan)
            .Where(m => m.MemberId == memberId && (m.Status == "active" || m.Status == "frozen"))
            .OrderByDescending(m => m.EndDate)
            .FirstOrDefaultAsync();

        if (membership == null)
            return Result<MembershipSummaryDto>.Failure(
                "No active membership / لا توجد عضوية نشطة");

        return Result<MembershipSummaryDto>.Success(MapMembership(membership));
    }

    public async Task<Result<string>> FreezeMembershipAsync(Guid memberId, DateTime frozenUntil, string? reason)
    {
        var membership = await _dbContext.Memberships
            .FirstOrDefaultAsync(m => m.MemberId == memberId && m.Status == "active");

        if (membership == null)
            return Result<string>.Failure("No active membership to freeze / لا توجد عضوية نشطة للتجميد");

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var frozenUntilDate = DateOnly.FromDateTime(frozenUntil);
        var freezeDays = frozenUntilDate.DayNumber - today.DayNumber;

        if (freezeDays <= 0)
            return Result<string>.Failure("Frozen-until date must be in the future / تاريخ التجميد لازم يكون في المستقبل");

        membership.Status = "frozen";
        membership.FrozenFromDate = today;
        membership.FrozenUntilDate = frozenUntilDate;

        // Extend end_date by freeze duration
        membership.EndDate = membership.EndDate.AddDays(freezeDays);
        membership.UpdatedAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation(
            "Membership frozen: member {MemberId}, until {FrozenUntil}, new end {End}, reason: {Reason}",
            memberId, frozenUntilDate, membership.EndDate, reason ?? "N/A");

        return Result<string>.Success(
            $"Membership frozen until {frozenUntilDate:dd/MM/yyyy}. New expiry: {membership.EndDate:dd/MM/yyyy} / " +
            $"تم تجميد العضوية حتى {frozenUntilDate:dd/MM/yyyy}. تاريخ الانتهاء الجديد: {membership.EndDate:dd/MM/yyyy}");
    }

    public async Task<Result<string>> UnfreezeMembershipAsync(Guid memberId)
    {
        var membership = await _dbContext.Memberships
            .FirstOrDefaultAsync(m => m.MemberId == memberId && m.Status == "frozen");

        if (membership == null)
            return Result<string>.Failure("No frozen membership found / لا توجد عضوية مجمدة");

        membership.Status = "active";
        membership.FrozenFromDate = null;
        membership.FrozenUntilDate = null;
        membership.UpdatedAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Membership unfrozen: member {MemberId}", memberId);

        return Result<string>.Success(
            "Membership unfrozen and active / تم تفعيل العضوية مرة أخرى");
    }

    // ── Private Helpers ──

    /// Live remaining = plan.ReferralInviteQuota − invitations created on the covering membership.
    /// Frozen / expired / cancelled covering membership → 0.
    private async Task<int> ComputeInvitationQuotaRemainingAsync(GymMember m)
    {
        var today = MembershipOperational.TodayCairo();
        var memberships = m.Memberships ?? Array.Empty<Membership>();
        var covering = MembershipOperational.SelectCoveringToday(memberships, today);

        if (covering?.Plan == null)
            return 0;
        if (!MembershipOperational.IsCheckinEligible(covering, today))
            return 0;

        var quota = covering.Plan.ReferralInviteQuota;
        if (quota <= 0)
            return 0;

        var used = await _dbContext.MemberInvitations
            .CountAsync(i => i.InvitingMemberId == m.Id
                             && i.TenantId == m.TenantId
                             && i.InvitationType == InvitationTypes.Invitation
                             && i.CoveringMembershipId == covering.Id);

        return Math.Max(0, quota - used);
    }

    private async Task<string> AllocateReferralCodeAsync(Guid tenantId)
    {
        for (var attempt = 0; attempt < 12; attempt++)
        {
            var code = ReferralCodeGenerator.Create();
            var exists = await _dbContext.GymMembers
                .AnyAsync(m => m.TenantId == tenantId && m.ReferralCode == code);
            if (!exists)
                return code;
        }

        // Extremely unlikely — append unique suffix
        return ReferralCodeGenerator.Create() + Guid.NewGuid().ToString("N")[..4].ToUpperInvariant();
    }

    private static MemberDetailDto MapToDetailDto(GymMember m)
    {
        var memberships = m.Memberships ?? Array.Empty<Membership>();
        var attendances = m.Attendances ?? Array.Empty<GymAttendance>();
        var activeMembership = MembershipOperational.SelectOperational(memberships);

        return new MemberDetailDto
        {
            Id = m.Id,
            MemberNumber = m.MemberNumber,
            FullName = m.FullName,
            FullNameAr = m.FullNameAr,
            Phone = m.PhoneNumber,
            Email = m.Email,
            DateOfBirth = m.DateOfBirth,
            ProfilePhotoUrl = m.ProfilePhotoUrl,
            Notes = m.Notes,
            IsActive = m.IsActive,
            InvitationQuotaRemaining = m.InvitationQuotaRemaining,
            ReferralCode = m.ReferralCode,
            SuccessfulReferralCount = m.SuccessfulReferralCount,
            ReferralTier = m.ReferralTier,
            CreatedAtUtc = m.CreatedAtUtc,
            CurrentMembership = activeMembership != null ? MapMembership(activeMembership) : null,
            RecentAttendance = attendances
                .OrderByDescending(a => a.CheckInAtUtc)
                .Take(5)
                .Select(a => new AttendanceSummaryDto
                {
                    Id = a.Id,
                    CheckInAtUtc = a.CheckInAtUtc,
                    CheckOutAtUtc = a.CheckOutAtUtc,
                    EntryMethod = a.EntryMethod
                }).ToList()
        };
    }

    private static MembershipSummaryDto MapMembership(Membership ms) => new()
    {
        Id = ms.Id,
        PlanName = ms.Plan?.Name ?? string.Empty,
        PlanNameAr = ms.Plan?.NameAr ?? string.Empty,
        PlanType = ms.Plan?.PlanType ?? string.Empty,
        Status = MembershipOperational.GetEffectiveStatus(ms),
        StartDate = ms.StartDate,
        EndDate = ms.EndDate,
        SessionsRemaining = ms.SessionsRemaining,
        FrozenFromDate = ms.FrozenFromDate,
        FrozenUntilDate = ms.FrozenUntilDate,
        AmountPaid = ms.AmountPaid,
        PaymentMethod = ms.PaymentMethod
    };
}
