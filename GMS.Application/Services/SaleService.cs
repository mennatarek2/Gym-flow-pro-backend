namespace GMS.Application.Services;

using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using GMS.Application.Common;
using GMS.Application.DTOs.Inventory;
using GMS.Application.DTOs.Members;
using GMS.Application.DTOs.Sales;
using GMS.Application.Interfaces;
using GMS.Core.Constants;
using GMS.Core.Entities;
using GMS.Core.Interfaces;
using GMS.Platform.Constants;
using GMS.Infrastructure.Persistence;

/// <summary>
/// Atomic point-of-sale processing. CreateSaleAsync runs member resolution, promo pricing, VAT,
/// split-payment validation, membership creation, and idempotent replay inside one transaction.
/// </summary>
public class SaleService : ISaleService
{
    private static readonly TimeZoneInfo CairoTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time");

    private readonly GymFlowProDbContext _dbContext;
    private readonly IMemberService _memberService;
    private readonly IPromoService _promoService;
    private readonly IAuditService _auditService;
    private readonly IShiftService _shiftService;
    private readonly IInvoiceService _invoiceService;
    private readonly IWhatsAppService _whatsAppService;
    private readonly IReferralAttributionService _referralAttribution;
    private readonly IStockLedgerService _stockLedger;
    private readonly IFeatureAccessService _featureAccess;
    private readonly ILogger<SaleService> _logger;

    public SaleService(
        GymFlowProDbContext dbContext,
        IMemberService memberService,
        IPromoService promoService,
        IAuditService auditService,
        IShiftService shiftService,
        IInvoiceService invoiceService,
        IWhatsAppService whatsAppService,
        IReferralAttributionService referralAttribution,
        IStockLedgerService stockLedger,
        IFeatureAccessService featureAccess,
        ILogger<SaleService> logger)
    {
        _dbContext = dbContext;
        _memberService = memberService;
        _promoService = promoService;
        _auditService = auditService;
        _shiftService = shiftService;
        _invoiceService = invoiceService;
        _whatsAppService = whatsAppService;
        _referralAttribution = referralAttribution;
        _stockLedger = stockLedger;
        _featureAccess = featureAccess;
        _logger = logger;
    }

    public async Task<Result<SaleResponse>> CreateSaleAsync(
        CreateSaleRequest request, Guid staffUserId, Guid tenantId, IReadOnlySet<string> callerPermissions)
    {
        // a. Idempotency check (read-only, before opening a transaction).
        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            var existingKey = await _dbContext.SaleIdempotencyKeys
                .AsNoTracking()
                .FirstOrDefaultAsync(k => k.TenantId == tenantId && k.Key == request.IdempotencyKey);

            if (existingKey != null)
                return await BuildReplayResponseAsync(existingKey.SaleId, tenantId);
        }

        var staffUserIdStr = staffUserId.ToString();
        var staffUser = await _dbContext.AppUsers
            .FirstOrDefaultAsync(u => u.UserId == staffUserIdStr && u.TenantId == tenantId);

        if (staffUser == null)
            return Fail(SaleFailureReasons.StaffUserNotFound, "Staff user not found / المستخدم غير موجود");

        var cart = NormalizeCart(request);
        if (cart.Count == 0)
            return Fail(SaleFailureReasons.PlanNotFound, "Sale has no lines / لا توجد أسطر في البيع");

        if (request.Payments.Any(payment => !IsDeskPaymentMethod(payment.Method)
            || payment.Amount <= 0m))
        {
            return Fail("PAYMENT_METHOD_NOT_READY",
                "Electronic payments must be authorized through their gateway before recording the sale / يجب اعتماد المدفوعات الإلكترونية عبر بوابتها قبل تسجيل البيع");
        }
        foreach (var payment in request.Payments)
            payment.Method = payment.Method.Trim().ToLowerInvariant();

        var hasMembershipLine = cart.Any(IsMembershipLike);
        var hasRetailLine = cart.Any(l => IsRetail(l.LineType));
        if (hasRetailLine)
        {
            var inventoryOn = await _featureAccess.IsEnabledAsync(tenantId, FeatureKeys.Inventory);
            if (!inventoryOn)
                return Fail(SaleFailureReasons.InventoryRequired,
                    "Inventory feature required for retail sales / ميزة المخزون مطلوبة لمبيعات التجزئة");
        }

