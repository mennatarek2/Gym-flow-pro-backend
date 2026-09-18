namespace GMS.Tests;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using GMS.Application.Common;
using GMS.Application.DTOs.Backup;
using GMS.Application.DTOs.LocalSetup;
using GMS.Application.DTOs.Provisioning;
using GMS.Application.Interfaces;
using GMS.Application.Services;
using GMS.Core.Entities;
using GMS.Core.Entities.Identity;
using GMS.Core.Interfaces;
using GMS.Infrastructure.Persistence;

/// <summary>
/// Local Edition first-run / recovery setup. Fakes ITenantProvisioningService,
/// ILocalLicenseClientService, and IBackupHealthService so these tests stay on the setup
/// state machine without the platform license server or SQL backup scripts.
/// </summary>
public class LocalFirstRunServiceTests
{
    private sealed class FakeTenantProvisioningService : ITenantProvisioningService
    {
        private readonly GymFlowProDbContext _db;

        public FakeTenantProvisioningService(GymFlowProDbContext db)
        {
            _db = db;
        }

        public FakeUserManager? Users { get; set; }
        public int CallCount { get; private set; }
        public ProvisionTenantRequest? LastRequest { get; private set; }
        public Result<ProvisionTenantResponse>? NextResult { get; set; }
        public bool LeaveUnsavedTenantOnFailure { get; set; }

        public async Task<Result<ProvisionTenantResponse>> ProvisionAsync(
            ProvisionTenantRequest request, Guid actorPlatformUserId, string? ipAddress = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastRequest = request;
            var result = NextResult ?? Result<ProvisionTenantResponse>.Failure("NOT_CONFIGURED|no canned result");
            if (result.IsSuccess)
            {
                _db.Tenants.Add(new Tenant
                {
                    Id = result.Data!.TenantId,
                    Name = request.Name,
                    GymCode = result.Data.GymCode,
                    Email = request.Email,
                    IsDeleted = false,
                });
                _db.AppUsers.Add(new AppUser
                {
                    TenantId = result.Data.TenantId,
                    UserId = result.Data.OwnerUserId.ToString(),
                    Email = request.OwnerEmail,
                    Role = "Owner",
                    IsActive = true,
                    FirstName = "Owner",
                    LastName = "Name",
                });
                Users?.SeedOwner(result.Data.OwnerUserId, result.Data.TenantId, request.OwnerEmail);
                await _db.SaveChangesAsync(cancellationToken);
            }
            else if (LeaveUnsavedTenantOnFailure)
            {
                _db.Tenants.Add(new Tenant
                {
                    Id = Guid.NewGuid(),
                    Name = request.Name,
                    GymCode = "GYM-GHOST-0001",
                    Email = request.Email,
                    IsDeleted = false,
                });
            }
            return result;
        }
    }

    private sealed class FakeLocalLicenseClientService : ILocalLicenseClientService
    {
        public int ActivateCallCount { get; private set; }
        public int CheckCallCount { get; private set; }
        public int ReleaseCallCount { get; private set; }
        public string? LastLicenseKeyActivated { get; private set; }
        public LocalLicenseActivationOutcome NextResult { get; set; } = new() { Success = true };

        public string GetOrCreateInstallationId() => "INS-TEST0001";
        public string? TryGetInstallationId() => "INS-TEST0001";
        public GMS.Core.Licensing.LocalLicensePayload? GetStoredLicense() => null;

        public Task<LocalLicenseActivationOutcome> ActivateAsync(string licenseKey, CancellationToken cancellationToken = default)
        {
            ActivateCallCount++;
            LastLicenseKeyActivated = licenseKey;
            return Task.FromResult(NextResult);
        }

        public Task<LocalLicenseActivationOutcome> CheckAsync(string licenseKey, CancellationToken cancellationToken = default)
        {
            CheckCallCount++;
            return Task.FromResult(NextResult);
        }

        public Task<LocalLicenseActivationOutcome> ReleaseAsync(string licenseKey, CancellationToken cancellationToken = default)
        {
            ReleaseCallCount++;
            return Task.FromResult(new LocalLicenseActivationOutcome { Success = true });
        }

        public GMS.Core.Licensing.LocalLicenseStatus Status { get; set; } = new() { HasLicense = false };

        public GMS.Core.Licensing.LocalLicenseStatus GetCurrentStatus() => Status;

        public Task<bool> TryRevalidateAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task ReportLifecycleEventAsync(
            string licenseKey,
            Guid operationId,
            string eventType,
            string? gymCode = null,
            string? gymName = null,
            string? message = null,
            CancellationToken cancellationToken = default)
        {
            Reported.Add(eventType);
            return Task.CompletedTask;
        }

        public Task<LocalDeskFeedbackReportOutcome> ReportDeskFeedbackAsync(
            LocalDeskFeedbackReportRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new LocalDeskFeedbackReportOutcome { Success = true, Id = Guid.NewGuid() });

        public List<string> Reported { get; } = new();
    }

    private sealed class FakeBackupHealthService : IBackupHealthService
    {
        public bool FailTrigger { get; set; }
        public bool FailVerify { get; set; }
        public bool NeverCompletes { get; set; }
        public int TriggerCount { get; private set; }
        public string Location { get; set; } = @"C:\ProgramData\HyMotion\Backups";
        public List<BackupHistoryItemDto> History { get; } = new();

        public Task<BackupHealthDto> GetHealthAsync() =>
            Task.FromResult(new BackupHealthDto { BackupLocation = Location, OverallStatus = "Healthy" });

        public Task<List<BackupHistoryItemDto>> GetHistoryAsync(int take = 30) =>
            Task.FromResult(History.OrderByDescending(h => h.CreatedAtUtc).Take(take).ToList());

