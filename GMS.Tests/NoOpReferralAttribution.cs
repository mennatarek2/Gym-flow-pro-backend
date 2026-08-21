namespace GMS.Tests;

using GMS.Application.Common;
using GMS.Application.Interfaces;

/// <summary>No-op referral attribution for tests that do not exercise INV-3.</summary>
internal sealed class NoOpReferralAttribution : IReferralAttributionService
{
    public Task<Result<Guid>> ResolveReferrerAsync(
        Guid tenantId, string? referralCode, Guid? referringMemberId,
        string guestPhoneNormalized, string? guestNationalIdPlain = null)
        => Task.FromResult(Result<Guid>.Failure("noop"));

    public Task<Result> AttachPendingAsync(
        Guid tenantId, Guid convertedMemberId, string? referralCode, Guid? referringMemberId)
        => Task.FromResult(Result.Success());

    public Task TryConvertOnPaidActivateAsync(
        Guid tenantId, Guid convertedMemberId, Guid? saleId, decimal amountPaid, string planType,
        CancellationToken ct = default)
        => Task.CompletedTask;
}
