namespace GMS.Tests;

using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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

public class AdminServiceStaffManagementTests
{
    private sealed class NoopPermissionCache : IPermissionCacheService
    {
        public int Invalidations { get; private set; }

        public Task<IReadOnlySet<string>?> GetAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlySet<string>?>(null);

        public Task SetAsync(Guid tenantId, Guid userId, IReadOnlySet<string> permissions, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task InvalidateAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default)
        {
            Invalidations++;
            return Task.CompletedTask;
        }
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

    private sealed class RecordingAudit : IAuditService
    {
        public List<(string Action, string? EntityType, Guid? EntityId, object? Before, object? After)> Events { get; } = new();

        public Task LogAsync(string action, string? entityType = null, Guid? entityId = null, object? before = null, object? after = null, Guid? tenantIdOverride = null)
        {
            Events.Add((action, entityType, entityId, before, after));
            return Task.CompletedTask;
        }

        public Task<Result<PagedResult<AuditEventDto>>> GetAuditEventsAsync(Guid tenantId, AuditEventQueryRequest query) =>
            Task.FromResult(Result<PagedResult<AuditEventDto>>.Failure("n/a"));
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

    internal class FakeUserManager : UserManager<ApplicationUser>
    {
        public Dictionary<Guid, List<string>> Roles { get; } = new();
        public bool ResetSucceeded { get; set; } = true;
        public int CreateCalls { get; private set; }

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
            CreateCalls++;
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

        public override Task<string> GeneratePasswordResetTokenAsync(ApplicationUser user) =>
            Task.FromResult("token");

        public override Task<IdentityResult> ResetPasswordAsync(ApplicationUser user, string token, string newPassword) =>
            Task.FromResult(ResetSucceeded ? IdentityResult.Success : IdentityResult.Failed(new IdentityError { Description = "weak" }));

        public override Task<ApplicationUser?> FindByEmailAsync(string email) =>
            Task.FromResult<ApplicationUser?>(null);

        public override Task<bool> CheckPasswordAsync(ApplicationUser user, string password) =>
            Task.FromResult(true);
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

    private static (AdminService Sut, FakeUserManager Users, RecordingAudit Audit, NoopPermissionCache Cache) CreateSut(
        GymFlowProDbContext db,
        IAuditService? audit = null)
    {
        var users = new FakeUserManager();
        var recording = audit as RecordingAudit ?? new RecordingAudit();
        var cache = new NoopPermissionCache();
        var sut = new AdminService(
            db,
            users,
            NullLogger<AdminService>.Instance,
            cache,
            new AllowAllTiers(),
            recording,
            new NoopFiles());
        return (sut, users, recording, cache);
    }

    private static ApplicationUser SeedStaff(
        GymFlowProDbContext db,
        Guid tenantId,
        FakeUserManager users,
        string role,
        bool active = true,
        DateTime? updatedAt = null)
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = $"{role.ToLowerInvariant()}@gym.test",
            Email = $"{role.ToLowerInvariant()}@gym.test",
            FirstName = role,
            LastName = "Staff",
            TenantId = tenantId,
            IsActive = active,
            UpdatedAtUtc = updatedAt ?? DateTime.UtcNow.AddDays(-1),
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

    [Fact]
    public async Task Owner_CannotBeDemoted()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(tenantId);
        var (sut, users, _, _) = CreateSut(db);
        var owner = SeedStaff(db, tenantId, users, "Owner");
        await db.SaveChangesAsync();

        var result = await sut.UpdateStaffUserAsync(tenantId, owner.Id, new UpdateStaffRequest
        {
            FullName = "Ahmed Owner",
            Role = "Manager",
            IsActive = true
        });

        Assert.False(result.IsSuccess);
        Assert.StartsWith("OWNER_PROTECTED|", result.Error);
        Assert.Contains("Owner", await users.GetRolesAsync(owner));
    }

