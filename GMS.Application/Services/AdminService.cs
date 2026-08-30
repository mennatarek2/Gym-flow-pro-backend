namespace GMS.Application.Services;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using GMS.Application.Common;
using GMS.Application.DTOs.Admin;
using GMS.Application.Interfaces;
using GMS.Core.Constants;
using GMS.Core.Entities;
using GMS.Core.Entities.Identity;
using GMS.Core.Interfaces;
using GMS.Infrastructure.Persistence;

/// <summary>
/// Admin service for staff user management.
/// Every AspNetUsers read/mutate is scoped by Id AND TenantId (no cross-tenant IDOR).
/// Staff API ids are ApplicationUser.Id (Identity / JWT sub), never AppUser.Id.
/// </summary>
public class AdminService : IAdminService
{
    private static readonly HashSet<string> StaffRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "Manager", "Trainer", "Receptionist"
    };

    private const string NotFoundMessage = "Staff user not found / المستخدم غير موجود";
    private const string OwnerProtectedMessage =
        "OWNER_PROTECTED|The gym owner cannot be demoted or deactivated through Staff Management / لا يمكن تغيير دور المالك أو إيقافه من إدارة الموظفين";
    private const string InvalidRoleMessage =
        "Role must be Manager, Trainer, or Receptionist / يجب أن يكون الدور Manager أو Trainer أو Receptionist";
    private const string EmailTakenMessage =
        "Email is already registered / البريد الإلكتروني مسجّل بالفعل";
    private const string UnknownDepartmentMessage =
        "Department must be Front Desk, Sales, Training, Management, Operations, or Other / يجب أن يكون القسم من القيم المعروفة";

    private readonly GymFlowProDbContext _dbContext;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<AdminService> _logger;
    private readonly IPermissionCacheService _permissionCacheService;
    private readonly ITierEnforcementService _tierEnforcement;
    private readonly IAuditService _auditService;
    private readonly IFileStorageService _files;

    public AdminService(
        GymFlowProDbContext dbContext,
        UserManager<ApplicationUser> userManager,
        ILogger<AdminService> logger,
        IPermissionCacheService permissionCacheService,
        ITierEnforcementService tierEnforcement,
        IAuditService auditService,
        IFileStorageService files)
    {
        _dbContext = dbContext;
        _userManager = userManager;
        _logger = logger;
        _permissionCacheService = permissionCacheService;
        _tierEnforcement = tierEnforcement;
        _auditService = auditService;
        _files = files;
    }

    /// <inheritdoc/>
    public async Task<Result<List<StaffListItemDto>>> GetStaffUsersAsync(Guid tenantId)
    {
        try
        {
            var users = await _dbContext.Users
                .Where(u => u.TenantId == tenantId)
                .OrderByDescending(u => u.CreatedAtUtc)
                .ToListAsync();

            var appUsers = await _dbContext.AppUsers
                .AsNoTracking()
                .Where(a => a.TenantId == tenantId)
                .ToListAsync();
            // UserId is ApplicationUser.Id.ToString(); seed/legacy rows may differ in casing.
            var appByIdentity = appUsers.ToDictionary(
                a => a.UserId,
                a => a,
                StringComparer.OrdinalIgnoreCase);

            var staffListItems = new List<StaffListItemDto>();

            foreach (var user in users)
            {
                var roles = await _userManager.GetRolesAsync(user);

                if (roles.Contains("Member") || !roles.Any())
                    continue;

                appByIdentity.TryGetValue(user.Id.ToString(), out var app);

                staffListItems.Add(MapListItem(user, roles.FirstOrDefault() ?? "staff", app));
            }

            _logger.LogInformation(
                "Retrieved {Count} staff users for tenant {TenantId}",
                staffListItems.Count, tenantId);

            return Result<List<StaffListItemDto>>.Success(staffListItems);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving staff users for {TenantId}", tenantId);
            return Result<List<StaffListItemDto>>.Failure(
                "Failed to retrieve staff users / فشل في جلب مستخدمي المنظمة",
                ex.Message);
        }
    }

    /// <inheritdoc/>
    public async Task<Result<StaffDetailDto>> GetStaffUserByIdAsync(Guid tenantId, Guid id)
    {
        try
        {
            var user = await FindStaffUserAsync(tenantId, id);
            if (user == null)
                return Result<StaffDetailDto>.Failure(NotFoundMessage);

            var roles = await _userManager.GetRolesAsync(user);
            if (roles.Contains("Member"))
                return Result<StaffDetailDto>.Failure(NotFoundMessage);

            return Result<StaffDetailDto>.Success(await MapDetailAsync(tenantId, user, roles));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving staff user {UserId} for tenant {TenantId}", id, tenantId);
            return Result<StaffDetailDto>.Failure(
                "Failed to retrieve staff user / فشل في جلب المستخدم",
                ex.Message);
        }
    }

    /// <inheritdoc/>
    public async Task<Result<StaffDetailDto>> CreateStaffUserAsync(Guid tenantId, CreateStaffRequest request)
    {
        try
        {
            var identityRole = NormalizeStaffRole(request.Role);
            if (identityRole == null)
            {
                if (string.Equals(request.Role, "owner", StringComparison.OrdinalIgnoreCase))
                {
                    return Result<StaffDetailDto>.Failure(
                        "Cannot create owner role via this endpoint / لا يمكن إنشاء دور المالك من خلال هذا الطلب");
                }

                return Result<StaffDetailDto>.Failure(InvalidRoleMessage);
            }

            var profile = TryParseProfile(request.PhoneNumber, request.JobTitle, request.Department, request.HireDate, request.Notes);
            if (profile.Error != null)
                return Result<StaffDetailDto>.Failure(profile.Error);

            var existingUser = await _dbContext.Users
                .FirstOrDefaultAsync(u => u.Email == request.Email && u.TenantId == tenantId);

            if (existingUser != null)
                return Result<StaffDetailDto>.Failure(EmailTakenMessage);

            var seatCheck = await _tierEnforcement.CheckCapAsync(tenantId, "staff_seats");
            if (!seatCheck.Allowed)
            {
                return Result<StaffDetailDto>.Failure(
                    "PLAN_LIMIT_EXCEEDED|Staff seat limit reached for this plan / تم الوصول لحد مقاعد الموظفين لهذه الخطة");
            }

            var nameParts = request.FullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var newUser = new ApplicationUser
            {
                UserName = request.Email,
                Email = request.Email,
                FirstName = nameParts.FirstOrDefault() ?? request.FullName,
                LastName = nameParts.Length > 1 ? string.Join(" ", nameParts.Skip(1)) : string.Empty,
                TenantId = tenantId,
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow,
                EmailConfirmed = true
            };

            var result = await _userManager.CreateAsync(newUser, request.Password);
            if (!result.Succeeded)
            {
                var errors = string.Join(", ", result.Errors.Select(e => e.Description));
                _logger.LogWarning("Failed to create user {Email}: {Errors}", request.Email, errors);
                var duplicate = result.Errors.Any(e =>
                    e.Code.Contains("Duplicate", StringComparison.OrdinalIgnoreCase)
                    || (e.Description?.Contains("already taken", StringComparison.OrdinalIgnoreCase) ?? false)
                    || (e.Description?.Contains("already in use", StringComparison.OrdinalIgnoreCase) ?? false));
                return Result<StaffDetailDto>.Failure(
                    duplicate ? EmailTakenMessage : "Failed to create staff user / فشل في إنشاء المستخدم",
                    errors);
            }

            await _userManager.AddToRoleAsync(newUser, identityRole);

            _dbContext.AppUsers.Add(new AppUser
            {
                TenantId = tenantId,
                UserId = newUser.Id.ToString(),
                FirstName = newUser.FirstName,
                LastName = newUser.LastName,
                Email = newUser.Email!,
                PhoneNumber = profile.Phone ?? string.Empty,
                Role = identityRole,
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow,
                StaffNumber = await AllocateStaffNumberAsync(tenantId),
                JobTitle = profile.JobTitle,
                Department = profile.Department,
                HireDate = profile.HireDate,
                Notes = profile.Notes
            });
            await _dbContext.SaveChangesAsync();

            await _auditService.LogAsync(
                "staff.create",
                "Staff",
                newUser.Id,
                before: null,
                after: new { identityUserId = newUser.Id, email = newUser.Email, role = identityRole });

            _logger.LogInformation(
                "Staff user created: {Email} with role {Role} for tenant {TenantId}",
                request.Email, identityRole, tenantId);

            return Result<StaffDetailDto>.Success(await MapDetailAsync(tenantId, newUser, new[] { identityRole }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating staff user {Email} for tenant {TenantId}",
                request.Email, tenantId);
            return Result<StaffDetailDto>.Failure(
                "Failed to create staff user / فشل في إنشاء المستخدم",
                ex.Message);
        }
    }

    /// <inheritdoc/>
    public async Task<Result<StaffDetailDto>> UpdateStaffUserAsync(Guid tenantId, Guid id, UpdateStaffRequest request)
    {
        try
        {
            var user = await FindStaffUserAsync(tenantId, id);
            if (user == null)
                return Result<StaffDetailDto>.Failure(NotFoundMessage);

            var currentRoles = await _userManager.GetRolesAsync(user);
            var currentRole = currentRoles.FirstOrDefault() ?? "staff";
            var isOwner = currentRoles.Contains("Owner", StringComparer.OrdinalIgnoreCase);

            var identityRole = string.IsNullOrWhiteSpace(request.Role)
                ? null
                : NormalizeStaffRole(request.Role);

            if (!string.IsNullOrWhiteSpace(request.Role) && identityRole == null)
                return Result<StaffDetailDto>.Failure(InvalidRoleMessage);

            var roleChanging = identityRole != null
                && !string.Equals(identityRole, currentRole, StringComparison.OrdinalIgnoreCase);
            var deactivating = user.IsActive && !request.IsActive;
            var reactivating = !user.IsActive && request.IsActive;

            if (isOwner && (roleChanging || deactivating))
                return Result<StaffDetailDto>.Failure(OwnerProtectedMessage);

            var profile = TryParseProfile(request.PhoneNumber, request.JobTitle, request.Department, request.HireDate, request.Notes);
            if (profile.Error != null)
                return Result<StaffDetailDto>.Failure(profile.Error);

            if (request.IsActive && !user.IsActive)
            {
                var seatCheck = await _tierEnforcement.CheckCapAsync(tenantId, "staff_seats");
                if (!seatCheck.Allowed)
                {
                    return Result<StaffDetailDto>.Failure(
                        "PLAN_LIMIT_EXCEEDED|Staff seat limit reached for this plan / تم الوصول لحد مقاعد الموظفين لهذه الخطة");
                }
            }

            var appUser = await _dbContext.AppUsers
                .FirstOrDefaultAsync(a => a.UserId == user.Id.ToString() && a.TenantId == tenantId);

            var before = new
            {
                fullName = $"{user.FirstName} {user.LastName}".Trim(),
                role = currentRole,
                isActive = user.IsActive,
                phone = appUser?.PhoneNumber,
                jobTitle = appUser?.JobTitle,
                department = appUser?.Department,
                hireDate = appUser?.HireDate
            };

            var nameParts = request.FullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            user.FirstName = nameParts.FirstOrDefault() ?? string.Empty;
            user.LastName = nameParts.Length > 1 ? string.Join(" ", nameParts.Skip(1)) : string.Empty;
            user.IsActive = request.IsActive;
            user.UpdatedAtUtc = DateTime.UtcNow;

            if (identityRole != null && roleChanging)
            {
                if (currentRoles.Any())
                    await _userManager.RemoveFromRolesAsync(user, currentRoles);

                await _userManager.AddToRoleAsync(user, identityRole);
                await _permissionCacheService.InvalidateAsync(tenantId, user.Id);
            }

            if (appUser != null)
            {
                appUser.FirstName = user.FirstName;
                appUser.LastName = user.LastName;
                appUser.IsActive = user.IsActive;
                appUser.UpdatedAtUtc = DateTime.UtcNow;
                if (identityRole != null)
                    appUser.Role = identityRole;
                ApplyProfile(appUser, request, profile);
                if (string.IsNullOrWhiteSpace(appUser.StaffNumber))
                    appUser.StaffNumber = await AllocateStaffNumberAsync(tenantId);
            }

            if (!request.IsActive)
                await RevokeRefreshTokensAsync(tenantId, id);

            await _dbContext.SaveChangesAsync();

            var afterRole = identityRole ?? currentRole;
            var after = new
            {
                fullName = $"{user.FirstName} {user.LastName}".Trim(),
                role = afterRole,
                isActive = user.IsActive,
                phone = appUser?.PhoneNumber,
                jobTitle = appUser?.JobTitle,
                department = appUser?.Department,
                hireDate = appUser?.HireDate
            };

            await _auditService.LogAsync("staff.update", "Staff", user.Id, before, after);

            if (roleChanging)
            {
                await _auditService.LogAsync(
                    "staff.role_change",
                    "Staff",
                    user.Id,
                    new { oldRole = currentRole, newRole = identityRole },
                    new { oldRole = currentRole, newRole = identityRole });
            }

            if (deactivating)
            {
                await _auditService.LogAsync(
                    "staff.deactivate",
                    "Staff",
                    user.Id,
                    new { previousStatus = "Active", newStatus = "Inactive" },
                    new { previousStatus = "Active", newStatus = "Inactive" });
            }
            else if (reactivating)
            {
                await _auditService.LogAsync(
                    "staff.reactivate",
                    "Staff",
                    user.Id,
                    new { previousStatus = "Inactive", newStatus = "Active" },
                    new { previousStatus = "Inactive", newStatus = "Active" });
            }

            _logger.LogInformation(
                "Staff user updated: {UserId} tenant {TenantId} (IsActive: {IsActive})",
                id, tenantId, request.IsActive);

            var roles = await _userManager.GetRolesAsync(user);
            return Result<StaffDetailDto>.Success(await MapDetailAsync(tenantId, user, roles));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating staff user {UserId} for tenant {TenantId}", id, tenantId);
            return Result<StaffDetailDto>.Failure(
                "Failed to update staff user / فشل في تحديث المستخدم",
                ex.Message);
        }
    }

    /// <inheritdoc/>
    public async Task<Result> DeleteStaffUserAsync(Guid tenantId, Guid id)
    {
        try
        {
            var user = await FindStaffUserAsync(tenantId, id);
            if (user == null)
                return Result.Failure(NotFoundMessage);

            var currentRoles = await _userManager.GetRolesAsync(user);
            if (currentRoles.Contains("Owner", StringComparer.OrdinalIgnoreCase))
                return Result.Failure(OwnerProtectedMessage);

            user.IsActive = false;
            user.UpdatedAtUtc = DateTime.UtcNow;

            var appUser = await _dbContext.AppUsers
                .FirstOrDefaultAsync(a => a.UserId == user.Id.ToString() && a.TenantId == tenantId);
            if (appUser != null)
            {
                appUser.IsActive = false;
                appUser.UpdatedAtUtc = DateTime.UtcNow;
            }

            await RevokeRefreshTokensAsync(tenantId, id);
            await _dbContext.SaveChangesAsync();

            await _auditService.LogAsync(
                "staff.deactivate",
                "Staff",
                user.Id,
                new { previousStatus = "Active", newStatus = "Inactive" },
                new { previousStatus = "Active", newStatus = "Inactive" });

            _logger.LogInformation("Staff user deactivated (soft): {UserId} tenant {TenantId}", id, tenantId);
            return Result.Success("Staff account deactivated / تم إيقاف حساب الموظف");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting staff user {UserId} for tenant {TenantId}", id, tenantId);
            return Result.Failure(
                "Failed to deactivate staff user / فشل في إيقاف المستخدم",
                ex.Message);
        }
    }

    /// <inheritdoc/>
    public async Task<Result> ResetStaffPasswordAsync(Guid tenantId, Guid id, string newPassword)
    {
        try
        {
            var user = await FindStaffUserAsync(tenantId, id);
            if (user == null)
                return Result.Failure(NotFoundMessage);

            var currentRoles = await _userManager.GetRolesAsync(user);
            if (currentRoles.Contains("Owner", StringComparer.OrdinalIgnoreCase))
                return Result.Failure(OwnerProtectedMessage);

            var resetToken = await _userManager.GeneratePasswordResetTokenAsync(user);
            var result = await _userManager.ResetPasswordAsync(user, resetToken, newPassword);

            if (!result.Succeeded)
            {
                var errors = string.Join(", ", result.Errors.Select(e => e.Description));
                _logger.LogWarning("Failed to reset password for {UserId}: {Errors}", id, errors);
                return Result.Failure(
                    "Failed to reset password / فشل في إعادة تعيين كلمة المرور",
                    errors);
            }

            await RevokeRefreshTokensAsync(tenantId, id);
            await _dbContext.SaveChangesAsync();

            await _auditService.LogAsync(
                "staff.password_reset",
                "Staff",
                user.Id,
                before: null,
                after: new { identityUserId = user.Id });

            _logger.LogInformation("Password reset for staff user: {UserId} tenant {TenantId}", id, tenantId);
            return Result.Success("Password reset successfully / تم إعادة تعيين كلمة المرور بنجاح");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resetting password for staff user {UserId} tenant {TenantId}", id, tenantId);
            return Result.Failure(
                "Failed to reset password / فشل في إعادة تعيين كلمة المرور",
                ex.Message);
        }
    }

    private async Task<ApplicationUser?> FindStaffUserAsync(Guid tenantId, Guid id)
    {
        return await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Id == id && u.TenantId == tenantId);
    }

    private async Task RevokeRefreshTokensAsync(Guid tenantId, Guid identityUserId)
    {
        var refreshTokens = await _dbContext.Set<RefreshToken>()
            .Where(rt => rt.UserId == identityUserId && rt.TenantId == tenantId && rt.RevokedAtUtc == null)
            .ToListAsync();

        foreach (var token in refreshTokens)
            token.RevokedAtUtc = DateTime.UtcNow;
    }

    private static string? NormalizeStaffRole(string? role)
    {
        if (string.IsNullOrWhiteSpace(role))
            return null;

        foreach (var allowed in StaffRoles)
        {
            if (string.Equals(allowed, role.Trim(), StringComparison.OrdinalIgnoreCase))
                return allowed;
        }

        return null;
    }

    private async Task<StaffDetailDto> MapDetailAsync(
        Guid tenantId,
        ApplicationUser user,
        IEnumerable<string> roles)
    {
        var identityId = user.Id.ToString();
        var appUser = (await _dbContext.AppUsers
            .AsNoTracking()
            .Where(a => a.TenantId == tenantId)
            .ToListAsync())
            .FirstOrDefault(a => string.Equals(a.UserId, identityId, StringComparison.OrdinalIgnoreCase));

        return new StaffDetailDto
        {
            Id = user.Id,
            AppUserId = appUser?.Id,
            FullName = $"{user.FirstName} {user.LastName}".Trim(),
            Email = user.Email ?? string.Empty,
            Role = roles.FirstOrDefault() ?? "staff",
            IsActive = user.IsActive,
            LastLoginAt = appUser?.LastLoginAtUtc,
            CreatedAtUtc = user.CreatedAtUtc,
            UpdatedAtUtc = user.UpdatedAtUtc,
            ProfilePhotoUrl = FirstPhoto(user.ProfilePhotoUrl, appUser?.ProfilePhotoUrl),
            PhoneNumber = EmptyToNull(appUser?.PhoneNumber),
            StaffNumber = appUser?.StaffNumber,
            JobTitle = appUser?.JobTitle,
            Department = appUser?.Department,
            HireDate = appUser?.HireDate,
            Notes = appUser?.Notes
        };
    }

    private static StaffListItemDto MapListItem(ApplicationUser user, string role, AppUser? app)
    {
        return new StaffListItemDto
        {
            Id = user.Id,
            AppUserId = app?.Id,
            FullName = $"{user.FirstName} {user.LastName}".Trim(),
            Email = user.Email ?? string.Empty,
            Role = role,
            IsActive = user.IsActive,
            LastLoginAt = app?.LastLoginAtUtc,
            CreatedAtUtc = user.CreatedAtUtc,
            ProfilePhotoUrl = FirstPhoto(user.ProfilePhotoUrl, app?.ProfilePhotoUrl),
            PhoneNumber = EmptyToNull(app?.PhoneNumber),
            StaffNumber = app?.StaffNumber,
            JobTitle = app?.JobTitle,
            Department = app?.Department,
            HireDate = app?.HireDate
        };
    }

    public async Task<Result<StaffDetailDto>> SetStaffPhotoAsync(
        Guid tenantId, Guid id, Stream image, string fileName, string contentType)
    {
        try
        {
            var user = await FindStaffUserAsync(tenantId, id);
            if (user == null)
                return Result<StaffDetailDto>.Failure(NotFoundMessage);

            var roles = await _userManager.GetRolesAsync(user);
            if (roles.Contains("Member"))
                return Result<StaffDetailDto>.Failure(NotFoundMessage);

            var folder = $"staff-photos-{tenantId:N}";
            var relativeUrl = await _files.UploadAsync(image, fileName, folder);
            user.ProfilePhotoUrl = relativeUrl;
            user.UpdatedAtUtc = DateTime.UtcNow;

            var appUser = await _dbContext.AppUsers
                .FirstOrDefaultAsync(a => a.UserId == user.Id.ToString() && a.TenantId == tenantId);
            if (appUser != null)
            {
                appUser.ProfilePhotoUrl = relativeUrl;
                appUser.UpdatedAtUtc = DateTime.UtcNow;
            }

            await _dbContext.SaveChangesAsync();
            await _auditService.LogAsync("staff.update", "Staff", user.Id, null, new { profilePhoto = true });
            return Result<StaffDetailDto>.Success(await MapDetailAsync(tenantId, user, roles));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading staff photo {UserId} tenant {TenantId}", id, tenantId);
            return Result<StaffDetailDto>.Failure("Could not save photo / تعذر حفظ الصورة", ex.Message);
        }
    }

    public async Task<Result<List<StaffActivityItemDto>>> GetStaffActivityAsync(Guid tenantId, Guid id, int take = 30)
    {
        try
        {
            var user = await FindStaffUserAsync(tenantId, id);
            if (user == null)
                return Result<List<StaffActivityItemDto>>.Failure(NotFoundMessage);

            var roles = await _userManager.GetRolesAsync(user);
            if (roles.Contains("Member"))
                return Result<List<StaffActivityItemDto>>.Failure(NotFoundMessage);

            var identityId = user.Id.ToString();
            var appUser = (await _dbContext.AppUsers
                .AsNoTracking()
                .Where(a => a.TenantId == tenantId)
                .ToListAsync())
                .FirstOrDefault(a => string.Equals(a.UserId, identityId, StringComparison.OrdinalIgnoreCase));

            var actorId = appUser?.Id;
            var limit = Math.Clamp(take, 1, 50);

            var q = _dbContext.AuditEvents.AsNoTracking()
                .Where(a => a.TenantId == tenantId && (
                    (a.EntityType == "Staff" && a.EntityId == user.Id)
                    || (actorId != null && a.ActorUserId == actorId)));

            var rows = await q
                .OrderByDescending(a => a.CreatedAtUtc)
                .Take(limit)
                .ToListAsync();

            var items = rows
                .GroupBy(a => a.Id)
                .Select(g => g.First())
                .OrderByDescending(a => a.CreatedAtUtc)
                .Select(a => new StaffActivityItemDto
                {
                    Id = a.Id,
                    Action = a.Action,
                    Label = LabelFor(a.Action),
                    EntityType = a.EntityType,
                    CreatedAtUtc = a.CreatedAtUtc,
                    AboutThisStaff = string.Equals(a.EntityType, "Staff", StringComparison.OrdinalIgnoreCase)
                                     && a.EntityId == user.Id
                })
                .ToList();

            return Result<List<StaffActivityItemDto>>.Success(items);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading staff activity {UserId} tenant {TenantId}", id, tenantId);
            return Result<List<StaffActivityItemDto>>.Failure(
                "Could not load activity / تعذر تحميل النشاط", ex.Message);
        }
    }

    private async Task<string> AllocateStaffNumberAsync(Guid tenantId)
    {
        var stolen = await _dbContext.AppUsers
            .IgnoreQueryFilters()
            .Where(a => a.TenantId == tenantId && a.StaffNumber != null && a.Role == "Member")
            .ToListAsync();
        if (stolen.Count > 0)
        {
            foreach (var row in stolen)
                row.StaffNumber = null;
            await _dbContext.SaveChangesAsync();
        }

        // Members are not staff. Never consume or collide with their rows.
        var existing = await _dbContext.AppUsers
            .IgnoreQueryFilters()
            .Where(a => a.TenantId == tenantId
                        && a.StaffNumber != null
                        && a.Role != "Member")
            .Select(a => a.StaffNumber!)
            .ToListAsync();

        var max = 0;
        foreach (var raw in existing)
        {
            if (raw.Length >= 4
                && raw.StartsWith("ST-", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(raw.AsSpan(3), out var n)
                && n > max)
            {
                max = n;
            }
        }

        return $"ST-{(max + 1):D4}";
    }

    private readonly record struct ParsedProfile(
        string? Error,
        string? Phone,
        string? JobTitle,
        string? Department,
        DateOnly? HireDate,
        string? Notes);

    private static ParsedProfile TryParseProfile(
        string? phone, string? jobTitle, string? department, DateOnly? hireDate, string? notes)
    {
        string? dept = null;
        if (!string.IsNullOrWhiteSpace(department))
        {
            dept = StaffDepartments.Normalize(department);
            if (dept == null)
                return new ParsedProfile(UnknownDepartmentMessage, null, null, null, null, null);
        }

        if (hireDate is DateOnly d)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            if (d.Year < 1970 || d > today.AddDays(1))
                return new ParsedProfile("Hire date is out of range / تاريخ التعيين غير صالح", null, null, null, null, null);
        }

        return new ParsedProfile(
            null,
            Clip(phone, 20),
            Clip(jobTitle, 80),
            dept,
            hireDate,
            Clip(notes, 2000));
    }

    private static void ApplyProfile(AppUser appUser, UpdateStaffRequest request, ParsedProfile profile)
    {
        if (request.PhoneNumber != null)
            appUser.PhoneNumber = profile.Phone ?? string.Empty;
        if (request.JobTitle != null)
            appUser.JobTitle = profile.JobTitle;
        if (request.Department != null)
            appUser.Department = profile.Department;
        if (request.HireDate.HasValue)
            appUser.HireDate = profile.HireDate;
        if (request.Notes != null)
            appUser.Notes = profile.Notes;
    }

    private static string? Clip(string? raw, int max)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        var t = raw.Trim();
        return t.Length <= max ? t : t[..max];
    }

    private static string? EmptyToNull(string? raw) =>
        string.IsNullOrWhiteSpace(raw) ? null : raw;

    private static string? FirstPhoto(string? a, string? b)
    {
        if (!string.IsNullOrWhiteSpace(a)) return a;
        if (!string.IsNullOrWhiteSpace(b)) return b;
        return null;
    }

    private static string LabelFor(string action) => action switch
    {
        "staff.create" => "Created account",
        "staff.update" => "Updated profile",
        "staff.role_change" => "Changed role",
        "staff.deactivate" => "Deactivated",
        "staff.reactivate" => "Reactivated",
        "staff.password_reset" => "Reset password",
        "checkin.manual" => "Checked in a member",
        "checkin.barcode" => "Checked in a member",
        "sale.discount.override" => "Overrode a discount",
        "refund.approved" => "Approved a refund",
        "refund.executed" => "Completed a refund",
        "refund.rejected" => "Rejected a refund",
        "product.create" => "Added a product",
        "product.update" => "Updated a product",
        "purchase_order.create" => "Created a purchase",
        "purchase_order.receive" => "Received stock",
        "stock_adjustment.post" => "Adjusted stock",
        "stock_transfer.create" => "Started a transfer",
        _ => action
    };
}
