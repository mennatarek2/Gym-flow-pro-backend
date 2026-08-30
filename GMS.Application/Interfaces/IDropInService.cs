namespace GMS.Application.Interfaces;

using GMS.Application.Common;

public interface IDropInService
{
    /// <summary>
    /// Purchase a drop-in valid for ONE specific session. Creates a Sale with a 'drop_in'
    /// line (existing Sales architecture). Returns the sale id to attach to the booking.
    /// </summary>
    Task<Result<Guid>> PurchaseDropInAsync(
        Guid tenantId, Guid? memberId, string? guestName, string? guestPhone,
        Guid sessionId, Guid soldByUserId, decimal? amountPaid = null,
        string paymentMethod = "cash", CancellationToken ct = default);
}
