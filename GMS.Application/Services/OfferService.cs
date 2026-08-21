namespace GMS.Application.Services;

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using GMS.Application.Common;
using GMS.Application.DTOs.Offers;
using GMS.Application.DTOs.Promo;
using GMS.Application.Interfaces;
using GMS.Application.Validators;
using GMS.Core.Constants;
using GMS.Core.Entities;
using GMS.Infrastructure.Persistence;

public class OfferService : IOfferService
{
    private static readonly TimeZoneInfo CairoTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time");
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly GymFlowProDbContext _db;
    private readonly IPromoService _promoService;
    private readonly ILogger<OfferService> _logger;

    public OfferService(GymFlowProDbContext db, IPromoService promoService, ILogger<OfferService> logger)
    {
        _db = db;
        _promoService = promoService;
        _logger = logger;
    }

    public async Task<Result<List<OfferDto>>> ListStaffAsync(Guid tenantId, CancellationToken ct = default)
    {
        var rows = await _db.Offers.AsNoTracking()
            .Where(o => o.TenantId == tenantId)
            .OrderByDescending(o => o.Featured)
            .ThenBy(o => o.DisplayOrder)
            .ThenByDescending(o => o.CreatedAtUtc)
            .ToListAsync(ct);

        var dtos = new List<OfferDto>(rows.Count);
        foreach (var row in rows)
            dtos.Add(await ToStaffDtoAsync(row, tenantId, ct));
        return Result<List<OfferDto>>.Success(dtos);
    }