        public Task<TriggerBackupResponse> TriggerManualBackupAsync()
        {
            TriggerCount++;
            if (FailTrigger)
            {
                return Task.FromResult(new TriggerBackupResponse
                {
                    Started = false,
                    Message = "Backup script not found.",
                });
            }

            if (!NeverCompletes)
            {
                History.Add(new BackupHistoryItemDto
                {
                    BackupId = "bak-" + TriggerCount,
                    CreatedAtUtc = DateTime.UtcNow,
                    BackupType = "Manual",
                    Status = FailVerify ? "Failed" : "Healthy",
                    DatabaseVerified = !FailVerify,
                });
            }

            return Task.FromResult(new TriggerBackupResponse { Started = true, Message = "Backup started." });
        }
    }

    private sealed class FakeUserManager : UserManager<ApplicationUser>
    {
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

        public List<ApplicationUser> Users { get; } = new();
        public HashSet<Guid> OwnerIds { get; } = new();
        public IdentityResult CreateResult { get; set; } = IdentityResult.Success;

        public void SeedOwner(Guid id, Guid tenantId, string email)
        {
            Users.Add(new ApplicationUser
            {
                Id = id,
                TenantId = tenantId,
                Email = email,
                UserName = email,
                IsActive = true,
            });
            OwnerIds.Add(id);
        }

        public override Task<ApplicationUser?> FindByEmailAsync(string email) =>
            Task.FromResult(Users.FirstOrDefault(u =>
                string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase)));

        public override Task<IdentityResult> CreateAsync(ApplicationUser user, string password)
        {
            if (!CreateResult.Succeeded) return Task.FromResult(CreateResult);
            if (user.Id == Guid.Empty) user.Id = Guid.NewGuid();
            Users.Add(user);
            return Task.FromResult(IdentityResult.Success);
        }

        public override Task<IdentityResult> UpdateAsync(ApplicationUser user) =>
            Task.FromResult(IdentityResult.Success);

        public override Task<string> GeneratePasswordResetTokenAsync(ApplicationUser user) =>
            Task.FromResult("token");

        public override Task<IdentityResult> ResetPasswordAsync(ApplicationUser user, string token, string newPassword) =>
            Task.FromResult(IdentityResult.Success);

        public override Task<bool> IsInRoleAsync(ApplicationUser user, string role) =>
            Task.FromResult(role == "Owner" && OwnerIds.Contains(user.Id));

        public override Task<IdentityResult> AddToRoleAsync(ApplicationUser user, string role)
        {
            if (role == "Owner") OwnerIds.Add(user.Id);
            return Task.FromResult(IdentityResult.Success);
        }

        public override Task<IList<ApplicationUser>> GetUsersInRoleAsync(string roleName)
        {
            IList<ApplicationUser> list = roleName == "Owner"
                ? Users.Where(u => OwnerIds.Contains(u.Id)).ToList()
                : new List<ApplicationUser>();
            return Task.FromResult(list);
        }

