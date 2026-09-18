namespace GMS.Application.Interfaces;

using GMS.Application.Common;
using GMS.Application.DTOs.Auth;

public interface ILocalOwnerRecoveryService
{
    Task<OwnerRecoveryContextResponse> GetContextAsync(CancellationToken cancellationToken = default);
    Task<Result<OwnerRecoveryStatusResponse>> StartAsync(CancellationToken cancellationToken = default);
    Task<OwnerRecoveryStatusResponse> GetStatusAsync(CancellationToken cancellationToken = default);
    Task<Result<OwnerRecoveryStatusResponse>> SubmitCodeAsync(string recoveryCode, CancellationToken cancellationToken = default);
    Task<Result<OwnerRecoveryStatusResponse>> CompleteAsync(CompleteOwnerRecoveryRequest request, CancellationToken cancellationToken = default);
    Task<OwnerRecoveryStatusResponse> CancelAsync(CancellationToken cancellationToken = default);
}
