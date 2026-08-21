namespace GMS.Application.Interfaces;

using GMS.Application.Common;
using GMS.Application.DTOs.Members;
using GMS.Core.Entities;

/// <summary>Staff-issued one-time Member App activation codes.</summary>
public interface IMemberAppActivationService
{
    Task<Result<MemberAppActivationCodeResponse>> GenerateAsync(
        Guid memberId,
        Guid? createdByIdentityUserId,
        CancellationToken cancellationToken = default);

    Task<MemberAppAccessStatusDto> GetStatusAsync(
        Guid memberId,
        Guid tenantId,
        Guid? appUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Locate a still-active code for the tenant, mark consumed (concurrency-safe), return member.
    /// Caller must run inside an ambient transaction if pairing with token issuance.
    /// </summary>
    Task<Result<GymMember>> ConsumeAsync(
        Guid tenantId,
        string activationCodePlaintext,
        CancellationToken cancellationToken = default);
}
