namespace GMS.Application.Services;

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using GMS.Application.Common;
using GMS.Application.DTOs.Promo;
using GMS.Application.Interfaces;
using GMS.Core.Constants;
using GMS.Core.Entities;
using GMS.Core.Interfaces;
using GMS.Infrastructure.Persistence;

/// <summary>
/// Promo code CRUD, server-side validation/pricing, and atomic redemption tracking.
/// </summary>
public class PromoService : IPromoService
{
    private static readonly TimeZoneInfo CairoTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time");

    private readonly GymFlowProDbContext _dbContext;
    private readonly IRepository<PromoCode> _promoRepository;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<PromoService> _logger;

    public PromoService(
        GymFlowProDbContext dbContext,
        IRepository<PromoCode> promoRepository,
        ITenantContext tenantContext,
        ILogger<PromoService> logger)
    {
        _dbContext = dbContext;
        _promoRepository = promoRepository;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    public async Task<Result<PromoValidationResult>> ValidateAndPriceAsync(string code, Guid planId, Guid memberId, Guid tenantId)
    {
        try
        {
            var normalizedCode = code.Trim().ToUpperInvariant();

            var promo = await _dbContext.PromoCodes
                .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.Code == normalizedCode);

            if (promo == null)
                return Rejected(PromoValidationReasons.CodeNotFound);

            if (!promo.IsActive)
                return Rejected(PromoValidationReasons.CodeInactive, promo);

            var todayCairo = GetTodayCairo();
            if (todayCairo < promo.ValidFrom || todayCairo > promo.ValidTo)
                return Rejected(PromoValidationReasons.DateRangeInvalid, promo);

            if (promo.MaxUses.HasValue && promo.UsesCount >= promo.MaxUses.Value)
                return Rejected(PromoValidationReasons.MaxUsesReached, promo);

            if (promo.MaxUsesPerMember.HasValue)
            {
                var memberUses = await _dbContext.Sales
                    .Where(s => s.TenantId == tenantId
                        && s.PromoCodeId == promo.Id
                        && s.MemberId == memberId
                        && s.Status != "refunded")
                    .CountAsync();

                if (memberUses >= promo.MaxUsesPerMember.Value)
                    return Rejected(PromoValidationReasons.MemberMaxUsesReached, promo);
            }

            var appliesTo = ParseAppliesTo(promo.AppliesTo);
            if (appliesTo is { Count: > 0 } && !appliesTo.Contains(planId))
                return Rejected(PromoValidationReasons.PlanNotInScope, promo);

            var plan = await _dbContext.MembershipPlans
                .FirstOrDefaultAsync(p => p.Id == planId && p.TenantId == tenantId);

            if (plan == null)
                return Rejected(PromoValidationReasons.PlanNotFound, promo);

            var discount = promo.Type == "fixed"
                ? Math.Min(promo.Value, plan.Price)
                : plan.Price * promo.Value / 100m;

            discount = RoundHalfUp(discount);
            var finalPrice = Math.Max(0m, RoundHalfUp(plan.Price - discount));

            if (promo.MinPrice.HasValue && finalPrice < promo.MinPrice.Value)
                return Rejected(PromoValidationReasons.BelowMinPrice, promo);

            return Result<PromoValidationResult>.Success(new PromoValidationResult
            {
                IsValid = true,
                PromoCodeId = promo.Id,
                Code = promo.Code,
                Type = promo.Type,
                OriginalPrice = plan.Price,
                DiscountAmount = discount,
                FinalPrice = finalPrice
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating promo code {Code}", code);
            return Result<PromoValidationResult>.Failure(
                "Failed to validate promo code / فشل التحقق من كود الخصم", ex.Message);
        }
    }

    public async Task<bool> TryConsumeAsync(Guid promoCodeId, Guid tenantId)
    {
        var rowsAffected = await _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE promo_codes SET UsesCount = UsesCount + 1 WHERE Id = {promoCodeId} AND TenantId = {tenantId} AND (MaxUses IS NULL OR UsesCount < MaxUses)");

        return rowsAffected > 0;
    }

    public async Task<Result<PromoCodeDto>> CreateAsync(Guid tenantId, CreatePromoCodeRequest request)
    {
        try
        {
            var normalizedCode = request.Code.Trim().ToUpperInvariant();

            var exists = await _dbContext.PromoCodes
                .AnyAsync(p => p.TenantId == tenantId && p.Code == normalizedCode);

            if (exists)
                return Result<PromoCodeDto>.Failure(
                    "A promo code with this code already exists / يوجد كود خصم بنفس هذا الرمز");

            var promo = new PromoCode
            {
                TenantId = tenantId,
                Code = normalizedCode,
                Type = request.Type,
                Value = request.Value,
                AppliesTo = SerializeAppliesTo(request.AppliesTo),
                ValidFrom = request.ValidFrom,
                ValidTo = request.ValidTo,
                MaxUses = request.MaxUses,
                MaxUsesPerMember = request.MaxUsesPerMember,
                MinPrice = request.MinPrice,
                IsActive = true
            };

            await _promoRepository.AddAsync(promo);

            _logger.LogInformation(
                "Promo code created: {PromoCodeId} ({Code}) for tenant {TenantId}",
                promo.Id, promo.Code, tenantId);

            return Result<PromoCodeDto>.Success(ToDto(promo));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating promo code for tenant {TenantId}", tenantId);
            return Result<PromoCodeDto>.Failure(
                "Failed to create promo code / فشل إنشاء كود الخصم", ex.Message);
        }
    }

    public async Task<Result<PromoCodeDto>> UpdateAsync(Guid id, UpdatePromoCodeRequest request)
    {
        try
        {
            var promo = await _dbContext.PromoCodes.FirstOrDefaultAsync(p => p.Id == id);
            if (promo == null)
                return Result<PromoCodeDto>.Failure("Promo code not found / كود الخصم غير موجود");

            promo.Code = request.Code.Trim().ToUpperInvariant();
            promo.Type = request.Type;
            promo.Value = request.Value;
            promo.AppliesTo = SerializeAppliesTo(request.AppliesTo);
            promo.ValidFrom = request.ValidFrom;
            promo.ValidTo = request.ValidTo;
            promo.MaxUses = request.MaxUses;
            promo.MaxUsesPerMember = request.MaxUsesPerMember;
            promo.MinPrice = request.MinPrice;
            promo.UpdatedAtUtc = DateTime.UtcNow;

            await _promoRepository.UpdateAsync(promo);

            _logger.LogInformation("Promo code updated: {PromoCodeId} ({Code})", promo.Id, promo.Code);

            return Result<PromoCodeDto>.Success(ToDto(promo));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating promo code {PromoCodeId}", id);
            return Result<PromoCodeDto>.Failure(
                "Failed to update promo code / فشل تحديث كود الخصم", ex.Message);
        }
    }

    public async Task<Result<PromoCodeDto>> GetByIdAsync(Guid id)
    {
        var promo = await _dbContext.PromoCodes.FirstOrDefaultAsync(p => p.Id == id);
        if (promo == null)
            return Result<PromoCodeDto>.Failure("Promo code not found / كود الخصم غير موجود");

        return Result<PromoCodeDto>.Success(ToDto(promo));
    }

    public async Task<Result<bool>> DeactivateAsync(Guid id)
    {
        try
        {
            var promo = await _dbContext.PromoCodes.FirstOrDefaultAsync(p => p.Id == id);
            if (promo == null)
                return Result<bool>.Failure("Promo code not found / كود الخصم غير موجود");

            promo.IsActive = false;
            promo.UpdatedAtUtc = DateTime.UtcNow;

            await _promoRepository.UpdateAsync(promo);

            _logger.LogInformation("Promo code deactivated: {PromoCodeId} ({Code})", promo.Id, promo.Code);

            return Result<bool>.Success(true, "Promo code deactivated / تم إلغاء تفعيل كود الخصم");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deactivating promo code {PromoCodeId}", id);
            return Result<bool>.Failure(
                "Failed to deactivate promo code / فشل إلغاء تفعيل كود الخصم", ex.Message);
        }
    }

    public async Task<Result<PagedResult<PromoCodeDto>>> GetPagedAsync(
        Guid tenantId, bool? activeOnly, bool? validToday, int page, int pageSize)
    {
        try
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);

            var query = _dbContext.PromoCodes.Where(p => p.TenantId == tenantId);

            if (activeOnly == true)
                query = query.Where(p => p.IsActive);

            if (validToday == true)
            {
                var todayCairo = GetTodayCairo();
                query = query.Where(p => p.ValidFrom <= todayCairo && p.ValidTo >= todayCairo);
            }

            var totalCount = await query.CountAsync();

            var items = await query
                .OrderByDescending(p => p.CreatedAtUtc)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return Result<PagedResult<PromoCodeDto>>.Success(new PagedResult<PromoCodeDto>
            {
                Items = items.Select(ToDto).ToList(),
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving promo codes for tenant {TenantId}", tenantId);
            return Result<PagedResult<PromoCodeDto>>.Failure(
                "Failed to retrieve promo codes / فشل جلب أكواد الخصم", ex.Message);
        }
    }

    // ========================================================================
    // PRIVATE HELPERS
    // ========================================================================

    private static Result<PromoValidationResult> Rejected(string reason, PromoCode? promo = null) =>
        Result<PromoValidationResult>.Success(new PromoValidationResult
        {
            IsValid = false,
            FailureReason = reason,
            PromoCodeId = promo?.Id,
            Code = promo?.Code
        });

    private static DateOnly GetTodayCairo() =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, CairoTimeZone));

    private static decimal RoundHalfUp(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static List<Guid>? ParseAppliesTo(string? appliesToJson) =>
        string.IsNullOrWhiteSpace(appliesToJson) ? null : JsonSerializer.Deserialize<List<Guid>>(appliesToJson);

    private static string? SerializeAppliesTo(List<Guid>? planIds) =>
        planIds == null || planIds.Count == 0 ? null : JsonSerializer.Serialize(planIds);

    private static PromoCodeDto ToDto(PromoCode promo) => new()
    {
        Id = promo.Id,
        Code = promo.Code,
        Type = promo.Type,
        Value = promo.Value,
        AppliesTo = ParseAppliesTo(promo.AppliesTo),
        ValidFrom = promo.ValidFrom,
        ValidTo = promo.ValidTo,
        MaxUses = promo.MaxUses,
        MaxUsesPerMember = promo.MaxUsesPerMember,
        UsesCount = promo.UsesCount,
        MinPrice = promo.MinPrice,
        IsActive = promo.IsActive,
        CreatedAtUtc = promo.CreatedAtUtc,
        UpdatedAtUtc = promo.UpdatedAtUtc
    };
}
