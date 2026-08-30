namespace GMS.Platform.Services;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using GMS.Platform.Constants;
using GMS.Platform.DTOs;
using GMS.Platform.Entities;
using GMS.Platform.Interfaces;
using GMS.Platform.Persistence;

public class PlatformUserAdminService : IPlatformUserAdminService
{
    private const int MinPasswordLength = 10;

    private readonly PlatformDbContext _db;
    private readonly IPasswordHasher<PlatformAdminUser> _passwordHasher;
    private readonly IPlatformAuditService _audit;

    public PlatformUserAdminService(
        PlatformDbContext db,
        IPasswordHasher<PlatformAdminUser> passwordHasher,
        IPlatformAuditService audit)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _audit = audit;
    }

    public async Task<IReadOnlyList<PlatformUserDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        return await _db.PlatformAdminUsers
            .AsNoTracking()
            .OrderBy(u => u.FullName)
            .Select(u => Map(u))
            .ToListAsync(cancellationToken);
    }

    public async Task<(PlatformActionResult Result, PlatformUserDto? User)> CreateAsync(
        Guid actorPlatformUserId, CreatePlatformUserRequest request, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var email = (request.Email ?? string.Empty).Trim().ToLowerInvariant();
        var fullName = (request.FullName ?? string.Empty).Trim();
        var role = (request.Role ?? string.Empty).Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(fullName))
            return (PlatformActionResult.Fail("INVALID_REQUEST", "Email and full name are required."), null);
        if (!PlatformRoles.IsValid(role))
            return (PlatformActionResult.Fail("INVALID_ROLE", $"Role must be one of: {string.Join(", ", PlatformRoles.All)}."), null);
        if (string.IsNullOrEmpty(request.Password) || request.Password.Length < MinPasswordLength)
            return (PlatformActionResult.Fail("WEAK_PASSWORD", $"Password must be at least {MinPasswordLength} characters."), null);

        var exists = await _db.PlatformAdminUsers.AnyAsync(u => u.Email == email, cancellationToken);
        if (exists)
            return (PlatformActionResult.Fail("EMAIL_TAKEN", "A platform user with this email already exists."), null);

        var user = new PlatformAdminUser
        {
            Email = email,
            FullName = fullName,
            Role = role,
            MfaEnabled = false,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        user.PasswordHash = _passwordHasher.HashPassword(user, request.Password);

        _db.PlatformAdminUsers.Add(user);
        await _db.SaveChangesAsync(cancellationToken);

        await _audit.LogAsync(actorPlatformUserId, "platform_user.created", null, null, Map(user), ipAddress);

        return (PlatformActionResult.Ok(), Map(user));
    }

    public async Task<(PlatformActionResult Result, PlatformUserDto? User)> DisableAsync(
        Guid actorPlatformUserId, Guid targetId, string? ipAddress, CancellationToken cancellationToken = default)
    {
        if (targetId == actorPlatformUserId)
            return (PlatformActionResult.Fail("SELF_PROTECTED", "You cannot disable your own account."), null);

        var user = await _db.PlatformAdminUsers.FirstOrDefaultAsync(u => u.Id == targetId, cancellationToken);
        if (user == null)
            return (PlatformActionResult.Fail("NOT_FOUND", "Platform user not found."), null);
        if (!user.IsActive)
            return (PlatformActionResult.Fail("ALREADY_DISABLED", "This platform user is already disabled."), null);

        var before = Map(user);
        user.IsActive = false;
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync(actorPlatformUserId, "platform_user.disabled", null, before, Map(user), ipAddress);

        return (PlatformActionResult.Ok(), Map(user));
    }

    public async Task<(PlatformActionResult Result, PlatformUserDto? User)> ReactivateAsync(
        Guid actorPlatformUserId, Guid targetId, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var user = await _db.PlatformAdminUsers.FirstOrDefaultAsync(u => u.Id == targetId, cancellationToken);
        if (user == null)
            return (PlatformActionResult.Fail("NOT_FOUND", "Platform user not found."), null);
        if (user.IsActive)
            return (PlatformActionResult.Fail("ALREADY_ACTIVE", "This platform user is already active."), null);

        var before = Map(user);
        user.IsActive = true;
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync(actorPlatformUserId, "platform_user.reactivated", null, before, Map(user), ipAddress);

        return (PlatformActionResult.Ok(), Map(user));
    }

    public async Task<(PlatformActionResult Result, PlatformUserDto? User)> ChangeRoleAsync(
        Guid actorPlatformUserId, Guid targetId, string newRole, string? ipAddress, CancellationToken cancellationToken = default)
    {
        if (targetId == actorPlatformUserId)
            return (PlatformActionResult.Fail("SELF_PROTECTED", "You cannot change your own role."), null);

        var role = (newRole ?? string.Empty).Trim().ToLowerInvariant();
        if (!PlatformRoles.IsValid(role))
            return (PlatformActionResult.Fail("INVALID_ROLE", $"Role must be one of: {string.Join(", ", PlatformRoles.All)}."), null);

        var user = await _db.PlatformAdminUsers.FirstOrDefaultAsync(u => u.Id == targetId, cancellationToken);
        if (user == null)
            return (PlatformActionResult.Fail("NOT_FOUND", "Platform user not found."), null);

        var before = Map(user);
        user.Role = role;
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync(actorPlatformUserId, "platform_user.role_changed", null, before, Map(user), ipAddress);

        return (PlatformActionResult.Ok(), Map(user));
    }

    private static PlatformUserDto Map(PlatformAdminUser u) => new()
    {
        Id = u.Id,
        Email = u.Email,
        FullName = u.FullName,
        Role = u.Role,
        IsActive = u.IsActive,
        MfaEnabled = u.MfaEnabled,
        LastLoginAtUtc = u.LastLoginAtUtc,
        CreatedAtUtc = u.CreatedAtUtc
    };
}
