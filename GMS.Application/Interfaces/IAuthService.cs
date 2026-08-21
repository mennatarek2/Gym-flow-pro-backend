namespace GMS.Application.Interfaces;

using GMS.Application.Common;
using GMS.Application.DTOs.Auth;

/// <summary>
/// Authentication service for staff login, token refresh, member OTP (legacy), and Member App activation.
/// </summary>
public interface IAuthService
{
    /// <summary>
    /// Authenticates a staff user with email + password and returns JWT tokens.
    /// </summary>
    Task<Result<LoginResponse>> LoginAsync(LoginRequest request, string? ipAddress = null);

    /// <summary>
    /// Refreshes an expired access token using a valid refresh token.
    /// Implements sliding rotation: old refresh token is revoked, new one issued.
    /// </summary>
    Task<Result<LoginResponse>> RefreshTokenAsync(RefreshTokenRequest request, string? ipAddress = null);

    /// <summary>
    /// Legacy Member App OTP send. Prefer <see cref="ActivateMemberAppAsync"/> for Stage 0.
    /// </summary>
    [Obsolete("Member App Stage 0 uses staff-issued activation codes via ActivateMemberAppAsync.")]
    Task<Result> SendMemberOtpAsync(MemberOtpRequest request);

    /// <summary>
    /// Legacy Member App OTP verify. Prefer <see cref="ActivateMemberAppAsync"/> for Stage 0.
    /// </summary>
    [Obsolete("Member App Stage 0 uses staff-issued activation codes via ActivateMemberAppAsync.")]
    Task<Result<LoginResponse>> VerifyMemberOtpAsync(MemberOtpVerifyRequest request, string? ipAddress = null);

    /// <summary>
    /// Member App Stage 0: Gym Code + one-time activation code → claim GymMember → JWT.
    /// </summary>
    Task<Result<LoginResponse>> ActivateMemberAppAsync(MemberActivateRequest request, string? ipAddress = null);
}
