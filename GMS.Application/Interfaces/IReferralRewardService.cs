namespace GMS.Application.Interfaces;

/// <summary>Referral reward hold / grant / forfeit / reverse (INV-4). No cash payout.</summary>
public interface IReferralRewardService
{
    /// <summary>
    /// After invitation convert: create dual-sided pending_hold rows (subject to monthly cap).
    /// </summary>
    Task CreateHoldsForConvertedInvitationAsync(
        Guid tenantId,
        Guid invitationId,
        Guid? saleId,
        decimal saleAmount,
        CancellationToken ct = default);

    /// <summary>Grant all due pending_hold rewards (Hangfire). Returns grants performed.</summary>
    Task<int> ProcessDueHoldsAsync(CancellationToken ct = default);

    /// <summary>
    /// On full sale refund: forfeit pending_hold or reverse granted rewards for that sale.
    /// </summary>
    Task HandleConvertingSaleRefundedAsync(Guid tenantId, Guid saleId, CancellationToken ct = default);
}