    public async Task<Result<OfferDto>> GetStaffByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default)
    {
        var row = await _db.Offers.FirstOrDefaultAsync(o => o.Id == id && o.TenantId == tenantId, ct);
        if (row == null)
            return Result<OfferDto>.Failure("Offer not found / العرض غير موجود");
        return Result<OfferDto>.Success(await ToStaffDtoAsync(row, tenantId, ct));
    }

    public async Task<Result<OfferDto>> CreateAsync(Guid tenantId, UpsertOfferRequest request, CancellationToken ct = default)
    {
        try
        {
            var offer = new Offer { TenantId = tenantId };
            ApplyRequest(offer, request);

            var promo = await SyncPromoAsync(tenantId, offer, request, ct);
            if (!promo.IsSuccess)
                return Result<OfferDto>.Failure(promo.Error ?? "Failed to sync promo code / فشل مزامنة كود الخصم");

            _db.Offers.Add(offer);
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("Offer created {OfferId} for tenant {TenantId}", offer.Id, tenantId);
            return Result<OfferDto>.Success(await ToStaffDtoAsync(offer, tenantId, ct));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating offer for tenant {TenantId}", tenantId);
            return Result<OfferDto>.Failure("Failed to create offer / فشل إنشاء العرض", ex.Message);
        }
    }

    public async Task<Result<OfferDto>> UpdateAsync(Guid tenantId, Guid id, UpsertOfferRequest request, CancellationToken ct = default)
    {
        try
        {
            var offer = await _db.Offers.FirstOrDefaultAsync(o => o.Id == id && o.TenantId == tenantId, ct);
            if (offer == null)
                return Result<OfferDto>.Failure("Offer not found / العرض غير موجود");

            ApplyRequest(offer, request);
            offer.UpdatedAtUtc = DateTime.UtcNow;

            var promo = await SyncPromoAsync(tenantId, offer, request, ct);
            if (!promo.IsSuccess)
                return Result<OfferDto>.Failure(promo.Error ?? "Failed to sync promo code / فشل مزامنة كود الخصم");

            await _db.SaveChangesAsync(ct);
            return Result<OfferDto>.Success(await ToStaffDtoAsync(offer, tenantId, ct));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating offer {OfferId}", id);
            return Result<OfferDto>.Failure("Failed to update offer / فشل تحديث العرض", ex.Message);
        }
    }

    public async Task<Result<OfferDto>> EndAsync(Guid tenantId, Guid id, CancellationToken ct = default)
    {
        var offer = await _db.Offers.FirstOrDefaultAsync(o => o.Id == id && o.TenantId == tenantId, ct);
        if (offer == null)
            return Result<OfferDto>.Failure("Offer not found / العرض غير موجود");

        offer.EndDate = GetTodayCairo().AddDays(-1);
        offer.UpdatedAtUtc = DateTime.UtcNow;

        if (offer.PromoCodeId.HasValue)
            await _promoService.DeactivateAsync(offer.PromoCodeId.Value);

        await _db.SaveChangesAsync(ct);
        return Result<OfferDto>.Success(await ToStaffDtoAsync(offer, tenantId, ct));
    }

    public async Task<Result<List<MemberOfferDto>>> ListMemberAsync(
        Guid tenantId, Guid identityUserId, CancellationToken ct = default)
    {
        var today = GetTodayCairo();
        var isNewMember = await IsNewMemberAsync(tenantId, identityUserId, ct);

        var rows = await _db.Offers.AsNoTracking()
            .Where(o => o.TenantId == tenantId
                        && o.ShowOnMemberApp
                        && !o.IsDraft
                        && o.StartDate <= today
                        && o.EndDate >= today)
            .OrderByDescending(o => o.Featured)
            .ThenBy(o => o.DisplayOrder)
            .ThenBy(o => o.Name)
            .ToListAsync(ct);

        var visible = rows
            .Where(o => !o.NewMembersOnly || isNewMember)
            .ToList();

        var dtos = new List<MemberOfferDto>(visible.Count);
        foreach (var row in visible)
            dtos.Add(await ToMemberDtoAsync(row, tenantId, ct));
        return Result<List<MemberOfferDto>>.Success(dtos);
    }

    public async Task<Result<MemberOfferDto>> GetMemberByIdAsync(
        Guid tenantId, Guid identityUserId, Guid id, CancellationToken ct = default)
    {
        var list = await ListMemberAsync(tenantId, identityUserId, ct);
        if (!list.IsSuccess)
            return Result<MemberOfferDto>.Failure(list.Error ?? "Failed to load offers / فشل جلب العروض");

        var found = list.Data!.FirstOrDefault(o => o.Id == id);
        if (found == null)
            return Result<MemberOfferDto>.Failure("Offer not found / العرض غير موجود");
        return Result<MemberOfferDto>.Success(found);
    }

    private static void ApplyRequest(Offer offer, UpsertOfferRequest request)
    {
        var discount = UpsertOfferRequestValidator.NormalizeDiscount(request.DiscountType);
        var redemption = UpsertOfferRequestValidator.NormalizeRedemption(request.Redemption);
        var applies = UpsertOfferRequestValidator.NormalizeApplies(request.AppliesTo);

        offer.Name = request.Name.Trim();
        offer.NameAr = EmptyToNull(request.NameAr);
        offer.ShortDescription = (request.ShortDescription ?? "").Trim();
        offer.ShortDescriptionAr = EmptyToNull(request.ShortDescriptionAr);
        offer.Description = EmptyToNull(request.Description);
        offer.BannerUrl = EmptyToNull(request.BannerUrl);
        offer.StartDate = request.Start;
        offer.EndDate = request.End;
        offer.AppliesTo = applies;
        offer.PlanIdsJson = SerializeGuids(request.PlanIds);
        offer.ProductIdsJson = SerializeGuids(request.ProductIds);
        offer.MembershipLabelsJson = SerializeStrings(request.MembershipLabels);
        offer.ProductLabelsJson = SerializeStrings(request.ProductLabels);
        offer.DiscountType = discount;
        offer.Value = request.Value;
        offer.MaxDiscount = request.MaxDiscount;
        offer.BuyQty = request.BuyQty;
        offer.GetQty = request.GetQty;
        offer.AllMembers = request.AllMembers && !request.NewMembersOnly;
        offer.NewMembersOnly = request.NewMembersOnly;
        offer.MinPurchase = request.MinPurchase;
        offer.UsageLimit = request.UsageLimit;
        offer.PerMemberLimit = request.PerMemberLimit;
        offer.ShowOnMemberApp = request.ShowOnMemberApp;
        offer.Featured = request.Featured;
        offer.ShowBanner = request.ShowBanner;
        offer.DisplayOrder = request.DisplayOrder < 1 ? 1 : request.DisplayOrder;
        offer.Redemption = redemption;
        offer.PromoCode = string.IsNullOrWhiteSpace(request.PromoCode)
            ? null
            : request.PromoCode.Trim().ToUpperInvariant();
        offer.IsDraft = request.IsDraft;
    }

    private async Task<Result<bool>> SyncPromoAsync(
        Guid tenantId, Offer offer, UpsertOfferRequest request, CancellationToken ct)
    {
        var needsPromo = !offer.IsDraft
                         && offer.Redemption == OfferRedemptions.PromoCode
                         && offer.DiscountType != OfferDiscountTypes.Bxgy
                         && !string.IsNullOrWhiteSpace(offer.PromoCode);

        if (!needsPromo)
            return Result<bool>.Success(true);

        var planIds = ParseGuids(offer.PlanIdsJson);
        var body = new CreatePromoCodeRequest
        {
            Code = offer.PromoCode!,
            Type = offer.DiscountType == OfferDiscountTypes.Fixed ? "fixed" : "percent",
            Value = offer.Value ?? 0,
            AppliesTo = planIds.Count > 0 ? planIds : null,
            ValidFrom = offer.StartDate,
            ValidTo = offer.EndDate,
            MaxUses = offer.UsageLimit,
            MaxUsesPerMember = offer.PerMemberLimit,
            MinPrice = offer.MinPurchase
        };

        if (offer.PromoCodeId.HasValue)
        {
            var update = await _promoService.UpdateAsync(offer.PromoCodeId.Value, new UpdatePromoCodeRequest
            {
                Code = body.Code,
                Type = body.Type,
                Value = body.Value,
                AppliesTo = body.AppliesTo,
                ValidFrom = body.ValidFrom,
                ValidTo = body.ValidTo,
                MaxUses = body.MaxUses,
                MaxUsesPerMember = body.MaxUsesPerMember,
                MinPrice = body.MinPrice
            });
            return update.IsSuccess
                ? Result<bool>.Success(true)
                : Result<bool>.Failure(update.Error ?? "Promo update failed");
        }

        var created = await _promoService.CreateAsync(tenantId, body);
        if (!created.IsSuccess)
            return Result<bool>.Failure(created.Error ?? "Promo create failed");

        offer.PromoCodeId = created.Data!.Id;
        return Result<bool>.Success(true);
    }

    private async Task<OfferDto> ToStaffDtoAsync(Offer offer, Guid tenantId, CancellationToken ct)
    {
        var labels = await ResolveLabelsAsync(offer, tenantId, ct);
        return new OfferDto
        {
            Id = offer.Id,
            Name = offer.Name,
            NameAr = offer.NameAr,
            ShortDescription = offer.ShortDescription,
            ShortDescriptionAr = offer.ShortDescriptionAr,
            Description = offer.Description,
            BannerUrl = offer.BannerUrl,
            Start = offer.StartDate,
            End = offer.EndDate,
            AppliesTo = offer.AppliesTo,
            PlanIds = ParseGuids(offer.PlanIdsJson),
            ProductIds = ParseGuids(offer.ProductIdsJson),
            MembershipLabels = labels.Plans,
            ProductLabels = labels.Products,
            DiscountType = offer.DiscountType,
            Value = offer.Value,
            MaxDiscount = offer.MaxDiscount,
            BuyQty = offer.BuyQty,
            GetQty = offer.GetQty,
            AllMembers = offer.AllMembers,
            NewMembersOnly = offer.NewMembersOnly,
            MinPurchase = offer.MinPurchase,
            UsageLimit = offer.UsageLimit,
            PerMemberLimit = offer.PerMemberLimit,
            UsesCount = offer.PromoCodeId.HasValue
                ? await UsesFromPromoAsync(offer.PromoCodeId.Value, offer.UsesCount, ct)
                : offer.UsesCount,
            ShowOnMemberApp = offer.ShowOnMemberApp,
            Featured = offer.Featured,
            ShowBanner = offer.ShowBanner,
            DisplayOrder = offer.DisplayOrder,
            Redemption = offer.Redemption,
            PromoCode = offer.PromoCode,
            PromoCodeId = offer.PromoCodeId,
            IsDraft = offer.IsDraft,
            Status = ComputeStatus(offer),
            DiscountLabel = DiscountLabel(offer),
            CreatedAtUtc = offer.CreatedAtUtc,
            UpdatedAtUtc = offer.UpdatedAtUtc
        };
    }

    private async Task<MemberOfferDto> ToMemberDtoAsync(Offer offer, Guid tenantId, CancellationToken ct)
    {
        var labels = await ResolveLabelsAsync(offer, tenantId, ct);
        return new MemberOfferDto
        {
            Id = offer.Id,
            Name = offer.Name,
            NameAr = offer.NameAr,
            ShortDescription = offer.ShortDescription,
            ShortDescriptionAr = offer.ShortDescriptionAr,
            Description = offer.Description,
            BannerUrl = offer.BannerUrl,
            Start = offer.StartDate,
            End = offer.EndDate,
            AppliesTo = offer.AppliesTo,
            MembershipLabels = labels.Plans,
            ProductLabels = labels.Products,
            DiscountType = offer.DiscountType,
            DiscountLabel = DiscountLabel(offer),
            BuyQty = offer.BuyQty,
            GetQty = offer.GetQty,
            NewMembersOnly = offer.NewMembersOnly,
            ShowOnMemberApp = offer.ShowOnMemberApp,
            Featured = offer.Featured,
            ShowBanner = offer.ShowBanner,
            DisplayOrder = offer.DisplayOrder,
            Redemption = offer.Redemption,
            PromoCodeHint = offer.Redemption == OfferRedemptions.PromoCode ? "Have a code?" : null,
            Status = OfferStatuses.Active
        };
    }

    private async Task<(List<string> Plans, List<string> Products)> ResolveLabelsAsync(
        Offer offer, Guid tenantId, CancellationToken ct)
    {
        var planLabels = ParseStrings(offer.MembershipLabelsJson);
        var productLabels = ParseStrings(offer.ProductLabelsJson);

        var planIds = ParseGuids(offer.PlanIdsJson);
        if (planLabels.Count == 0 && planIds.Count > 0)
        {
            planLabels = await _db.MembershipPlans.AsNoTracking()
                .Where(p => p.TenantId == tenantId && planIds.Contains(p.Id))
                .Select(p => p.Name)
                .ToListAsync(ct);
        }

        var productIds = ParseGuids(offer.ProductIdsJson);
        if (productLabels.Count == 0 && productIds.Count > 0)
        {
            productLabels = await _db.Products.AsNoTracking()
                .Where(p => p.TenantId == tenantId && productIds.Contains(p.Id))
                .Select(p => p.Name)
                .ToListAsync(ct);
        }

        return (planLabels, productLabels);
    }

    private async Task<int> UsesFromPromoAsync(Guid promoId, int fallback, CancellationToken ct)
    {
        var uses = await _db.PromoCodes.AsNoTracking()
            .Where(p => p.Id == promoId)
            .Select(p => (int?)p.UsesCount)
            .FirstOrDefaultAsync(ct);
        return uses ?? fallback;
    }

    private async Task<bool> IsNewMemberAsync(Guid tenantId, Guid identityUserId, CancellationToken ct)
    {
        var identityId = identityUserId.ToString();
        var member = await _db.GymMembers
            .AsNoTracking()
            .FirstOrDefaultAsync(m =>
                m.TenantId == tenantId
                && m.AppUser != null
                && m.AppUser.UserId == identityId, ct);
        if (member == null)
            return true;

        return !await _db.Memberships.AsNoTracking()
            .AnyAsync(m => m.MemberId == member.Id, ct);
    }

    private static string ComputeStatus(Offer offer)
    {
        if (offer.IsDraft) return OfferStatuses.Draft;
        var today = GetTodayCairo();
        if (offer.EndDate < today) return OfferStatuses.Expired;
        if (offer.StartDate > today) return OfferStatuses.Scheduled;
        return OfferStatuses.Active;
    }

    private static string DiscountLabel(Offer offer)
    {
        if (offer.DiscountType == OfferDiscountTypes.Bxgy)
            return $"Buy {offer.BuyQty ?? 2} Get {offer.GetQty ?? 1}";
        if (offer.DiscountType == OfferDiscountTypes.Fixed)
            return $"EGP {offer.Value ?? 0} OFF";
        return $"{offer.Value ?? 0}% OFF";
    }

    private static DateOnly GetTodayCairo() =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, CairoTimeZone));

    private static string? EmptyToNull(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static List<Guid> ParseGuids(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<Guid>();
        try
        {
            return JsonSerializer.Deserialize<List<Guid>>(json, JsonOpts) ?? new List<Guid>();
        }
        catch
        {
            return new List<Guid>();
        }
    }

    private static List<string> ParseStrings(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<string>();
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json, JsonOpts) ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }

    private static string? SerializeGuids(List<Guid>? ids) =>
        ids == null || ids.Count == 0 ? null : JsonSerializer.Serialize(ids);

    private static string? SerializeStrings(List<string>? labels)
    {
        var clean = (labels ?? new List<string>())
            .Select(s => (s ?? "").Trim())
            .Where(s => s.Length > 0)
            .ToList();
        return clean.Count == 0 ? null : JsonSerializer.Serialize(clean);
    }
}
