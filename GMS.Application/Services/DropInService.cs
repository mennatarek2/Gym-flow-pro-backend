namespace GMS.Application.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using GMS.Application.Common;
using GMS.Core.Constants;
using GMS.Core.Entities;
using GMS.Infrastructure.Persistence;
using GMS.Application.Interfaces;
using GMS.Application.Validators;

/// <summary>
/// Drop-in purchase for ONE specific class session, reusing the existing Sales/Payments
/// architecture (a Sale with a 'drop_in' SaleLine referencing the activity). No second
/// payment system. The booking links back via ActivityBooking.SaleId.
/// </summary>
public class DropInService : IDropInService
{
    private readonly GymFlowProDbContext _db;
    private readonly IInvoiceService? _invoiceService;
    private readonly IShiftService? _shiftService;
    private readonly ILogger<DropInService> _logger;

    public DropInService(
        GymFlowProDbContext db,
        ILogger<DropInService> logger,
        IInvoiceService? invoiceService = null,
        IShiftService? shiftService = null)
    {
        _db = db;
        _invoiceService = invoiceService;
        _shiftService = shiftService;
        _logger = logger;
    }

    /// <summary>Compatibility overload for existing member drop-in callers.</summary>
    public Task<Result<Guid>> PurchaseDropInAsync(
        Guid tenantId, Guid memberId, Guid sessionId, Guid soldByUserId,
        decimal? amountPaid = null, string gateway = "cash", CancellationToken ct = default) =>
        PurchaseDropInAsync(
            tenantId, memberId, null, null, sessionId, soldByUserId, amountPaid, gateway, ct);

    public async Task<Result<Guid>> PurchaseDropInAsync(
        Guid tenantId, Guid? memberId, string? guestName, string? guestPhone,
        Guid sessionId, Guid soldByUserId, decimal? amountPaid = null,
        string paymentMethod = "cash", CancellationToken ct = default)
    {
        if (memberId == Guid.Empty)
            memberId = null;
        paymentMethod = (paymentMethod ?? string.Empty).Trim().ToLowerInvariant();
        if (!SalePaymentRequestValidator.ValidMethods.Contains(paymentMethod))
            return Result<Guid>.Failure("Invalid payment method / طريقة دفع غير صالحة");
        if (paymentMethod is not ("cash" or "account_credit"))
            return Result<Guid>.Failure(
                "Electronic drop-in payments must be authorized through a gateway before booking / يجب اعتماد مدفوعات الدخول الإلكتروني عبر البوابة قبل الحجز");

        guestName = guestName?.Trim();
        guestPhone = guestPhone?.Trim();
        if (!memberId.HasValue && (string.IsNullOrWhiteSpace(guestName) || string.IsNullOrWhiteSpace(guestPhone)))
            return Result<Guid>.Failure("Guest name and phone are required / اسم ورقم هاتف الضيف مطلوبان");
        if (!memberId.HasValue && (guestName!.Length > 200 || guestPhone!.Length > 30))
            return Result<Guid>.Failure("Guest name or phone is too long / اسم الضيف أو هاتفه طويل جداً");
        if (!memberId.HasValue && paymentMethod == "account_credit")
            return Result<Guid>.Failure("Account credit requires a member / رصيد الحساب يتطلب عضواً");

        var session = await _db.ActivitySessions.AsNoTracking()
            .Include(s => s.Activity)
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.TenantId == tenantId && !s.IsDeleted, ct);
        if (session == null)
            return Result<Guid>.Failure("Session not found / الحصة غير موجودة");
        if (session.Status == "cancelled")
            return Result<Guid>.Failure("Session is cancelled / الحصة ملغاة");
        if (session.EndsAtUtc <= DateTime.UtcNow)
            return Result<Guid>.Failure("Session already ended / انتهت الحصة");

        var price = session.Activity?.DropInPrice;
        if (!price.HasValue || price.Value <= 0)
            return Result<Guid>.Failure("Drop-in not available for this class / الدخول الفردي غير متاح لهذا النشاط");

