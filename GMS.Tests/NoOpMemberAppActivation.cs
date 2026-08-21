namespace GMS.Tests;

using GMS.Application.Common;
using GMS.Application.DTOs.Members;
using GMS.Application.Interfaces;
using GMS.Core.Entities;

/// <summary>No-op activation service for tests that construct <see cref="GMS.Application.Services.MemberService"/> directly.</summary>
internal sealed class NoOpMemberAppActivation : IMemberAppActivationService
{
    public Task<Result<MemberAppActivationCodeResponse>> GenerateAsync(
        Guid memberId, Guid? createdByIdentityUserId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result<MemberAppActivationCodeResponse>.Failure("not-used-in-test"));

    public Task<MemberAppAccessStatusDto> GetStatusAsync(
        Guid memberId, Guid tenantId, Guid? appUserId, CancellationToken cancellationToken = default) =>
        Task.FromResult(new MemberAppAccessStatusDto { Status = "not_activated" });

    public Task<Result<GymMember>> ConsumeAsync(
        Guid tenantId, string activationCodePlaintext, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result<GymMember>.Failure("not-used-in-test"));
}