    [Fact]
    public async Task Owner_CannotBeDeactivated()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(tenantId);
        var (sut, users, _, _) = CreateSut(db);
        var owner = SeedStaff(db, tenantId, users, "Owner");
        await db.SaveChangesAsync();

        var result = await sut.UpdateStaffUserAsync(tenantId, owner.Id, new UpdateStaffRequest
        {
            FullName = "Ahmed Owner",
            Role = "",
            IsActive = false
        });

        Assert.False(result.IsSuccess);
        Assert.StartsWith("OWNER_PROTECTED|", result.Error);
        Assert.True((await db.Users.SingleAsync(u => u.Id == owner.Id)).IsActive);
    }

    [Fact]
    public async Task Owner_CannotBeDeleted()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(tenantId);
        var (sut, users, _, _) = CreateSut(db);
        var owner = SeedStaff(db, tenantId, users, "Owner");
        await db.SaveChangesAsync();

        var result = await sut.DeleteStaffUserAsync(tenantId, owner.Id);

        Assert.False(result.IsSuccess);
        Assert.StartsWith("OWNER_PROTECTED|", result.Error);
        Assert.True((await db.Users.SingleAsync(u => u.Id == owner.Id)).IsActive);
    }

    [Theory]
    [InlineData("Owner")]
    [InlineData("Member")]
    [InlineData("janitor")]
    public async Task Create_RejectsDisallowedRoles(string role)
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(tenantId);
        var (sut, users, _, _) = CreateSut(db);

        var result = await sut.CreateStaffUserAsync(tenantId, new CreateStaffRequest
        {
            FullName = "Bad Role",
            Email = "bad@gym.test",
            Password = "Passw0rd",
            Role = role
        });

