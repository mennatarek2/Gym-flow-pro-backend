namespace GMS.Application.Interfaces;

using GMS.Application.Common;
using GMS.Application.DTOs.Trials;

/// <summary>
/// Free-trial issuance via a two-step phone-OTP flow: staff enters the prospect's details and
/// triggers an OTP to their phone (fraud/anti-abuse check happens here), then staff confirms with
/// the code the prospect read back to create the trial member + zero-price membership + sale.
/// </summary>
public interface ITrialService
{
    Task<Result<TrialInitiateResponse>> InitiateAsync(TrialInitiateRequest request, Guid tenantId);

    Task<Result<TrialConfirmResponse>> ConfirmAsync(TrialConfirmRequest request, Guid staffUserId, Guid tenantId);
}
