namespace GMS.Application.Services;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using GMS.Application.Common;
using GMS.Application.DTOs.Auth;
using GMS.Application.Interfaces;
using GMS.Core.Entities;
using GMS.Core.Entities.Identity;
using GMS.Core.Interfaces;
using GMS.Infrastructure.Persistence;

/// <summary>
/// Implements authentication logic: staff login, token refresh, and member OTP flow.
/// Uses ASP.NET Core Identity for credential validation and ITokenService for JWT generation.
/// </summary>
public class AuthService : IAuthService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly GymFlowProDbContext _dbContext;
    private readonly ITokenService _tokenService;
    private readonly IOtpService _otpService;
    private readonly OtpCacheService _otpCacheService;
    private readonly IOtpDeliveryStrategy _otpDeliveryStrategy;
    private readonly IMemberAppActivationService _memberAppActivation;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthService> _logger;
    private readonly IPermissionProvider _permissionProvider;
    private readonly IPermissionCacheService _permissionCacheService;
    private readonly ISubscriptionAccessService _subscriptionAccess;

    public AuthService(
        UserManager<ApplicationUser> userManager,
        GymFlowProDbContext dbContext,
        ITokenService tokenService,
        IOtpService otpService,
        OtpCacheService otpCacheService,
        IOtpDeliveryStrategy otpDeliveryStrategy,
        IMemberAppActivationService memberAppActivation,
        IConfiguration configuration,
        ILogger<AuthService> logger,
        IPermissionProvider permissionProvider,
        IPermissionCacheService permissionCacheService,
        ISubscriptionAccessService subscriptionAccess)
    {
        _userManager = userManager;
        _dbContext = dbContext;
        _tokenService = tokenService;
        _otpService = otpService;
        _otpCacheService = otpCacheService;
        _otpDeliveryStrategy = otpDeliveryStrategy;
        _memberAppActivation = memberAppActivation;
        _configuration = configuration;
        _logger = logger;
        _permissionProvider = permissionProvider;
        _permissionCacheService = permissionCacheService;
        _subscriptionAccess = subscriptionAccess;
    }

    /// <summary>
    /// Resolves the user's effective permissions, preferring the Redis-backed cache
    /// (see <see cref="IPermissionCacheService"/>) over recomputing from role membership.
    /// </summary>
    private async Task<IReadOnlySet<string>> ResolvePermissionsAsync(
        ApplicationUser user,
        Guid tenantId,
        IList<string> roles,
        string? tenantSettingsJson)
    {
        var cached = await _permissionCacheService.GetAsync(tenantId, user.Id);
        if (cached != null)
            return cached;

        var settingsJson = tenantSettingsJson;
        if (settingsJson == null)
        {
            settingsJson = await _dbContext.Tenants
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(t => t.Id == tenantId)
                .Select(t => t.Settings)
                .FirstOrDefaultAsync();
        }

        var overlay = RolePermissionResolver.ParseOverlay(settingsJson);
        var permissions = RolePermissionResolver.Resolve(roles, _permissionProvider, overlay);
        await _permissionCacheService.SetAsync(tenantId, user.Id, permissions);
        return permissions;
    }

    public async Task<Result<LoginResponse>> LoginAsync(LoginRequest request, string? ipAddress = null)
    {
        // 1. Resolve tenant by gym code
        var tenant = await _dbContext.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.GymCode == request.GymCode && !t.IsDeleted);

        if (tenant == null)
            return Result<LoginResponse>.Failure("Invalid gym code.");

        if (!tenant.IsActive)
            return Result<LoginResponse>.Failure("This gym is currently inactive. Contact support.");

        // 2. Find user by email
        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user == null)
            return Result<LoginResponse>.Failure("Invalid email or password.");

        // 3. Verify user belongs to this tenant
        if (user.TenantId != tenant.Id)
            return Result<LoginResponse>.Failure("Invalid email or password.");

        // 4. Check user is active
        if (!user.IsActive)
            return Result<LoginResponse>.Failure("Your account is disabled. Contact your gym manager.");

        // 5. Validate password
        var isValidPassword = await _userManager.CheckPasswordAsync(user, request.Password);
        if (!isValidPassword)
            return Result<LoginResponse>.Failure("Invalid email or password.");

        // 5b. CP5 — suspended gym: block owner/manager/receptionist/trainer dashboard login
        // (check-in continues via existing tokens + TenantMiddleware allowlist during buffer).
        var roles = await _userManager.GetRolesAsync(user);
        if (IsStaffDashboardRole(roles))
        {
            var access = await _subscriptionAccess.GetAsync(tenant.Id);
            if (access?.IsSuspended == true)
            {
                return Result<LoginResponse>.Failure(
                    "SUBSCRIPTION_SUSPENDED|This gym subscription is suspended. Please pay the outstanding GymFlow invoice / الاشتراك موقوف — يرجى سداد فاتورة GymFlow");
            }
        }

        // 6. Generate tokens
        var permissions = await ResolvePermissionsAsync(user, tenant.Id, roles, tenant.Settings);
        var accessToken = await _tokenService.GenerateAccessTokenAsync(user, tenant.Id, tenant.GymCode, roles, permissions);
        var refreshToken = _tokenService.GenerateRefreshToken();

        // 7. Store refresh token
        var refreshTokenExpirationDays = int.Parse(
            _configuration["JwtSettings:RefreshTokenExpirationDays"] ?? "30");

        var refreshTokenEntity = new RefreshToken
        {
            TokenHash = _tokenService.HashToken(refreshToken),
            UserId = user.Id,
            TenantId = tenant.Id,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(refreshTokenExpirationDays),
            CreatedByIp = ipAddress
        };

        _dbContext.RefreshTokens.Add(refreshTokenEntity);

        // Successful password login only — token refresh is not a new login.
        // TenantContext is empty during anonymous login; ignore the AppUser global
        // tenant filter, then re-scope by Identity id AND the resolved tenant.
        var identityIdStr = user.Id.ToString();
        var appUser = await _dbContext.AppUsers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.UserId == identityIdStr && a.TenantId == tenant.Id);
        if (appUser != null)
            appUser.LastLoginAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("User {Email} logged in successfully for tenant {GymCode}.", user.Email, tenant.GymCode);

        var accessTokenExpirationMinutes = int.Parse(
            _configuration["JwtSettings:AccessTokenExpirationMinutes"] ?? "15");

        return Result<LoginResponse>.Success(new LoginResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(accessTokenExpirationMinutes),
            User = new UserInfo
            {
                Id = user.Id,
                Email = user.Email ?? string.Empty,
                FullName = $"{user.FirstName} {user.LastName}".Trim(),
                Role = roles.FirstOrDefault() ?? "Staff",
                TenantId = tenant.Id,
                GymCode = tenant.GymCode
            }
        });
    }

    public async Task<Result<LoginResponse>> RefreshTokenAsync(RefreshTokenRequest request, string? ipAddress = null)
    {
        // 1. Find the refresh token by hash
        var tokenHash = _tokenService.HashToken(request.RefreshToken);
        var storedToken = await _dbContext.RefreshTokens
            .IgnoreQueryFilters()
            .Include(rt => rt.User)
            .FirstOrDefaultAsync(rt => rt.TokenHash == tokenHash && !rt.IsDeleted);

        if (storedToken == null)
            return Result<LoginResponse>.Failure("Invalid refresh token.");

        // 2. Check if token is still active
        if (!storedToken.IsActive)
        {
            _logger.LogWarning("Attempted reuse of revoked/expired refresh token for user {UserId}.", storedToken.UserId);
            return Result<LoginResponse>.Failure("Refresh token is expired or revoked.");
        }

        // 3. Get user and tenant
        var user = storedToken.User!;
        if (!user.IsActive)
            return Result<LoginResponse>.Failure("Your account is disabled.");

        var tenant = await _dbContext.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == storedToken.TenantId && !t.IsDeleted);

        if (tenant == null || !tenant.IsActive)
            return Result<LoginResponse>.Failure("Tenant not found or inactive.");

        var rolesForRefresh = await _userManager.GetRolesAsync(user);
        if (IsStaffDashboardRole(rolesForRefresh))
        {
            var access = await _subscriptionAccess.GetAsync(tenant.Id);
            if (access?.IsSuspended == true)
            {
                return Result<LoginResponse>.Failure(
                    "SUBSCRIPTION_SUSPENDED|This gym subscription is suspended. Please pay the outstanding GymFlow invoice / الاشتراك موقوف — يرجى سداد فاتورة GymFlow");
            }
        }

        // 4. Rotate: revoke old token, create new one.
        // LastLoginAtUtc is NOT updated here — refresh is session continuation, not a new login.
        var newRefreshToken = _tokenService.GenerateRefreshToken();
        var newTokenHash = _tokenService.HashToken(newRefreshToken);

        storedToken.RevokedAtUtc = DateTime.UtcNow;
        storedToken.ReplacedByTokenHash = newTokenHash;

        var refreshTokenExpirationDays = int.Parse(
            _configuration["JwtSettings:RefreshTokenExpirationDays"] ?? "30");

        var newRefreshTokenEntity = new RefreshToken
        {
            TokenHash = newTokenHash,
            UserId = user.Id,
            TenantId = storedToken.TenantId,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(refreshTokenExpirationDays),
            CreatedByIp = ipAddress
        };

        _dbContext.RefreshTokens.Add(newRefreshTokenEntity);
        await _dbContext.SaveChangesAsync();

        // 5. Generate new access token
        var roles = await _userManager.GetRolesAsync(user);
        var permissions = await ResolvePermissionsAsync(user, tenant.Id, roles, tenant.Settings);
        var accessToken = await _tokenService.GenerateAccessTokenAsync(user, tenant.Id, tenant.GymCode, roles, permissions);

        var accessTokenExpirationMinutes = int.Parse(
            _configuration["JwtSettings:AccessTokenExpirationMinutes"] ?? "15");

        return Result<LoginResponse>.Success(new LoginResponse
        {
            AccessToken = accessToken,
            RefreshToken = newRefreshToken,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(accessTokenExpirationMinutes),
            User = new UserInfo
            {
                Id = user.Id,
                Email = user.Email ?? string.Empty,
                FullName = $"{user.FirstName} {user.LastName}".Trim(),
                Role = roles.FirstOrDefault() ?? "Staff",
                TenantId = tenant.Id,
                GymCode = tenant.GymCode
            }
        });
    }

    public async Task<Result> SendMemberOtpAsync(MemberOtpRequest request)
    {
        try
        {
            // 1. Resolve tenant by GymCode (case-insensitive)
            var tenant = await _dbContext.Tenants
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(t => t.GymCode.ToUpper() == request.GymCode.ToUpper() && !t.IsDeleted);

            if (tenant == null)
                return Result.Failure("Invalid gym code.");

            if (!tenant.IsActive)
                return Result.Failure("This gym is currently inactive. Contact support.");

            // 2. Find member by phone number in this tenant
            var member = await _dbContext.GymMembers
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(m => m.PhoneNumber == request.PhoneNumber
                                       && m.TenantId == tenant.Id
                                       && !m.IsDeleted
                                       && m.IsActive);

            if (member == null)
                return Result.Failure("No active member found with this phone number at this gym.");

            // 3. Check if member has email on file
            if (string.IsNullOrEmpty(member.Email))
                return Result.Failure(
                    "No email address on file. Please contact gym staff to add your email. " +
                    "/ لا يوجد بريد إلكتروني مسجل. تواصل مع موظفي الصالة لإضافته.");

            // 4. Generate OTP (or re-use existing if already cached and valid)
            var otp = _otpCacheService.GenerateAndStore(tenant.GymCode, request.PhoneNumber);

            // 5. Send OTP via email delivery strategy
            var maskedEmail = await _otpDeliveryStrategy.SendOtpAsync(member, otp, tenant.Name);

            var message = $"Verification code sent to {maskedEmail} / تم إرسال رمز التحقق إلى {maskedEmail}";
            return Result.Success(message);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Failed to send OTP: {Message}", ex.Message);
            return Result.Failure(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error sending OTP: {Message}", ex.Message);
            return Result.Failure("Failed to send verification code. Please try again.");
        }
    }

    public async Task<Result<LoginResponse>> VerifyMemberOtpAsync(MemberOtpVerifyRequest request, string? ipAddress = null)
    {
        try
        {
            // 1. Resolve tenant by GymCode (case-insensitive)
            var tenant = await _dbContext.Tenants
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(t => t.GymCode.ToUpper() == request.GymCode.ToUpper() && !t.IsDeleted);

            if (tenant == null)
                return Result<LoginResponse>.Failure("Invalid or expired verification code / رمز التحقق غير صحيح أو منتهي الصلاحية.");

            if (!tenant.IsActive)
                return Result<LoginResponse>.Failure("This gym is currently inactive.");

            // 2. Find member by phone number in this tenant
            // Return 401 not 404 — do not leak whether phone exists
            var member = await _dbContext.GymMembers
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(m => m.PhoneNumber == request.PhoneNumber
                                       && m.TenantId == tenant.Id
                                       && !m.IsDeleted);

            if (member == null)
                return Result<LoginResponse>.Failure("Invalid or expired verification code / رمز التحقق غير صحيح أو منتهي الصلاحية.");

            // 3. Validate and consume OTP
            var isValid = _otpCacheService.ValidateAndConsume(tenant.GymCode, request.PhoneNumber, request.Otp);
            if (!isValid)
                return Result<LoginResponse>.Failure("Invalid or expired verification code / رمز التحقق غير صحيح أو منتهي الصلاحية.");

            // 4. Find or create the Identity user for this member
            var user = await FindOrCreateMemberIdentityUserAsync(member, tenant);

            // 5. Generate tokens
            return await IssueMemberLoginResponseAsync(user, tenant, ipAddress, "OTP");
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Failed to verify OTP: {Message}", ex.Message);
            return Result<LoginResponse>.Failure("An error occurred. Please try again.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error verifying OTP: {Message}", ex.Message);
            return Result<LoginResponse>.Failure("An error occurred. Please try again.");
        }
    }

    /// <inheritdoc />
    public async Task<Result<LoginResponse>> ActivateMemberAppAsync(
        MemberActivateRequest request,
        string? ipAddress = null)
    {
        try
        {
            var gymCode = (request.GymCode ?? string.Empty).Trim();
            var tenant = await _dbContext.Tenants
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(t => t.GymCode.ToUpper() == gymCode.ToUpper() && !t.IsDeleted);

            if (tenant == null || !tenant.IsActive)
                return Result<LoginResponse>.Failure("Invalid or expired activation code.");

            var useTx = _dbContext.Database.IsRelational();
            if (useTx)
                await _dbContext.Database.BeginTransactionAsync();

            var consume = await _memberAppActivation.ConsumeAsync(tenant.Id, request.ActivationCode ?? string.Empty);
            if (!consume.IsSuccess || consume.Data == null)
            {
                if (useTx) await _dbContext.Database.RollbackTransactionAsync();
                return Result<LoginResponse>.Failure(consume.Error ?? "Invalid or expired activation code.");
            }

            var member = consume.Data;
            var user = await FindOrCreateMemberIdentityUserAsync(member, tenant);
            var login = await IssueMemberLoginResponseAsync(user, tenant, ipAddress, "activation-code");
            if (!login.IsSuccess)
            {
                if (useTx) await _dbContext.Database.RollbackTransactionAsync();
                return login;
            }

            if (useTx) await _dbContext.Database.CommitTransactionAsync();
            return login;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Member app activation failed: {Message}", ex.Message);
            return Result<LoginResponse>.Failure("Unable to activate this account.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during member app activation: {Message}", ex.Message);
            return Result<LoginResponse>.Failure("Unable to activate this account.");
        }
    }

    private async Task<Result<LoginResponse>> IssueMemberLoginResponseAsync(
        ApplicationUser user,
        Tenant tenant,
        string? ipAddress,
        string via)
    {
        var roles = await _userManager.GetRolesAsync(user);
        var permissions = await ResolvePermissionsAsync(user, tenant.Id, roles, tenant.Settings);
        var accessToken = await _tokenService.GenerateAccessTokenAsync(
            user, tenant.Id, tenant.GymCode, roles, permissions);
        var refreshToken = _tokenService.GenerateRefreshToken();

        var refreshTokenExpirationDays = int.Parse(
            _configuration["JwtSettings:RefreshTokenExpirationDays"] ?? "30");

        _dbContext.RefreshTokens.Add(new RefreshToken
        {
            TokenHash = _tokenService.HashToken(refreshToken),
            UserId = user.Id,
            TenantId = tenant.Id,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(refreshTokenExpirationDays),
            CreatedByIp = ipAddress
        });
        await _dbContext.SaveChangesAsync();

        var accessTokenExpirationMinutes = int.Parse(
            _configuration["JwtSettings:AccessTokenExpirationMinutes"] ?? "15");

        _logger.LogInformation(
            "Member Identity {UserId} authenticated via {Via} for tenant {GymCode}.",
            user.Id, via, tenant.GymCode);

        return Result<LoginResponse>.Success(new LoginResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(accessTokenExpirationMinutes),
            User = new UserInfo
            {
                Id = user.Id,
                Email = user.Email ?? string.Empty,
                FullName = $"{user.FirstName} {user.LastName}".Trim(),
                Role = "Member",
                TenantId = tenant.Id,
                GymCode = tenant.GymCode
            }
        });
    }

    /// <summary>
    /// Finds the Identity user linked to a GymMember, or creates one if it doesn't exist.
    /// Links <see cref="GymMember.AppUserId"/> to the domain <see cref="AppUser"/> row (FK to <c>app_users</c>),
    /// whose <see cref="AppUser.UserId"/> holds the Identity user id string — same pattern as staff seeding.
    /// </summary>
    private async Task<ApplicationUser> FindOrCreateMemberIdentityUserAsync(GymMember member, Tenant tenant)
    {
        if (member.AppUserId.HasValue)
        {
            var domainAppUser = await _dbContext.AppUsers
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == member.AppUserId.Value && !a.IsDeleted);

            if (domainAppUser != null)
            {
                var linkedIdentity = await _userManager.FindByIdAsync(domainAppUser.UserId);
                if (linkedIdentity != null)
                {
                    await EnsureMemberRoleAsync(linkedIdentity);
                    return linkedIdentity;
                }
            }
        }

        var email = !string.IsNullOrWhiteSpace(member.Email)
            ? member.Email.Trim()
            : $"{member.PhoneNumber}@member.gymflowpro.local";

        var existingIdentity = await FindMemberIdentityUserByLoginAsync(email);
        if (existingIdentity != null)
        {
            if (existingIdentity.TenantId != tenant.Id)
            {
                _logger.LogWarning(
                    "Member OTP: login {Email} already belongs to another tenant (member {MemberId}).",
                    email, member.Id);
                throw new InvalidOperationException("Email is already registered under another gym.");
            }

            await EnsureMemberRoleAsync(existingIdentity);
            var appUser = await FindOrCreateMemberAppUserAsync(tenant, member, existingIdentity);
            await LinkGymMemberToAppUserAsync(member, appUser.Id);
            return existingIdentity;
        }

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            PhoneNumber = member.PhoneNumber,
            PhoneNumberConfirmed = true,
            TenantId = tenant.Id,
            FirstName = member.FullName.Split(' ').FirstOrDefault() ?? member.FullName,
            LastName = string.Join(' ', member.FullName.Split(' ').Skip(1)),
            IsActive = true
        };

        var result = await _userManager.CreateAsync(user);
        if (!result.Succeeded)
        {
            var errorCodes = result.Errors.Select(e => e.Code).ToHashSet();
            if (errorCodes.Contains("DuplicateUserName") || errorCodes.Contains("DuplicateEmail"))
            {
                var recovered = await FindMemberIdentityUserByLoginAsync(email);
                if (recovered != null && recovered.TenantId == tenant.Id)
                {
                    _logger.LogInformation(
                        "Adopted existing Identity user for member {MemberId} after duplicate create (recovery path).",
                        member.Id);
                    await EnsureMemberRoleAsync(recovered);
                    var appUserRec = await FindOrCreateMemberAppUserAsync(tenant, member, recovered);
                    await LinkGymMemberToAppUserAsync(member, appUserRec.Id);
                    return recovered;
                }
            }

            _logger.LogError("Failed to create Identity user for member {MemberId}: {Errors}",
                member.Id, string.Join(", ", result.Errors.Select(e => e.Description)));

            throw new InvalidOperationException(
                $"Failed to create Identity user for member: {string.Join(", ", result.Errors.Select(e => e.Description))}");
        }

        await EnsureMemberRoleAsync(user);
        var newAppUser = await FindOrCreateMemberAppUserAsync(tenant, member, user);
        await LinkGymMemberToAppUserAsync(member, newAppUser.Id);
        return user;
    }

    /// <summary>
    /// Ensures a domain <see cref="AppUser"/> exists for this Identity user (member role).
    /// </summary>
    private async Task<AppUser> FindOrCreateMemberAppUserAsync(Tenant tenant, GymMember member, ApplicationUser identityUser)
    {
        var identityId = identityUser.Id.ToString();

        var existing = await _dbContext.AppUsers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.TenantId == tenant.Id && a.UserId == identityId && !a.IsDeleted);

        if (existing != null)
            return existing;

        var appEmail = !string.IsNullOrWhiteSpace(member.Email)
            ? member.Email.Trim()
            : identityUser.Email ?? $"{member.PhoneNumber}@member.gymflowpro.local";

        if (string.IsNullOrWhiteSpace(appEmail))
            appEmail = $"{member.PhoneNumber}@member.gymflowpro.local";

        var appUser = new AppUser
        {
            TenantId = tenant.Id,
            UserId = identityId,
            FirstName = identityUser.FirstName,
            LastName = identityUser.LastName,
            Email = appEmail,
            PhoneNumber = member.PhoneNumber ?? string.Empty,
            Role = "Member",
            IsActive = true
        };

        await _dbContext.AppUsers.AddAsync(appUser);
        await _dbContext.SaveChangesAsync();
        return appUser;
    }

    /// <summary>
    /// Locates an Identity user for member OTP login. Uses email first, then user name —
    /// partial/orphan users may have <see cref="ApplicationUser.UserName"/> set without a searchable normalized email.
    /// </summary>
    private async Task<ApplicationUser?> FindMemberIdentityUserByLoginAsync(string email)
    {
        var byEmail = await _userManager.FindByEmailAsync(email);
        if (byEmail != null)
            return byEmail;

        return await _userManager.FindByNameAsync(email);
    }

    private static bool IsStaffDashboardRole(IList<string> roles) =>
        roles.Any(r =>
            r.Equals("Owner", StringComparison.OrdinalIgnoreCase) ||
            r.Equals("Manager", StringComparison.OrdinalIgnoreCase) ||
            r.Equals("Trainer", StringComparison.OrdinalIgnoreCase) ||
            r.Equals("Receptionist", StringComparison.OrdinalIgnoreCase));

    private async Task EnsureMemberRoleAsync(ApplicationUser user)
    {
        if (await _userManager.IsInRoleAsync(user, "Member"))
            return;

        var roleResult = await _userManager.AddToRoleAsync(user, "Member");
        if (!roleResult.Succeeded)
        {
            var msg = string.Join(", ", roleResult.Errors.Select(e => e.Description));
            _logger.LogError("Failed to assign Member role to user {UserId}: {Errors}", user.Id, msg);
            throw new InvalidOperationException($"Failed to assign Member role: {msg}");
        }
    }

    private async Task LinkGymMemberToAppUserAsync(GymMember member, Guid appUserId)
    {
        member.AppUserId = appUserId;
        _dbContext.GymMembers.Update(member);
        await _dbContext.SaveChangesAsync();
    }
}