        var member = memberId.HasValue
            ? await _db.GymMembers.AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == memberId.Value && m.TenantId == tenantId && !m.IsDeleted, ct)
            : null;
        if (memberId.HasValue && member == null)
            return Result<Guid>.Failure("Member not found / العضو غير موجود");

        // Sale.SoldByUserId is AppUser.Id (domain), not the Identity JWT sub. Callers pass the
        // Identity id — resolve the matching AppUser row (same-tenant) for the FK.
        var sellerAppUserId = await _db.AppUsers.AsNoTracking()
            .Where(u => u.TenantId == tenantId && u.UserId == soldByUserId.ToString() && !u.IsDeleted)
            .Select(u => (Guid?)u.Id)
            .FirstOrDefaultAsync(ct) ?? await _db.AppUsers.AsNoTracking()
            .Where(u => u.TenantId == tenantId && u.Id == soldByUserId && !u.IsDeleted)
            .Select(u => (Guid?)u.Id)
            .FirstOrDefaultAsync(ct);
        if (sellerAppUserId == null)
            return Result<Guid>.Failure("Staff user not found / لم يتم العثور على المستخدم");

        Guid? shiftId = null;
        if (paymentMethod == "cash")
        {
            shiftId = _shiftService != null
                ? await _shiftService.GetCurrentOpenShiftIdAsync(soldByUserId, tenantId)
                : await _db.Shifts.AsNoTracking()
                    .Where(sh => sh.TenantId == tenantId && sh.ClosedAt == null && !sh.IsDeleted)
                    .OrderByDescending(sh => sh.OpenedAt)
                    .Select(sh => (Guid?)sh.Id)
                    .FirstOrDefaultAsync(ct);
            if (shiftId == null)
                return Result<Guid>.Failure("No open shift for cash payment / لا توجد وردية مفتوحة للدفع النقدي");
        }

        // Idempotent re-purchase: reuse an existing paid, unconsumed drop-in sale for this
        // activity rather than double-charging on a retry/double-click. SaleLine.ReferenceId
        // is the Activity (matches CreateBookingAsync's validation and the line created below),
        // not the session — a drop-in sale is "access to one session of this activity" and gets
        // tied to a specific session only via ActivityBooking.SaleId. Must also skip a sale
        // that's already consumed by a prior booking, or a returning drop-in customer could
        // never buy a second visit: every purchase attempt would just return the old, spent sale.
        var candidateSales = await _db.Sales
            .Include(s => s.Lines)
            .Where(s => s.TenantId == tenantId
                        && s.MemberId == memberId
                        && s.GuestName == guestName
                        && s.GuestPhone == guestPhone
                        && s.Status != "refunded"
                        && s.AmountDue <= 0
                        && !s.IsDeleted
                        && s.Lines.Any(l => l.LineType == "drop_in" && l.ReferenceId == session.ActivityId))
            .ToListAsync(ct);
        foreach (var candidate in candidateSales)
        {
            var consumed = await _db.ActivityBookings.AsNoTracking()
                .AnyAsync(b => b.TenantId == tenantId && b.SaleId == candidate.Id && !b.IsDeleted
                               && (b.Status == ActivityBookingStatuses.Booked
                                   || b.Status == ActivityBookingStatuses.CheckedIn
                                   || b.Status == ActivityBookingStatuses.CancelledLate
                                   || b.Status == ActivityBookingStatuses.NoShow), ct);
            if (consumed)
                continue;

            _logger.LogInformation("Drop-in sale {SaleId} reused (unconsumed) for member {MemberId} session {SessionId}",
                candidate.Id, memberId, sessionId);
            return Result<Guid>.Success(candidate.Id);
        }

        var paid = amountPaid ?? price.Value;
        if (paid != price.Value)
            return Result<Guid>.Failure(
                "Full drop-in payment is required before booking / يجب دفع قيمة الدخول الفردي كاملة قبل الحجز");

        var sale = new Sale
        {
            TenantId = tenantId,
            MemberId = memberId,
            GuestName = memberId.HasValue ? null : guestName,
            GuestPhone = memberId.HasValue ? null : guestPhone,
            SoldByUserId = sellerAppUserId.Value,
            ShiftId = shiftId,
            Subtotal = price.Value,
            Total = price.Value,
            AmountDue = price.Value - paid,
            Status = "completed",
            CreatedAtUtc = DateTime.UtcNow
        };
        var externalRef = $"dropin:{sessionId:N}:{memberId?.ToString("N") ?? guestPhone}:{DateTime.UtcNow.Ticks}";
        _db.PaymentTransactions.Add(new PaymentTransaction
        {
            TenantId = tenantId,
            SaleId = sale.Id,
            MemberId = memberId,
            Gateway = paymentMethod,
            ExternalRef = externalRef, // globally unique per payment_transactions index
            Amount = paid,
            Status = "success",
            SettlementStatus = "settled",
            PaidAtUtc = DateTime.UtcNow,
            ShiftId = shiftId,
            ReceivedByUserId = sellerAppUserId.Value,
            Method = paymentMethod,
            CreatedAtUtc = DateTime.UtcNow
        });

        if (paymentMethod == "account_credit")
        {
            var creditBalance = await _db.MemberCredits
                .Where(c => c.TenantId == tenantId && c.MemberId == memberId!.Value && !c.IsDeleted)
                .SumAsync(c => (decimal?)c.Amount, ct) ?? 0m;
            if (creditBalance < paid)
                return Result<Guid>.Failure("Member's account credit balance is insufficient / رصيد العضو غير كافٍ");
        }

        _db.Sales.Add(sale);

        // SaleLine must exist for the booking-side validation (LineType 'drop_in' → activity).
        sale.Lines.Add(new SaleLine
        {
            TenantId = tenantId,
            LineType = "drop_in",
            ReferenceId = session.ActivityId,
            Description = $"Drop-in: {session.Activity!.Name}",
            Qty = 1,
            UnitPrice = price.Value,
            LineTotal = price.Value,
            CreatedAtUtc = DateTime.UtcNow
        });

        if (shiftId.HasValue && paymentMethod == "cash")
        {
            _db.CashMovements.Add(new CashMovement
            {
                TenantId = tenantId,
                ShiftId = shiftId.Value,
                Type = "sale",
                Amount = paid,
                ReferenceId = sale.Id,
                Reason = $"Drop-in: {session.Activity.Name}",
                CreatedByUserId = sellerAppUserId.Value
            });
        }

        await _db.SaveChangesAsync(ct);

        // Account credit is consumed only after the sale has its stable id.
        if (paymentMethod == "account_credit")
        {
            _db.MemberCredits.Add(new MemberCredit
            {
                TenantId = tenantId,
                MemberId = memberId!.Value,
                Amount = -paid,
                EntryType = "payment_use",
                ReferenceId = sale.Id,
                CreatedByUserId = sellerAppUserId.Value
            });
        }

        await _db.SaveChangesAsync(ct);

        // Paid drop-ins must always have a legal invoice. The job is a retry fallback if the
        // inline creation cannot allocate a number because of a transient database failure.
        if (_invoiceService != null)
        {
            try
            {
                await _invoiceService.CreateForSaleAsync(sale.Id);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Inline invoice creation failed for drop-in sale {SaleId}; retrying in Hangfire", sale.Id);
            }
            await _invoiceService.EnqueueForSale(sale.Id);
        }

        _logger.LogInformation("Drop-in sale {SaleId} created ({Amount} EGP) member {MemberId} guest {GuestName} session {SessionId}",
            sale.Id, paid, memberId, guestName, sessionId);
        return Result<Guid>.Success(sale.Id);
    }
}
