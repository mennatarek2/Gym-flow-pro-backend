namespace GMS.Platform.Interfaces;

using GMS.Platform.DTOs;

public interface ILocalOwnerRecoveryService
{
    Task<LocalOwnerRecoveryClientDto> RequestFromInstallationAsync(
        LocalOwnerRecoveryAuthRequest request,
        string? ipAddress,
        CancellationToken cancellationToken = default);

    Task<LocalOwnerRecoveryClientDto> PollFromInstallationAsync(
        LocalOwnerRecoveryAuthRequest request,
        CancellationToken cancellationToken = default);

    Task<LocalOwnerRecoveryClientDto> CompleteFromInstallationAsync(
        LocalOwnerRecoveryAuthRequest request,
        string? ipAddress,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LocalOwnerRecoveryListItemDto>> ListAsync(
        Guid? customerId,
        string? status,
        CancellationToken cancellationToken = default);

    Task<LocalOwnerRecoveryDetailDto?> GetAsync(
        Guid id,
        bool includeRecoveryCode,
        CancellationToken cancellationToken = default);

    Task<LocalOwnerRecoveryDetailDto> ImportChallengeAsync(
        ImportOwnerRecoveryChallengeRequest request,
        Guid actorPlatformUserId,
        CancellationToken cancellationToken = default);

    Task<LocalOwnerRecoveryDetailDto> ApproveAsync(
        Guid id,
        OwnerRecoveryDecisionRequest request,
        Guid actorPlatformUserId,
        CancellationToken cancellationToken = default);

    Task<LocalOwnerRecoveryDetailDto> RejectAsync(
        Guid id,
        OwnerRecoveryDecisionRequest request,
        Guid actorPlatformUserId,
        CancellationToken cancellationToken = default);

    Task<LocalOwnerRecoveryDetailDto> RevokeAsync(
        Guid id,
        OwnerRecoveryDecisionRequest request,
        Guid actorPlatformUserId,
        CancellationToken cancellationToken = default);
}
