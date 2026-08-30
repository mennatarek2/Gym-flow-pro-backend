namespace GMS.Tests.Platform;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using GMS.Application.Common;
using GMS.Application.DTOs.Admin;
using GMS.Application.DTOs.Audit;
using GMS.Application.Interfaces;
using GMS.Application.Services;
using GMS.Core.Entities;
using GMS.Core.Entities.Identity;
using GMS.Core.Interfaces;
using GMS.Infrastructure.Persistence;
using GMS.Infrastructure.Services;
using GMS.Platform.DTOs;
using GMS.Platform.Interfaces;

/// <summary>
/// P2.2 — service-level security matrix for the platform-facing tenant-staff wrapper. Covers the
/// parts of the matrix a service test CAN prove (Owner protection, tenant isolation, disable/
/// reactivate/role-change correctness, dual audit trail). Platform role-tier enforcement (items 1/2
/// of the matrix) is covered separately in PlatformTenantUsersAuthorizationTests, since only the
/// controller's [Authorize(Policy=...)] attributes enforce that — this service has no concept of
/// platform roles at all.
/// </summary>
public class PlatformTenantStaffServiceTests
{
    private sealed class NoopPermissionCache : IPermissionCacheService
    {
        public Task<IReadOnlySet<string>?> GetAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlySet<string>?>(null);
        public Task SetAsync(Guid tenantId, Guid userId, IReadOnlySet<string> permissions, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
        public Task InvalidateAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class NoopFiles : IFileStorageService
    {
        public Task<string> UploadAsync(Stream stream, string fileName, string folder) =>
            Task.FromResult($"/uploads/{folder}/{fileName}");
        public Task DeleteAsync(string fileUrl) => Task.CompletedTask;
        public Task<bool> ExistsAsync(string fileUrl) => Task.FromResult(false);
    }

    private sealed class AllowAllTiers : ITierEnforcementService
    {
        public Task<CapCheckResult> CheckCapAsync(Guid tenantId, string metric, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CapCheckResult { Allowed = true, Metric = metric });
    }

    private sealed class RecordingTenantAudit : IAuditService
    {
        public List<string> Actions { get; } = new();
        public Task LogAsync(string action, string? entityType = null, Guid? entityId = null, object? before = null, object? after = null, Guid? tenantIdOverride = null)
        {
            Actions.Add(action);
            return Task.CompletedTask;
        }
        public Task<Result<PagedResult<AuditEventDto>>> GetAuditEventsAsync(Guid tenantId, AuditEventQueryRequest query) =>
            Task.FromResult(Result<PagedResult<AuditEventDto>>.Failure("n/a"));
    }

    private sealed class RecordingPlatformAudit : IPlatformAuditService
    {
        public List<(Guid ActorId, string Action, Guid? TenantId, object? Before, object? After)> Events { get; } = new();

        public Task LogAsync(Guid actorPlatformUserId, string action, Guid? tenantId = null, object? before = null, object? after = null, string? ipAddress = null)
        {
            Events.Add((actorPlatformUserId, action, tenantId, before, after));
            return Task.CompletedTask;
        }

        public Task<PlatformPagedResult<PlatformAuditLogDto>> ListAsync(Guid? tenantId, string? action, DateOnly? from, DateOnly? to, int page, int pageSize, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by these tests.");
    }

    private sealed class UnusedStore : IUserStore<ApplicationUser>
    {
        public void Dispose() { }
        public Task<IdentityResult> CreateAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(IdentityResult.Success);
        public Task<IdentityResult> DeleteAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(IdentityResult.Success);
        public Task<ApplicationUser?> FindByIdAsync(string userId, CancellationToken cancellationToken) => Task.FromResult<ApplicationUser?>(null);
        public Task<ApplicationUser?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken) => Task.FromResult<ApplicationUser?>(null);
        public Task<string?> GetNormalizedUserNameAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(user.NormalizedUserName);
        public Task<string> GetUserIdAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(user.Id.ToString());
        public Task<string?> GetUserNameAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(user.UserName);
        public Task SetNormalizedUserNameAsync(ApplicationUser user, string? normalizedName, CancellationToken cancellationToken) { user.NormalizedUserName = normalizedName; return Task.CompletedTask; }
        public Task SetUserNameAsync(ApplicationUser user, string? userName, CancellationToken cancellationToken) { user.UserName = userName; return Task.CompletedTask; }
        public Task<IdentityResult> UpdateAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(IdentityResult.Success);
    }

    private sealed class FakeUserManager : UserManager<ApplicationUser>
    {
        public Dictionary<Guid, List<string>> Roles { get; } = new();

        public FakeUserManager()
            : base(
                new UnusedStore(),
                Microsoft.Extensions.Options.Options.Create(new IdentityOptions()),
                new PasswordHasher<ApplicationUser>(),
                Array.Empty<IUserValidator<ApplicationUser>>(),
                Array.Empty<IPasswordValidator<ApplicationUser>>(),
                new UpperInvariantLookupNormalizer(),
                new IdentityErrorDescriber(),
                null!,
                NullLogger<UserManager<ApplicationUser>>.Instance)
        {
        }

        public override Task<IList<string>> GetRolesAsync(ApplicationUser user)
        {
            IList<string> roles = Roles.TryGetValue(user.Id, out var list) ? list : new List<string>();
            return Task.FromResult(roles);
        }

        public override Task<IdentityResult> CreateAsync(ApplicationUser user, string password)
        {
            if (user.Id == Guid.Empty)
                user.Id = Guid.NewGuid();
            return Task.FromResult(IdentityResult.Success);
        }

        public override Task<IdentityResult> AddToRoleAsync(ApplicationUser user, string role)
        {
            if (!Roles.TryGetValue(user.Id, out var list))
            {
                list = new List<string>();
                Roles[user.Id] = list;
            }
            list.Clear();
            list.Add(role);
            return Task.FromResult(IdentityResult.Success);
        }

        public override Task<IdentityResult> RemoveFromRolesAsync(ApplicationUser user, IEnumerable<string> roles)
        {
            if (Roles.TryGetValue(user.Id, out var list))
                list.RemoveAll(r => roles.Contains(r, StringComparer.OrdinalIgnoreCase));
            return Task.FromResult(IdentityResult.Success);
        }

        public override Task<ApplicationUser?> FindByEmailAsync(string email) =>
            Task.FromResult<ApplicationUser?>(null);
    }

    private static GymFlowProDbContext CreateDb(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<GymFlowProDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var tenantContext = new TenantContext();
        tenantContext.SetTenant(tenantId, "Gym", "Africa/Cairo");
        return new GymFlowProDbContext(options, tenantContext);
    }

    private static (PlatformTenantStaffService Sut, FakeUserManager Users, RecordingTenantAudit TenantAudit, RecordingPlatformAudit PlatformAudit) CreateSut(GymFlowProDbContext db)
    {
        var users = new FakeUserManager();
        var tenantAudit = new RecordingTenantAudit();
        var platformAudit = new RecordingPlatformAudit();
        var admin = new AdminService(
            db,
            users,
            NullLogger<AdminService>.Instance,
            new NoopPermissionCache(),
            new AllowAllTiers(),
            tenantAudit,
            new NoopFiles());
        var sut = new PlatformTenantStaffService(admin, platformAudit);
        return (sut, users, tenantAudit, platformAudit);
    }

    private static ApplicationUser SeedStaff(GymFlowProDbContext db, Guid tenantId, FakeUserManager users, string role, bool active = true)
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = $"{role.ToLowerInvariant()}-{Guid.NewGuid():N}@gym.test",
            Email = $"{role.ToLowerInvariant()}-{Guid.NewGuid():N}@gym.test",
            FirstName = role,
            LastName = "Staff",
            TenantId = tenantId,
            IsActive = active,
            UpdatedAtUtc = DateTime.UtcNow.AddDays(-1),
            CreatedAtUtc = DateTime.UtcNow.AddDays(-10),
            SecurityStamp = Guid.NewGuid().ToString()
        };
        db.Users.Add(user);
        db.AppUsers.Add(new AppUser
        {
            TenantId = tenantId,
            UserId = user.Id.ToString(),
            FirstName = user.FirstName,
            LastName = user.LastName,
            Email = user.Email!,
            Role = role,
            IsActive = active,
            LastLoginAtUtc = DateTime.UtcNow.AddDays(-3)
        });
        users.Roles[user.Id] = new List<string> { role };
        return user;
    }