        private sealed class UnusedStore : IUserStore<ApplicationUser>
        {
            public void Dispose() { }
            public Task<IdentityResult> CreateAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(IdentityResult.Success);
            public Task<IdentityResult> DeleteAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(IdentityResult.Success);
            public Task<ApplicationUser?> FindByIdAsync(string userId, CancellationToken cancellationToken) => Task.FromResult<ApplicationUser?>(null);
            public Task<ApplicationUser?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken) => Task.FromResult<ApplicationUser?>(null);
            public Task<string?> GetNormalizedUserNameAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(user.UserName);
            public Task<string> GetUserIdAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(user.Id.ToString());
            public Task<string?> GetUserNameAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(user.UserName);
            public Task SetNormalizedUserNameAsync(ApplicationUser user, string? normalizedName, CancellationToken cancellationToken) => Task.CompletedTask;
            public Task SetUserNameAsync(ApplicationUser user, string? userName, CancellationToken cancellationToken) => Task.CompletedTask;
            public Task<IdentityResult> UpdateAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(IdentityResult.Success);
        }
    }

    private static LocalFirstRunService NewSvc(
        GymFlowProDbContext db,
        FakeTenantProvisioningService? provisioning = null,
        FakeLocalLicenseClientService? license = null,
        FakeUserManager? users = null,
        FakeBackupHealthService? backup = null)
    {
        users ??= new FakeUserManager();
        provisioning ??= new FakeTenantProvisioningService(db);
        provisioning.Users = users;
        return new LocalFirstRunService(
            db,
            provisioning,
            license ?? new FakeLocalLicenseClientService(),
            backup ?? new FakeBackupHealthService(),
            users,
            NullLogger<LocalFirstRunService>.Instance)
        {
            BackupPollDelay = TimeSpan.Zero,
            BackupWaitTimeout = TimeSpan.FromMilliseconds(50),
        };
    }

    private static GymFlowProDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<GymFlowProDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new GymFlowProDbContext(options, tenantContext: null);
    }

    private static LocalFirstRunRequest ValidRequest(string? mode = null) => new()
    {
        Mode = mode,
        GymName = "Test Gym",
        OwnerFullName = "Owner Name",
        OwnerEmail = "owner@example.com",
        OwnerPassword = "P@ssword123",
        LicenseKey = "HY-LCL-TEST1-TEST2",
    };

    private static async Task<(Guid TenantId, Guid MemberId)> SeedExistingGymAsync(
        GymFlowProDbContext db, string name = "H3O", string code = "GYM-GYM-7050", string email = "owner@example.com")
    {
        var tenantId = Guid.NewGuid();
        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = name,
            GymCode = code,
            Email = email,
            IsDeleted = false,
            IsActive = true,
        });
        var memberId = Guid.NewGuid();
        db.GymMembers.Add(new GymMember
        {
            Id = memberId,
            TenantId = tenantId,
            FullName = "Existing Member",
            PhoneNumber = "01000000000",
            IsDeleted = false,
        });
        await db.SaveChangesAsync();
        return (tenantId, memberId);
    }

    private static async Task<(Guid TenantId, Guid MemberId, LocalSetupActor Actor)> SeedCompletedGymAsync(
        GymFlowProDbContext db, FakeUserManager users, string name = "H3O", string code = "GYM-GYM-7050", string email = "owner@example.com")
    {
        var (tenantId, memberId) = await SeedExistingGymAsync(db, name, code, email);
        var ownerId = Guid.NewGuid();
        users.SeedOwner(ownerId, tenantId, email);
        db.AppUsers.Add(new AppUser
        {
            TenantId = tenantId,
            UserId = ownerId.ToString(),
            Email = email,
            Role = "Owner",
            IsActive = true,
            FirstName = "Owner",
            LastName = "Name",
        });
        await db.SaveChangesAsync();
        return (tenantId, memberId, new LocalSetupActor(true, tenantId));
    }

    [Fact]
    public async Task GetStatusAsync_NoTenant_ReportsSetupRequired()
    {
        await using var db = NewContext();
        var svc = NewSvc(db);

        var status = await svc.GetStatusAsync();

        Assert.False(status.IsCompleted);
        Assert.True(status.SetupRequired);
        Assert.Null(status.TenantId);
        Assert.False(status.CanRestoreExistingGym);
        Assert.True(status.CanStartNewGym);
        Assert.False(status.RequiresOwnerForNewGym);
    }

    [Fact]
    public async Task CompleteFirstRunAsync_NewGym_AnonymousOnOwnerMissing_IsRejected()
    {
        await using var db = NewContext();
        await SeedExistingGymAsync(db);
        var fake = new FakeTenantProvisioningService(db);
        var svc = NewSvc(db, fake);
        var request = ValidRequest(LocalSetupModes.NewGym);
        request.ConfirmPhrase = LocalSetupModes.NewGymConfirmPhrase;

        var result = await svc.CompleteFirstRunAsync(request);

        Assert.False(result.IsSuccess);
        Assert.Equal(LocalSetupErrorCodes.UseExistingGym, result.Message);
        Assert.Equal(0, fake.CallCount);
    }

    [Fact]
    public async Task CompleteFirstRunAsync_NewGym_AnonymousOnCompletedGym_IsRejected()
    {
        await using var db = NewContext();
        var users = new FakeUserManager();
        await SeedCompletedGymAsync(db, users);
        var fake = new FakeTenantProvisioningService(db);
        var svc = NewSvc(db, fake, users: users);
        var request = ValidRequest(LocalSetupModes.NewGym);
        request.ConfirmPhrase = LocalSetupModes.NewGymConfirmPhrase;

        var result = await svc.CompleteFirstRunAsync(request);

        Assert.False(result.IsSuccess);
        Assert.Equal(LocalSetupErrorCodes.OwnerRequired, result.Message);
        Assert.Equal(0, fake.CallCount);
    }

    [Fact]
    public async Task CompleteFirstRunAsync_NewGym_OwnerForOtherTenant_IsRejected()
    {
        await using var db = NewContext();
        var users = new FakeUserManager();
        await SeedCompletedGymAsync(db, users);
        var fake = new FakeTenantProvisioningService(db);
        var svc = NewSvc(db, fake, users: users);
        var request = ValidRequest(LocalSetupModes.NewGym);
        request.ConfirmPhrase = LocalSetupModes.NewGymConfirmPhrase;
        var otherOwner = new LocalSetupActor(true, Guid.NewGuid());

        var result = await svc.CompleteFirstRunAsync(request, otherOwner);

        Assert.False(result.IsSuccess);
        Assert.Equal(LocalSetupErrorCodes.OwnerRequired, result.Message);
        Assert.Equal(0, fake.CallCount);
    }

    [Fact]
    public async Task GetStatusAsync_CompletedGym_RequiresOwnerForNewGym()
    {
        await using var db = NewContext();
        var users = new FakeUserManager();
        await SeedCompletedGymAsync(db, users);
        var svc = NewSvc(db, users: users);

        var status = await svc.GetStatusAsync();

        Assert.True(status.IsCompleted);
        Assert.True(status.CanStartNewGym);
        Assert.True(status.RequiresOwnerForNewGym);
        Assert.False(status.OwnerMissing);
        Assert.False(status.CanRestoreExistingGym);
    }

    [Fact]
    public async Task GetStatusAsync_TenantWithoutOwner_ReportsExistingGymAndOwnerMissing()
    {
        await using var db = NewContext();
        await SeedExistingGymAsync(db);
        var svc = NewSvc(db);

        var status = await svc.GetStatusAsync();

        Assert.False(status.IsCompleted);
        Assert.Equal("GYM-GYM-7050", status.GymCode);
        Assert.Equal("H3O", status.GymName);
        Assert.True(status.OwnerMissing);
        Assert.True(status.CanRestoreExistingGym);
        Assert.False(status.CanStartNewGym);
        Assert.False(status.RequiresOwnerForNewGym);
    }

    [Fact]
    public async Task GetStatusAsync_AppUserOwnerWithoutIdentity_ReportsSetupRequired()
    {
        await using var db = NewContext();
        var (tenantId, _) = await SeedExistingGymAsync(db);
        db.AppUsers.Add(new AppUser
        {
            TenantId = tenantId,
            UserId = Guid.NewGuid().ToString(),
            Email = "abdu@hymo.com",
            Role = "Owner",
            IsActive = true,
            FirstName = "Abdu",
            LastName = "Owner",
        });
        await db.SaveChangesAsync();
        var svc = NewSvc(db);

        var status = await svc.GetStatusAsync();

        Assert.False(status.IsCompleted);
        Assert.Equal("GYM-GYM-7050", status.GymCode);
    }

    [Fact]
    public async Task CompleteFirstRunAsync_NoTenant_ProvisionsAndMarksComplete()
    {
        await using var db = NewContext();
        var tenantId = Guid.NewGuid();
        var fake = new FakeTenantProvisioningService(db)
        {
            NextResult = Result<ProvisionTenantResponse>.Success(new ProvisionTenantResponse
            {
                TenantId = tenantId,
                GymCode = "GYM-LOCAL-01",
                OwnerUserId = Guid.NewGuid(),
                OwnerEmail = "owner@example.com",
            }),
        };
        var svc = NewSvc(db, fake);

        var result = await svc.CompleteFirstRunAsync(ValidRequest(LocalSetupModes.NewGym));

        Assert.True(result.IsSuccess);
        Assert.True(result.Data!.IsCompleted);
        Assert.Equal(tenantId, result.Data.TenantId);
        Assert.Equal("GYM-LOCAL-01", result.Data.GymCode);
        Assert.Equal(LocalSetupLicenseStates.Active, result.Data.LicenseState);
        Assert.Equal(1, fake.CallCount);
        Assert.False(fake.LastRequest!.StartTrial);
    }

    [Fact]
    public async Task CompleteFirstRunAsync_CalledTwice_DoesNotProvisionTwice()
    {
        await using var db = NewContext();
        var tenantId = Guid.NewGuid();
        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Test Gym",
            GymCode = "GYM-LOCAL-01",
            IsDeleted = false,
        });
        db.AppUsers.Add(new AppUser
        {
            TenantId = tenantId,
            UserId = Guid.NewGuid().ToString(),
            Email = "owner@example.com",
            Role = "Owner",
            IsActive = true,
            FirstName = "Owner",
            LastName = "Name",
        });
        await db.SaveChangesAsync();

        var users = new FakeUserManager();
        users.SeedOwner(Guid.NewGuid(), tenantId, "owner@example.com");
        var fake = new FakeTenantProvisioningService(db);
        var svc = NewSvc(db, fake, users: users);

        var result = await svc.CompleteFirstRunAsync(ValidRequest());

        Assert.True(result.IsSuccess);
        Assert.True(result.Data!.IsCompleted);
        Assert.Equal("GYM-LOCAL-01", result.Data.GymCode);
        Assert.Equal(0, fake.CallCount);
    }

    [Fact]
    public async Task CompleteFirstRunAsync_ExistingGym_RecreatesOwnerWithoutProvisioning()
    {
        await using var db = NewContext();
        var (tenantId, memberId) = await SeedExistingGymAsync(db);
        var fake = new FakeTenantProvisioningService(db);
        var svc = NewSvc(db, fake);

        var result = await svc.CompleteFirstRunAsync(ValidRequest(LocalSetupModes.ExistingGym));

        Assert.True(result.IsSuccess);
        Assert.True(result.Data!.IsCompleted);
        Assert.Equal(tenantId, result.Data.TenantId);
        Assert.Equal("GYM-GYM-7050", result.Data.GymCode);
        Assert.Equal(0, fake.CallCount);
        Assert.True(await db.AppUsers.AnyAsync(u =>
            u.TenantId == tenantId && u.Role == "Owner" && u.Email == "owner@example.com"));
        Assert.True(await db.GymMembers.IgnoreQueryFilters().AnyAsync(m => m.Id == memberId && !m.IsDeleted));
    }

    [Fact]
    public async Task CompleteFirstRunAsync_ExistingGym_OfflineValidLicenseWhenServerUnavailable()
    {
        await using var db = NewContext();
        await SeedExistingGymAsync(db);
        var fake = new FakeTenantProvisioningService(db);
        var license = new FakeLocalLicenseClientService
        {
            Status = new()
            {
                HasLicense = true,
                SignatureValid = true,
                WithinGracePeriod = true,
                LicenseKey = "HY-LCL-TEST1-TEST2",
            },
            NextResult = new LocalLicenseActivationOutcome
            {
                Success = false,
                ErrorCode = "not_configured",
                ErrorMessage = "This install does not have a license server configured.",
            },
        };
        var svc = NewSvc(db, fake, license);

        var result = await svc.CompleteFirstRunAsync(ValidRequest(LocalSetupModes.ExistingGym));

        Assert.True(result.IsSuccess);
        Assert.Equal(LocalSetupLicenseStates.OfflineValid, result.Data!.LicenseState);
        Assert.Equal("GYM-GYM-7050", result.Data.GymCode);
        Assert.Equal(1, license.ActivateCallCount);
        Assert.Equal(0, fake.CallCount);
    }

    [Fact]
    public async Task CompleteFirstRunAsync_ExistingGym_EmptyKeyFails()
    {
        await using var db = NewContext();
        await SeedExistingGymAsync(db);
        var fake = new FakeTenantProvisioningService(db);
        var license = new FakeLocalLicenseClientService
        {
            Status = new() { HasLicense = true, SignatureValid = true, WithinGracePeriod = true, LicenseKey = "HY-LCL-TEST1-TEST2" },
        };
        var svc = NewSvc(db, fake, license);
        var request = ValidRequest(LocalSetupModes.ExistingGym);
        request.LicenseKey = "";

        var result = await svc.CompleteFirstRunAsync(request);

        Assert.False(result.IsSuccess);
        Assert.Equal(LocalSetupLicenseStates.NotActivated, result.Message);
        Assert.Equal(0, license.ActivateCallCount);
        Assert.Equal(0, fake.CallCount);
    }

    [Fact]
    public async Task CompleteFirstRunAsync_ExistingGym_InvalidLicenseDoesNotTouchData()
    {
        await using var db = NewContext();
        var (tenantId, memberId) = await SeedExistingGymAsync(db);
        var fake = new FakeTenantProvisioningService(db);
        var license = new FakeLocalLicenseClientService
        {
            NextResult = new LocalLicenseActivationOutcome
            {
                Success = false,
                ErrorCode = "activation_failed",
                ErrorMessage = "The license key is invalid or cannot be activated for this installation.",
            },
        };
        var svc = NewSvc(db, fake, license);

        var result = await svc.CompleteFirstRunAsync(ValidRequest(LocalSetupModes.ExistingGym));

        Assert.False(result.IsSuccess);
        Assert.Equal(LocalSetupLicenseStates.Invalid, result.Message);
        Assert.Equal(0, fake.CallCount);
        Assert.False(await db.AppUsers.AnyAsync(u => u.TenantId == tenantId && u.Role == "Owner"));
        Assert.True(await db.GymMembers.IgnoreQueryFilters().AnyAsync(m => m.Id == memberId));
    }

    [Fact]
    public async Task CompleteFirstRunAsync_ExistingGym_MismatchedKeyWhenServerDownIsInvalid()
    {
        await using var db = NewContext();
        await SeedExistingGymAsync(db);
        var license = new FakeLocalLicenseClientService
        {
            Status = new()
            {
                HasLicense = true,
                SignatureValid = true,
                WithinGracePeriod = true,
                LicenseKey = "HY-LCL-OTHER-KEY01",
            },
            NextResult = new LocalLicenseActivationOutcome { Success = false, ErrorCode = "network_error" },
        };
        var svc = NewSvc(db, license: license);

        var result = await svc.CompleteFirstRunAsync(ValidRequest(LocalSetupModes.ExistingGym));

        Assert.False(result.IsSuccess);
        Assert.Equal(LocalSetupLicenseStates.Invalid, result.Message);
    }

    [Fact]
    public async Task CompleteFirstRunAsync_ExistingGym_ExpiredOfflineLicenseFails()
    {
        await using var db = NewContext();
        await SeedExistingGymAsync(db);
        var license = new FakeLocalLicenseClientService
        {
            Status = new()
            {
                HasLicense = true,
                SignatureValid = true,
                GracePeriodExceeded = true,
                LicenseKey = "HY-LCL-TEST1-TEST2",
            },
            NextResult = new LocalLicenseActivationOutcome { Success = false, ErrorCode = "network_error" },
        };
        var svc = NewSvc(db, license: license);

        var result = await svc.CompleteFirstRunAsync(ValidRequest(LocalSetupModes.ExistingGym));

        Assert.False(result.IsSuccess);
        Assert.Equal(LocalSetupLicenseStates.Expired, result.Message);
    }

    [Fact]
    public async Task ValidateLicenseAsync_ServerUnavailableWithoutStoredLicense_IsNotActivated()
    {
        await using var db = NewContext();
        await SeedExistingGymAsync(db);
        var license = new FakeLocalLicenseClientService
        {
            Status = new() { HasLicense = false },
            NextResult = new LocalLicenseActivationOutcome { Success = false, ErrorCode = "network_error" },
        };
        var svc = NewSvc(db, license: license);

        var result = await svc.ValidateLicenseAsync(new LocalLicenseValidationRequest
        {
            Mode = LocalSetupModes.ExistingGym,
            LicenseKey = "HY-LCL-TEST1-TEST2",
        });

        Assert.False(result.Valid);
        Assert.Equal(LocalSetupLicenseStates.NotActivated, result.State);
    }

    [Fact]
    public async Task CompleteFirstRunAsync_ProvisioningFails_ReturnsFailureAndDoesNotMarkComplete()
    {
        await using var db = NewContext();
        var fake = new FakeTenantProvisioningService(db)
        {
            NextResult = Result<ProvisionTenantResponse>.Failure("VALIDATION|Owner email already in use."),
        };
        var license = new FakeLocalLicenseClientService();
        var svc = NewSvc(db, fake, license);

        var result = await svc.CompleteFirstRunAsync(ValidRequest(LocalSetupModes.NewGym));

        Assert.False(result.IsSuccess);
        Assert.Equal(1, fake.CallCount);
        Assert.Equal(1, license.ActivateCallCount);
        Assert.Equal(1, license.ReleaseCallCount);

        var status = await svc.GetStatusAsync();
        Assert.False(status.IsCompleted);
    }

    [Fact]
    public async Task CompleteFirstRunAsync_ProvisioningFails_DoesNotReleaseAlreadyHeldKey()
    {
        await using var db = NewContext();
        var fake = new FakeTenantProvisioningService(db)
        {
            NextResult = Result<ProvisionTenantResponse>.Failure("VALIDATION|Owner email already in use."),
        };
        var license = new FakeLocalLicenseClientService
        {
            Status = new()
            {
                HasLicense = true,
                SignatureValid = true,
                LicenseKey = "HY-LCL-TEST1-TEST2",
            },
        };
        var svc = NewSvc(db, fake, license);

        var result = await svc.CompleteFirstRunAsync(ValidRequest(LocalSetupModes.NewGym));

        Assert.False(result.IsSuccess);
        Assert.Equal(1, license.ActivateCallCount);
        Assert.Equal(0, license.ReleaseCallCount);
    }

    [Fact]
    public async Task ValidateLicenseAsync_DoesNotConsumeDeviceSlot()
    {
        await using var db = NewContext();
        var license = new FakeLocalLicenseClientService();
        var svc = NewSvc(db, license: license);

        var result = await svc.ValidateLicenseAsync(new LocalLicenseValidationRequest
        {
            Mode = LocalSetupModes.NewGym,
            LicenseKey = "HY-LCL-TEST1-TEST2",
        });

        Assert.True(result.Valid);
        Assert.Equal(1, license.CheckCallCount);
        Assert.Equal(0, license.ActivateCallCount);
        Assert.Equal(0, license.ReleaseCallCount);
    }

    [Fact]
    public async Task CompleteFirstRunAsync_LicenseActivationFails_DoesNotProvisionAnything()
    {
        await using var db = NewContext();
        var provisioning = new FakeTenantProvisioningService(db)
        {
            NextResult = Result<ProvisionTenantResponse>.Success(new ProvisionTenantResponse
            {
                TenantId = Guid.NewGuid(),
                GymCode = "GYM-SHOULD-NOT-EXIST",
                OwnerUserId = Guid.NewGuid(),
                OwnerEmail = "owner@example.com",
            }),
        };
        var license = new FakeLocalLicenseClientService
        {
            NextResult = new LocalLicenseActivationOutcome
            {
                Success = false,
                ErrorCode = "device_limit_exceeded",
                ErrorMessage = "This license has reached its device limit.",
            },
        };
        var svc = NewSvc(db, provisioning, license);

        var result = await svc.CompleteFirstRunAsync(ValidRequest(LocalSetupModes.NewGym));

        Assert.False(result.IsSuccess);
        Assert.Equal(LocalSetupLicenseStates.Invalid, result.Message);
        Assert.Equal(1, license.ActivateCallCount);
        Assert.Equal(0, provisioning.CallCount);

        var status = await svc.GetStatusAsync();
        Assert.False(status.IsCompleted);
    }

    [Fact]
    public async Task CompleteFirstRunAsync_MissingLicenseKey_FailsWithoutCallingActivation()
    {
        await using var db = NewContext();
        var provisioning = new FakeTenantProvisioningService(db);
        var license = new FakeLocalLicenseClientService();
        var svc = NewSvc(db, provisioning, license);

        var request = ValidRequest(LocalSetupModes.NewGym);
        request.LicenseKey = "";

        var result = await svc.CompleteFirstRunAsync(request);

        Assert.False(result.IsSuccess);
        Assert.Equal(0, license.ActivateCallCount);
        Assert.Equal(0, provisioning.CallCount);
    }

    [Fact]
    public async Task CompleteFirstRunAsync_NewGym_InvalidLicenseDoesNotRetireExisting()
    {
        await using var db = NewContext();
        var users = new FakeUserManager();
        var (tenantId, memberId, actor) = await SeedCompletedGymAsync(db, users);
        var backup = new FakeBackupHealthService();
        var license = new FakeLocalLicenseClientService
        {
            NextResult = new LocalLicenseActivationOutcome { Success = false, ErrorCode = "activation_failed" },
        };
        var fake = new FakeTenantProvisioningService(db);
        var svc = NewSvc(db, fake, license, users, backup);
        var request = ValidRequest(LocalSetupModes.NewGym);
        request.ConfirmPhrase = LocalSetupModes.NewGymConfirmPhrase;

        var result = await svc.CompleteFirstRunAsync(request, actor);

        Assert.False(result.IsSuccess);
        Assert.Equal(0, fake.CallCount);
        Assert.False((await db.Tenants.FindAsync(tenantId))!.IsDeleted);
        Assert.True(await db.GymMembers.IgnoreQueryFilters().AnyAsync(m => m.Id == memberId));
    }

    [Fact]
    public async Task CompleteFirstRunAsync_NewGym_WrongConfirmationDoesNotBackupOrRetire()
    {
        await using var db = NewContext();
        var users = new FakeUserManager();
        var (tenantId, _, actor) = await SeedCompletedGymAsync(db, users);
        var backup = new FakeBackupHealthService();
        var fake = new FakeTenantProvisioningService(db);
        var svc = NewSvc(db, fake, users: users, backup: backup);
        var request = ValidRequest(LocalSetupModes.NewGym);
        request.ConfirmPhrase = "start new gym";

        var result = await svc.CompleteFirstRunAsync(request, actor);

        Assert.False(result.IsSuccess);
        Assert.Equal(LocalSetupErrorCodes.ConfirmationFailed, result.Message);
        Assert.Equal(0, backup.TriggerCount);
        Assert.Equal(0, fake.CallCount);
        Assert.False((await db.Tenants.FindAsync(tenantId))!.IsDeleted);
    }

    [Fact]
    public async Task CompleteFirstRunAsync_NewGym_BackupFailureLeavesGymUntouched()
    {
        await using var db = NewContext();
        var users = new FakeUserManager();
        var (tenantId, memberId, actor) = await SeedCompletedGymAsync(db, users);
        var backup = new FakeBackupHealthService { FailTrigger = true };
        var fake = new FakeTenantProvisioningService(db);
        var svc = NewSvc(db, fake, users: users, backup: backup);
        var request = ValidRequest(LocalSetupModes.NewGym);
        request.ConfirmPhrase = LocalSetupModes.NewGymConfirmPhrase;

        var result = await svc.CompleteFirstRunAsync(request, actor);

        Assert.False(result.IsSuccess);
        Assert.Equal(LocalSetupErrorCodes.BackupFailed, result.Message);
        Assert.Contains("has not been changed", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, fake.CallCount);
        Assert.False((await db.Tenants.FindAsync(tenantId))!.IsDeleted);
        Assert.True(await db.GymMembers.IgnoreQueryFilters().AnyAsync(m => m.Id == memberId));
    }

    [Fact]
    public async Task CompleteFirstRunAsync_NewGym_BackupNeverCompletesIsFailure()
    {
        await using var db = NewContext();
        var users = new FakeUserManager();
        await SeedCompletedGymAsync(db, users);
        var backup = new FakeBackupHealthService { NeverCompletes = true };
        var fake = new FakeTenantProvisioningService(db);
        var svc = NewSvc(db, fake, users: users, backup: backup);
        var request = ValidRequest(LocalSetupModes.NewGym);
        request.ConfirmPhrase = LocalSetupModes.NewGymConfirmPhrase;

        var result = await svc.CompleteFirstRunAsync(request, new LocalSetupActor(true, (await db.Tenants.SingleAsync()).Id));

        Assert.False(result.IsSuccess);
        Assert.Equal(LocalSetupErrorCodes.BackupFailed, result.Message);
        Assert.Equal(0, fake.CallCount);
    }

    [Fact]
    public async Task CompleteFirstRunAsync_NewGym_ServerUnavailableDoesNotAcceptOfflineLicense()
    {
        await using var db = NewContext();
        var users = new FakeUserManager();
        var (_, _, actor) = await SeedCompletedGymAsync(db, users);
        var backup = new FakeBackupHealthService();
        var license = new FakeLocalLicenseClientService
        {
            Status = new()
            {
                HasLicense = true,
                SignatureValid = true,
                WithinGracePeriod = true,
                LicenseKey = "HY-LCL-TEST1-TEST2",
            },
            NextResult = new LocalLicenseActivationOutcome { Success = false, ErrorCode = "not_configured" },
        };
        var fake = new FakeTenantProvisioningService(db);
        var svc = NewSvc(db, fake, license, users, backup);
        var request = ValidRequest(LocalSetupModes.NewGym);
        request.ConfirmPhrase = LocalSetupModes.NewGymConfirmPhrase;

        var result = await svc.CompleteFirstRunAsync(request, actor);

        Assert.False(result.IsSuccess);
        Assert.Equal(LocalSetupLicenseStates.ServerUnavailable, result.Message);
        Assert.Equal(0, fake.CallCount);
    }

    [Fact]
    public async Task CompleteFirstRunAsync_NewGym_SuccessRetiresOldGymAndProvisions()
    {
        await using var db = NewContext();
        var users = new FakeUserManager();
        var (oldTenantId, memberId, actor) = await SeedCompletedGymAsync(db, users);
        var newTenantId = Guid.NewGuid();
        var fake = new FakeTenantProvisioningService(db)
        {
            NextResult = Result<ProvisionTenantResponse>.Success(new ProvisionTenantResponse
            {
                TenantId = newTenantId,
                GymCode = "GYM-NEW-0001",
                OwnerUserId = Guid.NewGuid(),
                OwnerEmail = "owner@example.com",
            }),
        };
        var backup = new FakeBackupHealthService();
        var svc = NewSvc(db, fake, users: users, backup: backup);
        var request = ValidRequest(LocalSetupModes.NewGym);
        request.GymName = "Gym B";
        request.ConfirmPhrase = LocalSetupModes.NewGymConfirmPhrase;

        var result = await svc.CompleteFirstRunAsync(request, actor);

        Assert.True(result.IsSuccess);
        Assert.Equal(newTenantId, result.Data!.TenantId);
        Assert.Equal("GYM-NEW-0001", result.Data.GymCode);
        Assert.Equal(1, fake.CallCount);
        Assert.False(fake.LastRequest!.StartTrial);
        Assert.Equal(1, backup.TriggerCount);
        var retired = await db.Tenants.IgnoreQueryFilters().FirstAsync(t => t.Id == oldTenantId);
        Assert.True(retired.IsDeleted);
        Assert.Equal($"retired.{oldTenantId:N}@retired.local", retired.Email);
        Assert.True(await db.GymMembers.IgnoreQueryFilters().AnyAsync(m => m.Id == memberId && m.TenantId == oldTenantId));
        Assert.False(await db.Tenants.AnyAsync(t => t.Id == oldTenantId && !t.IsDeleted));
    }

    [Fact]
    public async Task CompleteFirstRunAsync_NewGym_ProvisionFailureRestoresOldGym()
    {
        await using var db = NewContext();
        var users = new FakeUserManager();
        var (oldTenantId, memberId, actor) = await SeedCompletedGymAsync(db, users);
        var fake = new FakeTenantProvisioningService(db)
        {
            NextResult = Result<ProvisionTenantResponse>.Failure("Provisioning failed"),
        };
        var backup = new FakeBackupHealthService();
        var svc = NewSvc(db, fake, users: users, backup: backup);
        var request = ValidRequest(LocalSetupModes.NewGym);
        request.ConfirmPhrase = LocalSetupModes.NewGymConfirmPhrase;

        var result = await svc.CompleteFirstRunAsync(request, actor);

        Assert.False(result.IsSuccess);
        Assert.Equal(LocalSetupErrorCodes.GymCreateFailed, result.Message);
        Assert.Equal(1, fake.CallCount);
        var restored = await db.Tenants.IgnoreQueryFilters().FirstAsync(t => t.Id == oldTenantId);
        Assert.False(restored.IsDeleted);
        Assert.Equal("owner@example.com", restored.Email);
        Assert.True(await db.GymMembers.IgnoreQueryFilters().AnyAsync(m => m.Id == memberId));
    }

    [Fact]
    public async Task CompleteFirstRunAsync_NewGym_ProvisionFailureClearsTrackedTenantBeforeRestore()
    {
        await using var db = NewContext();
        var users = new FakeUserManager();
        var (oldTenantId, memberId, actor) = await SeedCompletedGymAsync(db, users);
        var fake = new FakeTenantProvisioningService(db)
        {
            NextResult = Result<ProvisionTenantResponse>.Failure("Provisioning failed"),
            LeaveUnsavedTenantOnFailure = true,
        };
        var backup = new FakeBackupHealthService();
        var svc = NewSvc(db, fake, users: users, backup: backup);
        var request = ValidRequest(LocalSetupModes.NewGym);
        request.ConfirmPhrase = LocalSetupModes.NewGymConfirmPhrase;

        var result = await svc.CompleteFirstRunAsync(request, actor);

        Assert.False(result.IsSuccess);
        Assert.Equal(LocalSetupErrorCodes.GymCreateFailed, result.Message);
        var restored = await db.Tenants.IgnoreQueryFilters().FirstAsync(t => t.Id == oldTenantId);
        Assert.False(restored.IsDeleted);
        Assert.Equal("owner@example.com", restored.Email);
        Assert.True(await db.GymMembers.IgnoreQueryFilters().AnyAsync(m => m.Id == memberId));
        Assert.Equal(1, await db.Tenants.IgnoreQueryFilters().CountAsync(t => t.Email == "owner@example.com"));
    }

    [Fact]
    public async Task CompleteFirstRunAsync_NewGym_FreesDeletedTenantEmailBeforeRetry()
    {
        await using var db = NewContext();
        var (oldTenantId, _) = await SeedExistingGymAsync(db);
        var old = await db.Tenants.FindAsync(oldTenantId);
        old!.IsDeleted = true;
        old.IsActive = false;
        await db.SaveChangesAsync();

        var newTenantId = Guid.NewGuid();
        var fake = new FakeTenantProvisioningService(db)
        {
            NextResult = Result<ProvisionTenantResponse>.Success(new ProvisionTenantResponse
            {
                TenantId = newTenantId,
                GymCode = "GYM-NEW-0002",
                OwnerUserId = Guid.NewGuid(),
                OwnerEmail = "owner@example.com",
            }),
        };
        var svc = NewSvc(db, fake);
        var request = ValidRequest(LocalSetupModes.NewGym);

        var result = await svc.CompleteFirstRunAsync(request);

        Assert.True(result.IsSuccess);
        Assert.Equal($"retired.{oldTenantId:N}@retired.local",
            (await db.Tenants.IgnoreQueryFilters().FirstAsync(t => t.Id == oldTenantId)).Email);
    }

    [Fact]
    public async Task CompleteFirstRunAsync_ExistingGym_OwnerCreateFailureLeavesGym()
    {
        await using var db = NewContext();
        var (tenantId, memberId) = await SeedExistingGymAsync(db);
        var users = new FakeUserManager
        {
            CreateResult = IdentityResult.Failed(new IdentityError { Description = "password too weak" }),
        };
        var fake = new FakeTenantProvisioningService(db);
        var svc = NewSvc(db, fake, users: users);

        var result = await svc.CompleteFirstRunAsync(ValidRequest(LocalSetupModes.ExistingGym));

        Assert.False(result.IsSuccess);
        Assert.Equal(LocalSetupErrorCodes.OwnerCreateFailed, result.Message);
        Assert.Equal(0, fake.CallCount);
        Assert.False((await db.Tenants.FindAsync(tenantId))!.IsDeleted);
        Assert.True(await db.GymMembers.IgnoreQueryFilters().AnyAsync(m => m.Id == memberId));
    }

    [Fact]
    public async Task PrepareBackupAsync_FailureDoesNotChangeGym()
    {
        await using var db = NewContext();
        var (tenantId, _) = await SeedExistingGymAsync(db);
        var backup = new FakeBackupHealthService { FailVerify = true };
        var svc = NewSvc(db, backup: backup);

        var result = await svc.PrepareBackupAsync();

        Assert.False(result.Success);
        Assert.Equal(LocalSetupErrorCodes.BackupFailed, result.ErrorCode);
        Assert.False((await db.Tenants.FindAsync(tenantId))!.IsDeleted);
    }

    [Fact]
    public async Task ValidateLicenseAsync_NewGym_NotRegistered()
    {
        await using var db = NewContext();
        var license = new FakeLocalLicenseClientService
        {
            NextResult = new LocalLicenseActivationOutcome { Success = false, ErrorCode = "unbound" },
        };
        var svc = NewSvc(db, license: license);

        var result = await svc.ValidateLicenseAsync(new LocalLicenseValidationRequest
        {
            Mode = LocalSetupModes.NewGym,
            LicenseKey = "HY-LCL-TEST1-TEST2",
        });

        Assert.False(result.Valid);
        Assert.Equal(LocalSetupLicenseStates.NotRegistered, result.State);
    }

    [Fact]
    public async Task GetStatusAsync_AfterInterruptedExistingGymSetup_StillShowsGym()
    {
        await using var db = NewContext();
        await SeedExistingGymAsync(db);
        var license = new FakeLocalLicenseClientService
        {
            NextResult = new LocalLicenseActivationOutcome { Success = false, ErrorCode = "activation_failed" },
        };
        var svc = NewSvc(db, license: license);
        await svc.CompleteFirstRunAsync(ValidRequest(LocalSetupModes.ExistingGym));

        var status = await svc.GetStatusAsync();

        Assert.False(status.IsCompleted);
        Assert.Equal("H3O", status.GymName);
        Assert.Equal("GYM-GYM-7050", status.GymCode);
        Assert.True(status.CanRestoreExistingGym);
    }
}