        // b. Member resolution (optional for walk-in retail-only).
        GymMember? member = null;
        if (request.MemberId.HasValue)
        {
            member = await _dbContext.GymMembers
                .FirstOrDefaultAsync(m => m.Id == request.MemberId.Value && m.TenantId == tenantId);

            if (member == null)
                return Fail(SaleFailureReasons.MemberNotFound, "Member not found / العضو غير موجود");
        }
        else if (request.NewMember != null)
        {
            var dob = request.NewMember.DateOfBirth ?? DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-20));

            var createResult = await _memberService.CreateMemberAsync(tenantId, new CreateMemberRequest
            {
                FullName = request.NewMember.FullName,
                FullNameAr = request.NewMember.FullNameAr ?? string.Empty,
                Phone = request.NewMember.PhoneNumber,
                DateOfBirth = dob,
                ReferralCode = request.ReferralCode ?? request.NewMember.ReferralCode,
                ReferringMemberId = request.ReferringMemberId ?? request.NewMember.ReferringMemberId
            });

            if (!createResult.IsSuccess)
                return Fail(SaleFailureReasons.MemberCreateFailed,
                    createResult.Error ?? "Failed to create member / فشل إنشاء العضو");

            member = await _dbContext.GymMembers.FirstOrDefaultAsync(m => m.Id == createResult.Data!.Id);
        }
        else if (hasMembershipLine)
        {
            return Fail(SaleFailureReasons.MemberRequired,
                "Member required for membership sales / العضو مطلوب لمبيعات العضوية");
        }

        if (member != null && request.MemberId.HasValue
            && (!string.IsNullOrWhiteSpace(request.ReferralCode) || request.ReferringMemberId.HasValue))
        {
            var attach = await _referralAttribution.AttachPendingAsync(
                tenantId, member.Id, request.ReferralCode, request.ReferringMemberId);
            if (!attach.IsSuccess)
                return Fail("REFERRAL_INVALID", attach.Error ?? "Invalid referral / إحالة غير صالحة");
        }

        // Resolve plans / products and price lines.
        MembershipPlan? primaryPlan = null;
        decimal membershipSubtotal = 0m;
        decimal retailSubtotal = 0m;
        var priced = new List<PricedSaleLine>();

        foreach (var line in cart)
        {
            if (IsMembershipLike(line.LineType))
            {
                var planId = line.PlanId!.Value;
                var plan = await _dbContext.MembershipPlans
                    .FirstOrDefaultAsync(p => p.Id == planId && p.TenantId == tenantId && p.IsActive);
                if (plan == null)
                    return Fail(SaleFailureReasons.PlanNotFound,
                        "Membership plan not found or inactive / الخطة غير موجودة أو غير نشطة");

                primaryPlan ??= plan;
                var unit = line.UnitPrice ?? plan.Price;
                var lineTotal = RoundHalfUp(unit * line.Qty);
                membershipSubtotal += lineTotal;
                priced.Add(new PricedSaleLine(line, plan, null, unit, lineTotal));
            }
            else if (IsRetail(line.LineType))
            {
                var product = await _dbContext.Products
                    .FirstOrDefaultAsync(p => p.Id == line.ProductId && p.TenantId == tenantId);
                if (product == null || !product.IsActive || product.IsArchived || !product.IsSellable)
                    return Fail(SaleFailureReasons.ProductNotFound,
                        "Product not found or not sellable / المنتج غير موجود أو غير قابل للبيع");

                var unit = line.UnitPrice ?? product.SellPrice;
                var lineTotal = RoundHalfUp(unit * line.Qty);
                retailSubtotal += lineTotal;
                priced.Add(new PricedSaleLine(line, null, product, unit, lineTotal));
            }
            else
            {
                return Fail(SaleFailureReasons.PlanNotFound,
                    $"Unsupported lineType {line.LineType} / نوع سطر غير مدعوم");
            }
        }

        if (hasMembershipLine && primaryPlan == null)
            return Fail(SaleFailureReasons.PlanNotFound, "Membership plan not found / الخطة غير موجودة");

        Warehouse? warehouse = null;
        if (hasRetailLine)
        {
            if (request.WarehouseId.HasValue)
            {
                warehouse = await _dbContext.Warehouses
                    .FirstOrDefaultAsync(w => w.Id == request.WarehouseId && w.TenantId == tenantId && w.IsActive);
            }
            else
            {
                warehouse = await _dbContext.Warehouses
                    .FirstOrDefaultAsync(w => w.TenantId == tenantId && w.IsDefault && w.IsActive)
                    ?? await _dbContext.Warehouses
                        .FirstOrDefaultAsync(w => w.TenantId == tenantId && w.IsActive);
            }

            if (warehouse == null)
                return Fail(SaleFailureReasons.WarehouseNotFound,
                    "No active warehouse for retail sale / لا يوجد مخزن نشط لمبيعات التجزئة");

            foreach (var row in priced.Where(p => p.Product != null && p.Product.TrackStock))
            {
                var available = await _stockLedger.GetAvailableAsync(
                    tenantId, row.Product!.Id, warehouse.Id);
                if (!available.IsSuccess)
                    return Fail(SaleFailureReasons.InsufficientStock, available.Error!);

                if (available.Data < row.Req.Qty)
                {
                    var physical = await _stockLedger.GetOnHandAsync(
                        tenantId, row.Product.Id, warehouse.Id);
                    var onHand = physical.IsSuccess ? physical.Data : 0m;
                    if (onHand > 0 && available.Data <= 0)
                    {
                        return Fail(SaleFailureReasons.StockUnsellableExpired,
                            $"No sellable stock for {row.Product.Sku} ({row.Product.Name}): on hand {onHand} is expired / لا يوجد رصيد قابل للبيع للمنتج {row.Product.Sku} (الكمية الموجودة منتهية الصلاحية)");
                    }

                    return Fail(SaleFailureReasons.InsufficientStock,
                        $"Insufficient stock for {row.Product.Sku} ({row.Product.Name}): available {available.Data}, requested {row.Req.Qty} / رصيد غير كافٍ للمنتج {row.Product.Sku}");
                }
            }
        }

        // c. Promo — membership plan only.
        var discountAmount = 0m;
        Guid? promoCodeId = null;

        if (!string.IsNullOrWhiteSpace(request.PromoCode))
        {
            if (primaryPlan == null || member == null)
                return Fail("PROMO_INVALID",
                    "Promo codes require a membership line and member / أكواد الخصم تتطلب سطر عضوية وعضو");

            var promoResult = await _promoService.ValidateAndPriceAsync(
                request.PromoCode, primaryPlan.Id, member.Id, tenantId);

            if (!promoResult.IsSuccess)
                return Fail("PROMO_VALIDATION_ERROR", promoResult.Error ?? "Failed to validate promo code / فشل التحقق من كود الخصم");

            if (!promoResult.Data!.IsValid)
                return Fail(promoResult.Data.FailureReason ?? "PROMO_INVALID",
                    "Promo code is not valid for this sale / كود الخصم غير صالح لهذه العملية");

            discountAmount = promoResult.Data.DiscountAmount ?? 0m;
            promoCodeId = promoResult.Data.PromoCodeId;
        }

        decimal manualDiscountAmount = 0m;
        string? manualDiscountReason = null;

        if (request.ManualDiscount is { Amount: > 0 })
        {
            if (!callerPermissions.Contains(Permissions.SalesDiscountOverride))
                return Fail(SaleFailureReasons.ForbiddenDiscountOverride,
                    "You do not have permission to apply a manual discount / ليس لديك صلاحية لتطبيق خصم يدوي");

            manualDiscountAmount = request.ManualDiscount.Amount;
            manualDiscountReason = request.ManualDiscount.Reason;
            discountAmount += manualDiscountAmount;

            await _auditService.LogAsync("sale.discount.override", "Sale", null, null,
                new { amount = manualDiscountAmount, reason = manualDiscountReason });
        }

        var subtotal = RoundHalfUp(membershipSubtotal + retailSubtotal);
        var discountedSubtotal = Math.Max(0m, RoundHalfUp(subtotal - discountAmount));

        var tenant = await _dbContext.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId);
        var vatEnabled = GetSettingBool(tenant?.Settings, TenantSettingsKeys.VatEnabled, false);
        var vatRate = GetSettingDecimal(tenant?.Settings, TenantSettingsKeys.VatRate, 0.14m);

        var taxAmount = vatEnabled ? RoundHalfUp(discountedSubtotal * vatRate) : 0m;
        var total = RoundHalfUp(discountedSubtotal + taxAmount);

        var paidAmount = request.Payments.Sum(p => p.Amount);
        string saleStatus;
        decimal amountDue;
        DateOnly? dueDate = null;

        if (paidAmount > total)
        {
            return Fail(SaleFailureReasons.Overpay, "Payment total exceeds the sale total / إجمالي المدفوعات يتجاوز إجمالي البيع");
        }
        else if (paidAmount == total)
        {
            saleStatus = "completed";
            amountDue = 0m;
        }
        else if (request.PartialPayment != null)
        {
            saleStatus = "partially_paid";
            amountDue = RoundHalfUp(total - paidAmount);
            dueDate = request.PartialPayment.DueDate;
        }
        else
        {
            return Fail(SaleFailureReasons.PaymentIncomplete,
                "Payment total is less than the sale total; provide partialPayment to allow a balance / إجمالي المدفوعات أقل من إجمالي البيع، أضف partialPayment للسماح برصيد متبقٍ");
        }

        var hasCash = request.Payments.Any(p => p.Method == "cash");
        var shiftId = await _shiftService.GetCurrentOpenShiftIdAsync(staffUserId, tenantId);

        if (hasCash && shiftId == null)
            return Fail(SaleFailureReasons.OpenShiftRequired,
                "An open shift is required to accept cash payments / يجب فتح وردية لقبول مدفوعات نقدية");

        var isRelational = _dbContext.Database.IsRelational();
        var transaction = isRelational
            ? await _dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted)
            : null;

        try
        {
            var today = GetTodayCairo();

            var creditRequested = request.Payments.Where(p => p.Method == "account_credit").Sum(p => p.Amount);
            if (creditRequested > 0m)
            {
                if (member == null)
                {
                    if (transaction != null) await transaction.RollbackAsync();
                    return Fail(SaleFailureReasons.MemberRequired,
                        "Account credit requires a member / رصيد الحساب يتطلب عضواً");
                }

                var creditBalance = await GetMemberCreditBalanceLockedAsync(member.Id, tenantId);
                if (creditBalance < creditRequested)
                {
                    if (transaction != null)
                        await transaction.RollbackAsync();

                    return Fail(SaleFailureReasons.InsufficientCredit,
                        "Member's account credit balance is insufficient / رصيد العضو غير كافٍ");
                }
            }

            var sale = new Sale
            {
                TenantId = tenantId,
                MemberId = member?.Id,
                SoldByUserId = staffUser.Id,
                ShiftId = shiftId,
                Subtotal = subtotal,
                DiscountAmount = discountAmount,
                TaxAmount = taxAmount,
                Total = total,
                PromoCodeId = promoCodeId,
                ManualDiscountAmount = manualDiscountAmount > 0 ? manualDiscountAmount : null,
                ManualDiscountReason = manualDiscountReason,
                AmountDue = amountDue,
                DueDate = dueDate,
                Status = saleStatus,
                IdempotencyKey = request.IdempotencyKey
            };
            _dbContext.Sales.Add(sale);

            Membership? membership = null;
            if (hasMembershipLine && primaryPlan != null && member != null)
            {
                membership = new Membership
                {
                    TenantId = tenantId,
                    MemberId = member.Id,
                    PlanId = primaryPlan.Id,
                    StartDate = today,
                    EndDate = primaryPlan.PlanType == "day_pass" ? today : today.AddDays(primaryPlan.DurationDays),
                    Status = "active",
                    SessionsRemaining = primaryPlan.PlanType == "session_pack" ? primaryPlan.SessionCount : null,
                    PaymentMethod = request.Payments.Count == 1 ? request.Payments[0].Method : "mixed",
                    AmountPaid = paidAmount,
                    PaymentDate = DateTime.UtcNow
                };
                _dbContext.Memberships.Add(membership);

                if (member.IsTrial && primaryPlan.PlanType != "trial")
                {
                    member.IsTrial = false;
                    member.TrialOutcome = "converted";
                    member.TrialConvertedAt = DateTime.UtcNow;
                    member.ConvertingSaleId = sale.Id;
                    member.UpdatedAtUtc = DateTime.UtcNow;
                }
            }

            // Allocate discount preferentially against membership subtotal (promo is plan-scoped).
            var remainingDiscount = discountAmount;
            var saleLinesToStock = new List<(SaleLine Line, Product Product)>();

            foreach (var row in priced)
            {
                var lineDiscountShare = 0m;
                if (remainingDiscount > 0m && row.Plan != null)
                {
                    lineDiscountShare = Math.Min(remainingDiscount, row.LineTotal);
                    remainingDiscount -= lineDiscountShare;
                }

                var netLineTotal = RoundHalfUp(row.LineTotal - lineDiscountShare);
                var saleLine = new SaleLine
                {
                    TenantId = tenantId,
                    SaleId = sale.Id,
                    LineType = row.Req.LineType.Trim().ToLowerInvariant(),
                    ReferenceId = row.Product?.Id ?? membership?.Id,
                    Description = row.Product?.Name ?? row.Plan!.Name,
                    DescriptionAr = row.Product?.NameAr ?? row.Plan?.NameAr,
                    Qty = row.Req.Qty,
                    UnitPrice = row.UnitPrice,
                    LineTotal = netLineTotal,
                    // Cost is assigned only from the immutable stock allocation below.
                    // Product.CostPrice is a current catalog hint, not historical COGS.
                    UnitCost = null,
                    CogsAmount = null
                };
                _dbContext.SaleLines.Add(saleLine);

                if (row.Product != null && row.Product.TrackStock)
                    saleLinesToStock.Add((saleLine, row.Product));
            }

            if (creditRequested > 0m && member != null)
            {
                _dbContext.MemberCredits.Add(new MemberCredit
                {
                    TenantId = tenantId,
                    MemberId = member.Id,
                    Amount = -creditRequested,
                    EntryType = "payment_use",
                    ReferenceId = sale.Id,
                    CreatedByUserId = staffUser.Id
                });
            }

            for (var i = 0; i < request.Payments.Count; i++)
            {
                var payment = request.Payments[i];
                _dbContext.PaymentTransactions.Add(new PaymentTransaction
                {
                    TenantId = tenantId,
                    MemberId = member?.Id,
                    MembershipId = membership?.Id,
                    Gateway = payment.Method,
                    ExternalRef = $"POS:{sale.Id}:{i}",
                    Amount = payment.Amount,
                    Currency = "EGP",
                    Status = "success",
                    SettlementStatus = payment.Method == "cash" ? "settled" : "pending",
                    SettledAtUtc = payment.Method == "cash" ? DateTime.UtcNow : null,
                    PaidAtUtc = DateTime.UtcNow,
                    SaleId = sale.Id,
                    ReceivedByUserId = staffUser.Id,
                    ShiftId = shiftId,
                    Method = payment.Method
                });
            }

            if (promoCodeId.HasValue)
            {
                var consumed = await _promoService.TryConsumeAsync(promoCodeId.Value, tenantId);
                if (!consumed)
                {
                    if (transaction != null)
                        await transaction.RollbackAsync();

                    return Fail(SaleFailureReasons.PromoRaceLost,
                        "The promo code's usage limit was reached by a concurrent request / تم الوصول للحد الأقصى لاستخدام كود الخصم في نفس اللحظة");
                }
            }

            // Persist sale lines so they have IDs before ledger posts (BaseEntity assigns Ids client-side).
            await _dbContext.SaveChangesAsync();

            if (warehouse != null)
            {
                foreach (var (saleLine, product) in saleLinesToStock)
                {
                    var alloc = await _stockLedger.AllocateSaleAsync(
                        tenantId, product.Id, warehouse.Id, saleLine.Qty);
                    if (!alloc.IsSuccess)
                    {
                        if (transaction != null)
                            await transaction.RollbackAsync();

                        return Fail(SaleFailureReasons.InsufficientStock,
                            alloc.Error ?? $"Insufficient stock for {product.Sku} / رصيد غير كافٍ");
                    }

                    decimal allocatedCost = 0m;
                    var completeCost = true;
                    foreach (var slice in alloc.Data!)
                    {
                        var post = await _stockLedger.PostAsync(new StockLedgerPostRequest
                        {
                            TenantId = tenantId,
                            ProductId = product.Id,
                            WarehouseId = warehouse.Id,
                            BatchId = slice.BatchId,
                            QtyDelta = -slice.Qty,
                            UnitCost = slice.UnitCost,
                            Reason = StockMovementReasons.Sale,
                            ReferenceType = StockReferenceTypes.SaleLine,
                            ReferenceId = saleLine.Id,
                            Note = $"Sale {sale.Id:N}",
                            CreatedByUserId = staffUser.Id
                        });

                        if (!post.IsSuccess)
                        {
                            if (transaction != null)
                                await transaction.RollbackAsync();

                            return Fail(SaleFailureReasons.InsufficientStock,
                                post.Error ?? $"Insufficient stock for {product.Sku} / رصيد غير كافٍ");
                        }

                        if (slice.UnitCost is { } unitCost)
                            allocatedCost += slice.Qty * unitCost;
                        else
                            completeCost = false;
                    }

                    if (completeCost && saleLine.Qty > 0m)
                    {
                        saleLine.CogsAmount = RoundHalfUp(allocatedCost);
                        saleLine.UnitCost = RoundHalfUp(allocatedCost / saleLine.Qty);
                    }
                }
            }

            var warnings = new List<string>();
            var requirePaperWaiver = GetSettingBool(tenant?.Settings, TenantSettingsKeys.RequirePaperWaiver, false);
            if (requirePaperWaiver && member != null && !member.PaperWaiverOnFile)
                warnings.Add("Paper waiver is required but not on file for this member / يلزم توقيع نموذج الإعفاء الورقي ولم يتم تسجيله لهذا العضو");

            if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
            {
                _dbContext.SaleIdempotencyKeys.Add(new SaleIdempotencyKey
                {
                    TenantId = tenantId,
                    Key = request.IdempotencyKey,
                    SaleId = sale.Id,
                    ResponseHash = ComputeResponseHash(tenantId, request.IdempotencyKey, sale.Id)
                });
            }

            await _dbContext.SaveChangesAsync();
            if (transaction != null)
                await transaction.CommitAsync();

            if (member != null && membership != null && primaryPlan != null)
            {
                try
                {
                    await _referralAttribution.TryConvertOnPaidActivateAsync(
                        tenantId, member.Id, sale.Id, paidAmount, primaryPlan.PlanType);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Referral convert failed for sale {SaleId}", sale.Id);
                }
            }

            var response = new SaleResponse
            {
                SaleId = sale.Id,
                MembershipId = membership?.Id,
                IsReplay = false,
                InvoiceStatus = total > 0 ? "queued" : "skipped",
                Totals = new SaleTotalsDto
                {
                    Subtotal = subtotal,
                    Discount = discountAmount,
                    Tax = taxAmount,
                    Total = total,
                    Paid = paidAmount,
                    AmountDue = amountDue
                },
                Warnings = warnings
            };

            if (total > 0)
            {
                // Desk needs the receipt immediately — create inline, then still enqueue so
                // delivery + any create retry stay Hangfire-backed (create is idempotent).
                try
                {
                    await _invoiceService.CreateForSaleAsync(sale.Id);
                    await AttachInvoiceRefAsync(response, sale.Id);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Inline invoice create failed for sale {SaleId} — queueing Hangfire job", sale.Id);
                }

                await _invoiceService.EnqueueForSale(sale.Id);
            }

            if (member != null && membership != null)
            {
                _ = _whatsAppService.SendRenewalConfirmationAsync(
                    member.PhoneNumber, member.FullName, membership.EndDate.ToDateTime(TimeOnly.MinValue));
            }

            var cashAmount = request.Payments.Where(p => p.Method == "cash").Sum(p => p.Amount);
            if (shiftId.HasValue && cashAmount > 0)
            {
                try
                {
                    await _shiftService.RecordMovementAsync(
                        shiftId.Value, "sale", cashAmount, sale.Id, null, staffUserId, tenantId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Failed to record cash movement for sale {SaleId} on shift {ShiftId}", sale.Id, shiftId);
                }
            }

            return Result<SaleResponse>.Success(response);
        }
        catch (DbUpdateException ex)
        {
            if (transaction != null)
                await transaction.RollbackAsync();

            if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
            {
                var winner = await _dbContext.SaleIdempotencyKeys
                    .AsNoTracking()
                    .FirstOrDefaultAsync(k => k.TenantId == tenantId && k.Key == request.IdempotencyKey);

                if (winner != null)
                    return await BuildReplayResponseAsync(winner.SaleId, tenantId);
            }

            _logger.LogError(ex, "Error saving sale for tenant {TenantId}", tenantId);
            return Result<SaleResponse>.Failure("Failed to process sale / فشل في معالجة عملية البيع", ex.Message);
        }
        catch (Exception ex)
        {
            if (transaction != null)
                await transaction.RollbackAsync();

            _logger.LogError(ex, "Unexpected error processing sale for tenant {TenantId}", tenantId);
            return Result<SaleResponse>.Failure(
                "An unexpected error occurred while processing the sale / حدث خطأ غير متوقع أثناء معالجة عملية البيع", ex.Message);
        }
        finally
        {
            if (transaction != null)
                await transaction.DisposeAsync();
        }
    }

    private static List<CreateSaleLineRequest> NormalizeCart(CreateSaleRequest request)
    {
        if (request.Lines != null && request.Lines.Count > 0)
            return request.Lines;

        if (request.PlanId.HasValue && request.PlanId.Value != Guid.Empty)
        {
            return new List<CreateSaleLineRequest>
            {
                new()
                {
                    LineType = "membership",
                    PlanId = request.PlanId,
                    Qty = 1
                }
            };
        }

        return new List<CreateSaleLineRequest>();
    }

    private static bool IsRetail(string? t) =>
        string.Equals(t?.Trim(), "retail", StringComparison.OrdinalIgnoreCase);

    private static bool IsDeskPaymentMethod(string? method) =>
        string.Equals(method?.Trim(), "cash", StringComparison.OrdinalIgnoreCase)
        || string.Equals(method?.Trim(), "account_credit", StringComparison.OrdinalIgnoreCase);

    private static bool IsMembershipLike(CreateSaleLineRequest line) => IsMembershipLike(line.LineType);

    private static bool IsMembershipLike(string? t)
    {
        var v = t?.Trim();
        return string.Equals(v, "membership", StringComparison.OrdinalIgnoreCase)
            || string.Equals(v, "trial", StringComparison.OrdinalIgnoreCase)
            || string.Equals(v, "day_pass", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record PricedSaleLine(
        CreateSaleLineRequest Req,
        MembershipPlan? Plan,
        Product? Product,
        decimal UnitPrice,
        decimal LineTotal);

    public async Task<Result<SaleResponse>> RecordPaymentAsync(
        Guid saleId, Guid tenantId, Guid staffUserId, RecordPaymentRequest request)
    {
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction = null;
        try
        {
            var method = request.Method.Trim().ToLowerInvariant();
            if (!IsDeskPaymentMethod(method) || request.Amount <= 0m)
            {
                return Fail("PAYMENT_METHOD_NOT_READY",
                    "Electronic payments must be authorized through their gateway before recording the sale / يجب اعتماد المدفوعات الإلكترونية عبر بوابتها قبل تسجيل البيع");
            }

            if (_dbContext.Database.IsRelational())
                transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable);

            var sale = await _dbContext.Sales.FirstOrDefaultAsync(s => s.Id == saleId && s.TenantId == tenantId);
            if (sale == null)
                return Fail(SaleFailureReasons.SaleNotFound, "Sale not found / عملية البيع غير موجودة");

            if (!string.Equals(sale.Status, "partially_paid", StringComparison.OrdinalIgnoreCase)
                || sale.AmountDue <= 0m)
            {
                return Fail(SaleFailureReasons.SaleNotCollectable,
                    "This sale has no outstanding balance / لا يوجد رصيد مستحق على عملية البيع");
            }

            var allocated = await _dbContext.PaymentTransactions
                .Where(payment => payment.TenantId == tenantId
                    && payment.SaleId == saleId
                    && payment.Status == "success"
                    && payment.Amount > 0m)
                .SumAsync(payment => (decimal?)payment.Amount) ?? 0m;
            var adjustments = await _dbContext.SaleAdjustments
                .Where(adjustment => adjustment.TenantId == tenantId
                    && adjustment.SaleId == saleId
                    && adjustment.Status == "posted")
                .SumAsync(adjustment => (decimal?)adjustment.Amount) ?? 0m;
            var canonicalDue = Math.Max(0m, RoundHalfUp(sale.Total - allocated - adjustments));
            if (Math.Abs(sale.AmountDue - canonicalDue) > 0.01m)
            {
                return Fail("SALE_RECONCILIATION_REQUIRED",
                    "The sale balance does not reconcile with its payment and adjustment records / رصيد البيع لا يتطابق مع سجلات الدفع والتسويات");
            }

            if (request.Amount > canonicalDue)
                return Fail(SaleFailureReasons.PaymentExceedsAmountDue,
                    "Payment amount exceeds the outstanding balance / مبلغ الدفع يتجاوز الرصيد المستحق");

            var staffUserIdStr = staffUserId.ToString();
            var staffUser = await _dbContext.AppUsers
                .FirstOrDefaultAsync(u => u.UserId == staffUserIdStr && u.TenantId == tenantId);

            if (staffUser == null)
                return Fail(SaleFailureReasons.StaffUserNotFound, "Staff user not found / المستخدم غير موجود");

            Guid? shiftId = null;
            if (method == "cash")
            {
                shiftId = await _shiftService.GetCurrentOpenShiftIdAsync(staffUserId, tenantId);
                if (shiftId == null)
                    return Fail(SaleFailureReasons.OpenShiftRequired,
                        "An open shift is required to accept cash payments / يجب فتح وردية لقبول مدفوعات نقدية");
            }

            var membershipId = await _dbContext.PaymentTransactions
                .Where(p => p.SaleId == saleId)
                .Select(p => (Guid?)p.MembershipId)
                .FirstOrDefaultAsync();

            sale.AmountDue = Math.Max(0m, RoundHalfUp(canonicalDue - request.Amount));
            if (sale.AmountDue == 0m)
                sale.Status = "completed";
            sale.UpdatedAtUtc = DateTime.UtcNow;

            var payment = new PaymentTransaction
            {
                TenantId = tenantId,
                MemberId = sale.MemberId,
                MembershipId = membershipId,
                Gateway = method,
                ExternalRef = $"POS:{saleId}:{Guid.NewGuid():N}",
                Amount = request.Amount,
                Currency = "EGP",
                Status = "success",
                SettlementStatus = method == "cash" ? "settled" : "pending",
                SettledAtUtc = method == "cash" ? DateTime.UtcNow : null,
                PaidAtUtc = DateTime.UtcNow,
                SaleId = saleId,
                ReceivedByUserId = staffUser.Id,
                ShiftId = shiftId,
                Method = method
            };
            _dbContext.PaymentTransactions.Add(payment);

            await _dbContext.SaveChangesAsync();

            // Debt-payment receipt: points at the original invoice (no new invoice number is
            // generated for the common case) with ?paymentId= so the renderer adds a
            // "Payment Received" section. Most tenants leave invoice_per_payment=false; markets that
            // need a distinct legal document per payment opt in via that tenant setting instead.
            var originalInvoiceId = await _dbContext.Invoices
                .Where(i => i.SaleId == saleId && i.Type == "invoice")
                .Select(i => (Guid?)i.Id)
                .FirstOrDefaultAsync();

            string? receiptUrl = null;
            if (originalInvoiceId.HasValue)
                receiptUrl = $"/api/invoices/{originalInvoiceId.Value}/receipt-html?paymentId={payment.Id}";

            var tenant = await _dbContext.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId);
            if (GetSettingBool(tenant?.Settings, TenantSettingsKeys.InvoicePerPayment, false))
                await _invoiceService.EnqueueForSale(saleId);

            // The payment already committed — a failure recording the drawer movement must not make
            // the caller think the payment itself failed (mirrors CreateSaleAsync's same pattern).
            if (shiftId.HasValue && method == "cash")
            {
                var movement = await _shiftService.RecordMovementAsync(
                    shiftId.Value, "sale", request.Amount, saleId, null, staffUserId, tenantId);
                if (!movement.IsSuccess)
                    return Fail("CASH_MOVEMENT_FAILED",
                        movement.Error ?? "Failed to record the cash movement / فشل تسجيل الحركة النقدية");
            }

            if (transaction != null)
                await transaction.CommitAsync();

            return Result<SaleResponse>.Success(new SaleResponse
            {
                SaleId = sale.Id,
                MembershipId = membershipId,
                IsReplay = false,
                InvoiceStatus = "not_applicable",
                Totals = new SaleTotalsDto
                {
                    Subtotal = sale.Subtotal,
                    Discount = sale.DiscountAmount,
                    Tax = sale.TaxAmount,
                    Total = sale.Total,
                    Paid = sale.Total - sale.AmountDue,
                    AmountDue = sale.AmountDue
                },
                Warnings = new List<string>(),
                ReceiptUrl = receiptUrl
            });
        }
        catch (Exception ex)
        {
            if (transaction != null)
                await transaction.RollbackAsync();
            _logger.LogError(ex, "Error recording payment for sale {SaleId}", saleId);
            return Result<SaleResponse>.Failure("Failed to record payment / فشل تسجيل الدفعة", ex.Message);
        }
        finally
        {
            if (transaction != null)
                await transaction.DisposeAsync();
        }
    }

    // ========================================================================
    // PRIVATE HELPERS
    // ========================================================================

    private async Task<Result<SaleResponse>> BuildReplayResponseAsync(Guid saleId, Guid tenantId)
    {
        var sale = await _dbContext.Sales.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == saleId && s.TenantId == tenantId);

        if (sale == null)
            return Result<SaleResponse>.Failure(
                "Original sale for this idempotency key could not be found / تعذر العثور على عملية البيع الأصلية لهذا المفتاح");

        var membershipId = await _dbContext.PaymentTransactions
            .AsNoTracking()
            .Where(p => p.SaleId == saleId)
            .Select(p => (Guid?)p.MembershipId)
            .FirstOrDefaultAsync();

        var response = new SaleResponse
        {
            SaleId = sale.Id,
            MembershipId = membershipId,
            IsReplay = true,
            InvoiceStatus = sale.Total > 0 ? "queued" : "skipped",
            Totals = new SaleTotalsDto
            {
                Subtotal = sale.Subtotal,
                Discount = sale.DiscountAmount,
                Tax = sale.TaxAmount,
                Total = sale.Total,
                Paid = sale.Total - sale.AmountDue,
                AmountDue = sale.AmountDue
            },
            Warnings = new List<string>()
        };
        if (sale.Total > 0)
            await AttachInvoiceRefAsync(response, sale.Id);
        return Result<SaleResponse>.Success(response);
    }

    private async Task AttachInvoiceRefAsync(SaleResponse response, Guid saleId)
    {
        var idResult = await _invoiceService.GetOriginalInvoiceIdForSaleAsync(saleId);
        if (!idResult.IsSuccess)
            return;

        var invResult = await _invoiceService.GetByIdAsync(idResult.Data);
        if (!invResult.IsSuccess || invResult.Data is null)
            return;

        response.InvoiceId = invResult.Data.Id;
        response.InvoiceNumber = invResult.Data.InvoiceNumber;
        response.InvoiceStatus = "ready";
        response.ReceiptUrl = $"/api/invoices/{invResult.Data.Id}/receipt-html";
    }

    /// <summary>SUM(Amount) over a member's credit ledger, UPDLOCK+HOLDLOCK'd so it's race-safe
    /// against a concurrent spend of the same balance — UPDLOCK alone was not sufficient to
    /// serialize two concurrent SUM-aggregate reads under READ COMMITTED (confirmed empirically:
    /// both readers could see the pre-spend balance before either write committed); HOLDLOCK forces
    /// the lock to be held SERIALIZABLE-style for the query's duration, closing that window.
    /// Duplicated from RefundService's identical query rather than taking a cross-service dependency
    /// for one raw-SQL helper (matches this codebase's existing pattern of small per-service helpers,
    /// e.g. GetSettingDecimal repeated in several services).</summary>
    private async Task<decimal> GetMemberCreditBalanceLockedAsync(Guid memberId, Guid tenantId)
    {
        var results = await _dbContext.Database
            .SqlQuery<decimal>(
                $"SELECT ISNULL(SUM(Amount), 0) FROM member_credits WITH (UPDLOCK, HOLDLOCK) WHERE MemberId = {memberId} AND TenantId = {tenantId} AND IsDeleted = 0")
            .ToListAsync();

        return results.Count > 0 ? results[0] : 0m;
    }

    private static Result<SaleResponse> Fail(string code, string message) =>
        Result<SaleResponse>.Failure($"{code}|{message}");

    private static DateOnly GetTodayCairo() =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, CairoTimeZone));

    private static decimal RoundHalfUp(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static string ComputeResponseHash(Guid tenantId, string idempotencyKey, Guid saleId)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{tenantId}|{idempotencyKey}|{saleId}"));
        return Convert.ToHexString(bytes);
    }

    private static bool GetSettingBool(string? settingsJson, string key, bool defaultValue)
    {
        if (string.IsNullOrWhiteSpace(settingsJson))
            return defaultValue;

        try
        {
            using var doc = JsonDocument.Parse(settingsJson);
            return doc.RootElement.TryGetProperty(key, out var value) &&
                   value.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? value.GetBoolean()
                : defaultValue;
        }
        catch (JsonException)
        {
            return defaultValue;
        }
    }

    private static decimal GetSettingDecimal(string? settingsJson, string key, decimal defaultValue)
    {
        if (string.IsNullOrWhiteSpace(settingsJson))
            return defaultValue;

        try
        {
            using var doc = JsonDocument.Parse(settingsJson);
            return doc.RootElement.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number
                ? value.GetDecimal()
                : defaultValue;
        }
        catch (JsonException)
        {
            return defaultValue;
        }
    }
}
