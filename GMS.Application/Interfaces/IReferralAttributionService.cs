namespace GMS.Application.Interfaces;

using GMS.Application.Common;

/// <summary>
/// Staff attach + paid-activate attribution for referral invitations (no reward payout — INV-4).
/// </summary>
public interface IReferralAttributionService
{
    /// <summary>
    /// Resolves referring member and rejects self-referral / missing code before member create.
    /// </summary>
    Task<Result<Guid>> ResolveReferrerAsync(
        Guid tenantId,
        string? referralCode,
        Guid? referringMemberId,
        string guestPhoneNormalized,
        string? guestNationalIdPlain = null);

    /// <summary>
    /// Creates/updates a pending <c>referral</c> invitation pre-linked via ConvertedMemberId.
    /// No-op when both code and referringMemberId are empty.
    /// </summary>
    Task<Result> AttachPendingAsync(
        Guid tenantId,
        Guid convertedMemberId,
        string? referralCode,
        Guid? referringMemberId);

    /// <summary>
    /// Marks pending referral invitation converted when plan/amount are reward-eligible.
    /// Trials and day_pass never convert. Does not grant credits/days.
    /// </summary>
    Task TryConvertOnPaidActivateAsync(
        Guid tenantId,
        Guid convertedMemberId,
        Guid? saleId,
        decimal amountPaid,
        string planType,
        CancellationToken ct = default);
}