        Assert.False(result.IsSuccess);
        Assert.Equal(0, users.CreateCalls);
        Assert.Empty(db.Users);
    }

    [Theory]
    [InlineData("manager", "Manager")]
    [InlineData("Trainer", "Trainer")]
    [InlineData("RECEPTIONIST", "Receptionist")]
    public async Task Create_NormalizesCanonicalRole(string input, string canonical)
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(tenantId);
        var (sut, users, audit, _) = CreateSut(db);

        var result = await sut.CreateStaffUserAsync(tenantId, new CreateStaffRequest
        {
            FullName = "Mona Hassan",
            Email = $"{input}@gym.test",
            Password = "Passw0rd",
            Role = input
        });

        Assert.True(result.IsSuccess);
        Assert.Equal(canonical, result.Data!.Role);
        Assert.Contains(audit.Events, e => e.Action == "staff.create" && e.EntityType == "Staff");
        Assert.Equal(1, users.CreateCalls);
    }

    [Fact]
    public async Task LastLogin_ComesFromAppUser_NotUpdatedAtUtc()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(tenantId);
        var (sut, users, _, _) = CreateSut(db);
        var updatedAt = DateTime.UtcNow.AddHours(-1);
        var lastLogin = DateTime.UtcNow.AddDays(-5);
        var trainer = SeedStaff(db, tenantId, users, "Trainer", updatedAt: updatedAt);
        var appUser = db.AppUsers.Local.Single(a => a.UserId == trainer.Id.ToString());
        appUser.LastLoginAtUtc = lastLogin;
        await db.SaveChangesAsync();

        var list = await sut.GetStaffUsersAsync(tenantId);
        var item = list.Data!.Single(s => s.Id == trainer.Id);
        Assert.Equal(lastLogin, item.LastLoginAt);
        Assert.NotEqual(updatedAt, item.LastLoginAt);

        var detail = await sut.GetStaffUserByIdAsync(tenantId, trainer.Id);
        Assert.Equal(lastLogin, detail.Data!.LastLoginAt);
    }

    [Fact]
    public async Task LastLogin_ListMatchesWhenAppUserUserIdCasingDiffers()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(tenantId);
        var (sut, users, _, _) = CreateSut(db);
        var lastLogin = DateTime.UtcNow.AddDays(-2);
        var trainer = SeedStaff(db, tenantId, users, "Trainer");
        var appUser = db.AppUsers.Local.Single(a => a.UserId == trainer.Id.ToString());
        appUser.UserId = trainer.Id.ToString().ToUpperInvariant();
        appUser.LastLoginAtUtc = lastLogin;
        await db.SaveChangesAsync();

        var list = await sut.GetStaffUsersAsync(tenantId);
        var item = list.Data!.Single(s => s.Id == trainer.Id);
        Assert.Equal(lastLogin, item.LastLoginAt);

        var detail = await sut.GetStaffUserByIdAsync(tenantId, trainer.Id);
        Assert.Equal(lastLogin, detail.Data!.LastLoginAt);
    }

    [Fact]
    public async Task ProfileAndRoleUpdate_DoNotChangeLastLogin()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(tenantId);
        var (sut, users, audit, cache) = CreateSut(db);
        var trainer = SeedStaff(db, tenantId, users, "Trainer");
        var originalLogin = db.AppUsers.Local.Single(a => a.UserId == trainer.Id.ToString()).LastLoginAtUtc;
        await db.SaveChangesAsync();

        var result = await sut.UpdateStaffUserAsync(tenantId, trainer.Id, new UpdateStaffRequest
        {
            FullName = "Karim Trainer",
            Role = "Receptionist",
            IsActive = true
        });

        Assert.True(result.IsSuccess);
        var appUser = await db.AppUsers.SingleAsync(a => a.UserId == trainer.Id.ToString());
        Assert.Equal(originalLogin, appUser.LastLoginAtUtc);
        Assert.Equal("Receptionist", appUser.Role);
        Assert.Equal(1, cache.Invalidations);
        Assert.Contains(audit.Events, e => e.Action == "staff.role_change");
        Assert.Contains(audit.Events, e => e.Action == "staff.update");
    }

    [Fact]
    public async Task Deactivate_SoftDisablesAndRevokesRefresh_PreservesRows()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(tenantId);
        var (sut, users, audit, _) = CreateSut(db);
        var trainer = SeedStaff(db, tenantId, users, "Trainer");
        var originalLogin = db.AppUsers.Local.Single(a => a.UserId == trainer.Id.ToString()).LastLoginAtUtc;
        db.Set<RefreshToken>().Add(new RefreshToken
        {
            UserId = trainer.Id,
            TenantId = tenantId,
            TokenHash = "abc",
            ExpiresAtUtc = DateTime.UtcNow.AddDays(10)
        });
        await db.SaveChangesAsync();

        var result = await sut.UpdateStaffUserAsync(tenantId, trainer.Id, new UpdateStaffRequest
        {
            FullName = "Karim Trainer",
            Role = "Trainer",
            IsActive = false
        });

        Assert.True(result.IsSuccess);
        var identity = await db.Users.SingleAsync(u => u.Id == trainer.Id);
        var appUser = await db.AppUsers.SingleAsync(a => a.UserId == trainer.Id.ToString());
        Assert.False(identity.IsActive);
        Assert.False(appUser.IsActive);
        Assert.False(appUser.IsDeleted);
        Assert.Equal(originalLogin, appUser.LastLoginAtUtc);
        Assert.Contains("Trainer", users.Roles[trainer.Id]);
        Assert.NotNull((await db.Set<RefreshToken>().SingleAsync()).RevokedAtUtc);
        Assert.Contains(audit.Events, e => e.Action == "staff.deactivate");
    }

    [Fact]
    public async Task Reactivate_WritesStaffReactivateAudit()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(tenantId);
        var (sut, users, audit, _) = CreateSut(db);
        var trainer = SeedStaff(db, tenantId, users, "Trainer", active: false);
        await db.SaveChangesAsync();

        var result = await sut.UpdateStaffUserAsync(tenantId, trainer.Id, new UpdateStaffRequest
        {
            FullName = "Karim Trainer",
            Role = "Trainer",
            IsActive = true
        });

        Assert.True(result.IsSuccess);
        Assert.Contains(audit.Events, e => e.Action == "staff.reactivate");
    }

    [Fact]
    public async Task Delete_IsSoftDeactivate_NotHardDelete()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(tenantId);
        var (sut, users, audit, _) = CreateSut(db);
        var trainer = SeedStaff(db, tenantId, users, "Trainer");
        await db.SaveChangesAsync();

        var result = await sut.DeleteStaffUserAsync(tenantId, trainer.Id);

        Assert.True(result.IsSuccess);
        Assert.False((await db.Users.SingleAsync(u => u.Id == trainer.Id)).IsActive);
        Assert.False((await db.AppUsers.SingleAsync(a => a.UserId == trainer.Id.ToString())).IsDeleted);
        Assert.Contains(audit.Events, e => e.Action == "staff.deactivate");
        Assert.DoesNotContain(audit.Events, e => e.Action.Contains("delete", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ResetPassword_RevokesRefreshTokens_AndAuditsWithoutSecret()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(tenantId);
        var (sut, users, audit, _) = CreateSut(db);
        var trainer = SeedStaff(db, tenantId, users, "Trainer");
        db.Set<RefreshToken>().Add(new RefreshToken
        {
            UserId = trainer.Id,
            TenantId = tenantId,
            TokenHash = "live",
            ExpiresAtUtc = DateTime.UtcNow.AddDays(20)
        });
        await db.SaveChangesAsync();

        var result = await sut.ResetStaffPasswordAsync(tenantId, trainer.Id, "NewPass1");

        Assert.True(result.IsSuccess);
        Assert.NotNull((await db.Set<RefreshToken>().SingleAsync()).RevokedAtUtc);
        var ev = audit.Events.Single(e => e.Action == "staff.password_reset");
        Assert.Equal("Staff", ev.EntityType);
        Assert.Equal(trainer.Id, ev.EntityId);
        Assert.DoesNotContain("NewPass1", ev.After?.ToString() ?? "");
    }

    [Fact]
    public async Task AuditActor_IsAppUserId_NotIdentityId()
    {
        var tenantId = Guid.NewGuid();
        var identityId = Guid.NewGuid();
        await using var db = CreateDb(tenantId);

        var actor = new AppUser
        {
            TenantId = tenantId,
            UserId = identityId.ToString(),
            FirstName = "Acting",
            LastName = "Owner",
            Email = "owner@gym.test",
            Role = "Owner"
        };
        db.AppUsers.Add(actor);
        await db.SaveChangesAsync();

        var http = new DefaultHttpContext();
        var claims = new ClaimsIdentity("Test");
        claims.AddClaim(new Claim(ClaimTypes.NameIdentifier, identityId.ToString()));
        http.User = new ClaimsPrincipal(claims);

        var tenantContext = new TenantContext();
        tenantContext.SetTenant(tenantId, "Gym", "Africa/Cairo");
        var realAudit = new AuditService(
            db,
            new HttpContextAccessor { HttpContext = http },
            tenantContext,
            NullLogger<AuditService>.Instance);

        var users = new FakeUserManager();
        var trainer = SeedStaff(db, tenantId, users, "Trainer");
        await db.SaveChangesAsync();

        var sut = new AdminService(
            db,
            users,
            NullLogger<AdminService>.Instance,
            new NoopPermissionCache(),
            new AllowAllTiers(),
            realAudit,
            new NoopFiles());

        await sut.UpdateStaffUserAsync(tenantId, trainer.Id, new UpdateStaffRequest
        {
            FullName = "Karim Trainer",
            Role = "Trainer",
            IsActive = true
        });

        var stored = await db.AuditEvents.OrderByDescending(a => a.CreatedAtUtc).FirstAsync();
        Assert.Equal(actor.Id, stored.ActorUserId);
        Assert.NotEqual(identityId, stored.ActorUserId);
        Assert.Equal("staff.update", stored.Action);
        Assert.Equal("Staff", stored.EntityType);
        Assert.Equal(trainer.Id, stored.EntityId);
    }

    [Fact]
    public async Task Create_AssignsStableTenantStaffNumberAndProfile()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(tenantId);
        var (sut, _, _, _) = CreateSut(db);

        var first = await sut.CreateStaffUserAsync(tenantId, new CreateStaffRequest
        {
            FullName = "Sara Desk",
            Email = "sara@gym.test",
            Password = "Passw0rd",
            Role = "Receptionist",
            PhoneNumber = "01000000001",
            JobTitle = "Front Desk Supervisor",
            Department = "front desk",
            HireDate = new DateOnly(2024, 3, 1),
            Notes = "Has gym keys"
        });
        var second = await sut.CreateStaffUserAsync(tenantId, new CreateStaffRequest
        {
            FullName = "Omar Train",
            Email = "omar@gym.test",
            Password = "Passw0rd",
            Role = "Trainer",
            Department = "Training"
        });

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal("ST-0001", first.Data!.StaffNumber);
        Assert.Equal("ST-0002", second.Data!.StaffNumber);
        Assert.Equal("01000000001", first.Data.PhoneNumber);
        Assert.Equal("Front Desk Supervisor", first.Data.JobTitle);
        Assert.Equal("Front Desk", first.Data.Department);
        Assert.Equal(new DateOnly(2024, 3, 1), first.Data.HireDate);
        Assert.Equal("Has gym keys", first.Data.Notes);

        var stored = await db.AppUsers.SingleAsync(a => a.Email == "sara@gym.test");
        Assert.Equal("ST-0001", stored.StaffNumber);
        Assert.Equal(stored.StaffNumber, first.Data.StaffNumber);
    }

    [Fact]
    public async Task Create_DoesNotConsumeMemberStaffNumbers()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(tenantId);
        var (sut, _, _, _) = CreateSut(db);
        db.AppUsers.Add(new AppUser
        {
            TenantId = tenantId,
            UserId = Guid.NewGuid().ToString(),
            FirstName = "Gym",
            LastName = "Member",
            Email = "member@gym.test",
            Role = "Member",
            IsActive = true,
            StaffNumber = "ST-0099",
            CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var created = await sut.CreateStaffUserAsync(tenantId, new CreateStaffRequest
        {
            FullName = "New Trainer",
            Email = "new.trainer@gym.test",
            Password = "Passw0rd",
            Role = "Trainer"
        });

        Assert.True(created.IsSuccess);
        Assert.Equal("ST-0001", created.Data!.StaffNumber);
    }

    [Fact]
    public async Task Create_RejectsUnknownDepartment()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(tenantId);
        var (sut, _, _, _) = CreateSut(db);
        var result = await sut.CreateStaffUserAsync(tenantId, new CreateStaffRequest
        {
            FullName = "Bad Dept",
            Email = "dept@gym.test",
            Password = "Passw0rd",
            Role = "Trainer",
            Department = "Payroll"
        });
        Assert.False(result.IsSuccess);
        Assert.Contains("Department", result.Error);
    }

    [Fact]
    public async Task Update_WritesPhoneTitleDepartmentWithoutChangingStaffNumber()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(tenantId);
        var (sut, users, _, _) = CreateSut(db);
        var trainer = SeedStaff(db, tenantId, users, "Trainer");
        await db.SaveChangesAsync();

        var updated = await sut.UpdateStaffUserAsync(tenantId, trainer.Id, new UpdateStaffRequest
        {
            FullName = "Numbered Staff",
            Role = "Trainer",
            IsActive = true,
            PhoneNumber = "01111111111",
            JobTitle = "Personal Trainer",
            Department = "Training",
            HireDate = new DateOnly(2025, 1, 10),
            Notes = "PT floor"
        });

        Assert.True(updated.IsSuccess);
        Assert.Equal("ST-0001", updated.Data!.StaffNumber);
        Assert.Equal("Personal Trainer", updated.Data.JobTitle);
        Assert.Equal("Training", updated.Data.Department);
        Assert.Equal("01111111111", updated.Data.PhoneNumber);
        Assert.Equal("PT floor", updated.Data.Notes);

        var again = await sut.UpdateStaffUserAsync(tenantId, trainer.Id, new UpdateStaffRequest
        {
            FullName = "Numbered Staff",
            Role = "Trainer",
            IsActive = true,
            JobTitle = "Senior Personal Trainer"
        });
        Assert.True(again.IsSuccess);
        Assert.Equal("ST-0001", again.Data!.StaffNumber);
        Assert.Equal("Senior Personal Trainer", again.Data.JobTitle);
        Assert.Equal("Training", again.Data.Department);
    }

    [Fact]
    public async Task Activity_IncludesActorEventsAndStaffLifecycle()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(tenantId);
        var (sut, users, _, _) = CreateSut(db);
        var trainer = SeedStaff(db, tenantId, users, "Trainer");
        await db.SaveChangesAsync();
        var app = await db.AppUsers.SingleAsync(a => a.UserId == trainer.Id.ToString());

        db.AuditEvents.Add(new AuditEvent
        {
            TenantId = tenantId,
            ActorUserId = app.Id,
            Action = "checkin.manual",
            EntityType = "GymAttendance",
            EntityId = Guid.NewGuid(),
            CreatedAtUtc = DateTime.UtcNow.AddMinutes(-5)
        });
        db.AuditEvents.Add(new AuditEvent
        {
            TenantId = tenantId,
            ActorUserId = Guid.NewGuid(),
            Action = "staff.password_reset",
            EntityType = "Staff",
            EntityId = trainer.Id,
            CreatedAtUtc = DateTime.UtcNow.AddMinutes(-2)
        });
        db.AuditEvents.Add(new AuditEvent
        {
            TenantId = tenantId,
            ActorUserId = app.Id,
            Action = "checkin.manual",
            EntityType = "GymAttendance",
            EntityId = Guid.NewGuid(),
            CreatedAtUtc = DateTime.UtcNow.AddMinutes(-1)
        });
        await db.SaveChangesAsync();

        var result = await sut.GetStaffActivityAsync(tenantId, trainer.Id);
        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Data!.Count);
        Assert.Contains(result.Data, e => e.Action == "checkin.manual" && e.Label == "Checked in a member");
        Assert.Contains(result.Data, e => e.Action == "staff.password_reset" && e.AboutThisStaff);
    }

    [Fact]
    public async Task Photo_WritesBothProfileUrls()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(tenantId);
        var (sut, users, _, _) = CreateSut(db);
        var trainer = SeedStaff(db, tenantId, users, "Trainer");
        await db.SaveChangesAsync();

        await using var stream = new MemoryStream(new byte[] { 1, 2, 3, 4 });
        var result = await sut.SetStaffPhotoAsync(tenantId, trainer.Id, stream, "a.jpg", "image/jpeg");
        Assert.True(result.IsSuccess);
        Assert.Contains("/uploads/staff-photos-", result.Data!.ProfilePhotoUrl);
        var app = await db.AppUsers.SingleAsync(a => a.UserId == trainer.Id.ToString());
        Assert.Equal(result.Data.ProfilePhotoUrl, app.ProfilePhotoUrl);
        var identity = await db.Users.SingleAsync(u => u.Id == trainer.Id);
        Assert.Equal(result.Data.ProfilePhotoUrl, identity.ProfilePhotoUrl);
    }
}