    // --- Owner protection ---------------------------------------------------------------------

    [Fact]
    public async Task Disable_CannotAccidentallyDisableTheOnlyOwner()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(tenantId);
        var (sut, users, _, platformAudit) = CreateSut(db);
        var owner = SeedStaff(db, tenantId, users, "Owner");
        await db.SaveChangesAsync();

        var result = await sut.DisableAsync(tenantId, owner.Id, Guid.NewGuid(), "Attempting to disable the owner account.", null);

        Assert.False(result.Success);
        Assert.Equal("OWNER_PROTECTED", result.ErrorCode);
        Assert.True((await db.Users.SingleAsync(u => u.Id == owner.Id)).IsActive);
        Assert.Empty(platformAudit.Events);
    }

    [Fact]
    public async Task ChangeRole_CannotDemoteTheOnlyOwner()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(tenantId);
        var (sut, users, _, platformAudit) = CreateSut(db);
        var owner = SeedStaff(db, tenantId, users, "Owner");
        await db.SaveChangesAsync();

        var result = await sut.ChangeRoleAsync(tenantId, owner.Id, "Manager", Guid.NewGuid(), "Attempting to demote the owner.", null);

        Assert.False(result.Success);
        Assert.Equal("OWNER_PROTECTED", result.ErrorCode);
        Assert.Contains("Owner", await users.GetRolesAsync(owner));
        Assert.Empty(platformAudit.Events);
    }

    // --- Tenant isolation -----------------------------------------------------------------------

    [Fact]
    public async Task Disable_TenantAStaffId_AgainstTenantB_IsRejectedAsNotFound()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        await using var db = CreateDb(tenantA);
        var (sut, users, _, platformAudit) = CreateSut(db);
        var staffInA = SeedStaff(db, tenantA, users, "Trainer");
        await db.SaveChangesAsync();

        var result = await sut.DisableAsync(tenantB, staffInA.Id, Guid.NewGuid(), "Cross-tenant probe should fail.", null);

        Assert.False(result.Success);
        Assert.Equal("NOT_FOUND", result.ErrorCode);
        Assert.True((await db.Users.SingleAsync(u => u.Id == staffInA.Id)).IsActive);
        Assert.Empty(platformAudit.Events);
    }

    [Fact]
    public async Task ChangeRole_TenantAStaffId_AgainstTenantB_IsRejectedAsNotFound()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        await using var db = CreateDb(tenantA);
        var (sut, users, _, _) = CreateSut(db);
        var staffInA = SeedStaff(db, tenantA, users, "Trainer");
        await db.SaveChangesAsync();

        var result = await sut.ChangeRoleAsync(tenantB, staffInA.Id, "Manager", Guid.NewGuid(), "Cross-tenant probe should fail.", null);

        Assert.False(result.Success);
        Assert.Equal("NOT_FOUND", result.ErrorCode);
    }

    // --- Disable / reactivate round trip -----------------------------------------------------

    [Fact]
    public async Task Disable_BlocksAuthentication_AndReactivate_RestoresIt()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(tenantId);
        var (sut, users, _, platformAudit) = CreateSut(db);
        var actorId = Guid.NewGuid();
        var trainer = SeedStaff(db, tenantId, users, "Trainer");
        db.Set<RefreshToken>().Add(new RefreshToken
        {
            UserId = trainer.Id,
            TenantId = tenantId,
            TokenHash = "abc",
            ExpiresAtUtc = DateTime.UtcNow.AddDays(10)
        });
        await db.SaveChangesAsync();

        var disable = await sut.DisableAsync(tenantId, trainer.Id, actorId, "Leaving the company effective today.", "10.0.0.1");
        Assert.True(disable.Success);

        // This mirrors AuthService.cs's `if (!user.IsActive)` login gate — the establishe
        // disable mechanism, not a second status field.
        Assert.False((await db.Users.SingleAsync(u => u.Id == trainer.Id)).IsActive);
        Assert.NotNull((await db.Set<RefreshToken>().SingleAsync()).RevokedAtUtc);

        var disableEvent = Assert.Single(platformAudit.Events);
        Assert.Equal("platform.tenant.staff_disable", disableEvent.Action);
        Assert.Equal(actorId, disableEvent.ActorId);
        Assert.Equal(tenantId, disableEvent.TenantId);

        var reactivate = await sut.ReactivateAsync(tenantId, trainer.Id, actorId, "Rehired, restoring access.", "10.0.0.1");
        Assert.True(reactivate.Success);
        Assert.True((await db.Users.SingleAsync(u => u.Id == trainer.Id)).IsActive);

        Assert.Equal(2, platformAudit.Events.Count);
        Assert.Equal("platform.tenant.staff_reactivate", platformAudit.Events[1].Action);
    }

    [Fact]
    public async Task Disable_AlreadyDisabled_IsRejected_NotDoubleAudited()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(tenantId);
        var (sut, users, _, platformAudit) = CreateSut(db);
        var trainer = SeedStaff(db, tenantId, users, "Trainer", active: false);
        await db.SaveChangesAsync();

        var result = await sut.DisableAsync(tenantId, trainer.Id, Guid.NewGuid(), "Redundant disable attempt.", null);

        Assert.False(result.Success);
        Assert.Equal("ALREADY_DISABLED", result.ErrorCode);
        Assert.Empty(platformAudit.Events);
    }

    // --- Role change ----------------------------------------------------------------------------

    [Fact]
    public async Task ChangeRole_UpdatesIdentityRole_AndAuditsBothOldAndNew()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(tenantId);
        var (sut, users, tenantAudit, platformAudit) = CreateSut(db);
        var actorId = Guid.NewGuid();
        var trainer = SeedStaff(db, tenantId, users, "Trainer");
        await db.SaveChangesAsync();

        var result = await sut.ChangeRoleAsync(tenantId, trainer.Id, "Manager", actorId, "Promoted to floor manager.", null);

        Assert.True(result.Success);
        Assert.Equal("Manager", result.Staff!.Role);
        Assert.Contains("Manager", users.Roles[trainer.Id]);
        Assert.DoesNotContain("Trainer", users.Roles[trainer.Id]);

        // Both the pre-existing tenant-side audit AND the new platform audit fire once.
        Assert.Contains(tenantAudit.Actions, a => a == "staff.role_change");
        var ev = Assert.Single(platformAudit.Events);
        Assert.Equal("platform.tenant.staff_role_change", ev.Action);
        Assert.Equal(actorId, ev.ActorId);
    }

    [Fact]
    public async Task ChangeRole_ToSameRole_IsRejectedAsNoChange()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(tenantId);
        var (sut, users, _, platformAudit) = CreateSut(db);
        var trainer = SeedStaff(db, tenantId, users, "Trainer");
        await db.SaveChangesAsync();

        var result = await sut.ChangeRoleAsync(tenantId, trainer.Id, "Trainer", Guid.NewGuid(), "No actual change.", null);

        Assert.False(result.Success);
        Assert.Equal("NO_CHANGE", result.ErrorCode);
        Assert.Empty(platformAudit.Events);
    }

    // --- Create -----------------------------------------------------------------------------

    [Fact]
    public async Task Create_AddsStaff_AndAuditsOnce()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(tenantId);
        var (sut, users, tenantAudit, platformAudit) = CreateSut(db);
        var actorId = Guid.NewGuid();

        var result = await sut.CreateAsync(tenantId, actorId, new CreateStaffRequest
        {
            FullName = "New Receptionist",
            Email = "new.receptionist@gym.test",
            Password = "Passw0rd1",
            Role = "Receptionist"
        }, "10.0.0.1");

        Assert.True(result.Success);
        Assert.Equal("Receptionist", result.Staff!.Role);
        Assert.Contains(tenantAudit.Actions, a => a == "staff.create");
        var ev = Assert.Single(platformAudit.Events);
        Assert.Equal("platform.tenant.staff_create", ev.Action);
        Assert.Equal(actorId, ev.ActorId);
    }

    [Fact]
    public async Task Create_RejectsOwnerRole_NotAuditedOnPlatformSide()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(tenantId);
        var (sut, _, _, platformAudit) = CreateSut(db);

        var result = await sut.CreateAsync(tenantId, Guid.NewGuid(), new CreateStaffRequest
        {
            FullName = "Second Owner Attempt",
            Email = "second.owner@gym.test",
            Password = "Passw0rd1",
            Role = "Owner"
        }, null);

        Assert.False(result.Success);
        Assert.Empty(platformAudit.Events);
    }
}
