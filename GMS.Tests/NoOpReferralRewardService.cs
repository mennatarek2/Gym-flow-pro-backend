namespace GMS.Tests;

using GMS.Application.Interfaces;

internal sealed class NoOpReferralRewardService : IReferralRewardService
{
    public Task CreateHoldsForConvertedInvitationAsync(
        Guid tenantId, Guid invitationId, Guid? saleId, decimal saleAmount, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<int> ProcessDueHoldsAsync(CancellationToken ct = default)
        => Task.FromResult(0);

    public Task HandleConvertingSaleRefundedAsync(Guid tenantId, Guid saleId, CancellationToken ct = default)
        => Task.CompletedTask;
}
