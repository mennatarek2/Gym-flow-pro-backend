namespace GMS.Tests;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using GMS.Application.DTOs.Auth;
using GMS.Application.Interfaces;
using GMS.Application.Services;
using GMS.Core.Entities;
using GMS.Core.Entities.Identity;
using GMS.Core.Interfaces;
using GMS.Infrastructure.Persistence;
using GMS.Infrastructure.Services;

public class AuthStaffLastLoginTests
{
    private sealed class StubTokenService : ITokenService
    {
        public Task<string> GenerateAccessTokenAsync(ApplicationUser user, Guid tenantId, string gymCode, IList<string> roles, IEnumerable<string>? permissions = null) =>
            Task.FromResult("access");

        public Task<string> GenerateImpersonationAccessTokenAsync(ApplicationUser user, Guid tenantId, string gymCode, IList<string> roles, IEnumerable<string>? permissions, Guid platformUserId, int lifetimeMinutes = 30) =>
            Task.FromResult("imp");

        public string GenerateRefreshToken() => "refresh-token";
        public string HashToken(string token) => "hash:" + token;
        public System.Security.Claims.ClaimsPrincipal? ValidateExpiredToken(string token) => null;
    }

    private sealed class StubSubscriptionAccess : ISubscriptionAccessService
    {
        public Task<SubscriptionAccessSnapshot?> GetAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult<SubscriptionAccessSnapshot?>(new SubscriptionAccessSnapshot { Status = "active" });
    }

    private sealed class StubPermissionCache : IPermissionCacheService
    {
        public Task<IReadOnlySet<string>?> GetAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlySet<string>?>(null);

        public Task SetAsync(Guid tenantId, Guid userId, IReadOnlySet<string> permissions, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task InvalidateAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class LoginUserManager : AdminServiceStaffManagementTests.FakeUserManager
    {
        public ApplicationUser? User { get; set; }

        public override Task<ApplicationUser?> FindByEmailAsync(string email) =>
            Task.FromResult(User != null && string.Equals(User.Email, email, StringComparison.OrdinalIgnoreCase) ? User : null);
    }

    private static AuthService CreateAuth(GymFlowProDbContext db, LoginUserManager users)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["JwtSettings:RefreshTokenExpirationDays"] = "30",
            ["JwtSettings:AccessTokenExpirationMinutes"] = "15"
        }).Build();

        return new AuthService(
            users,
            db,
            new StubTokenService(),
            otpService: null!,
            otpCacheService: null!,
            otpDeliveryStrategy: null!,
            memberAppActivation: null!,
            config,
            NullLogger<AuthService>.Instance,
            new DefaultPermissionProvider(),
            new StubPermissionCache(),
            new StubSubscriptionAccess());
    }

    private static GymFlowProDbContext CreateDbWithoutTenant()
    {
        var options = new DbContextOptionsBuilder<GymFlowProDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new GymFlowProDbContext(options, new TenantContext());
    }

    [Fact]
    public async Task Login_WithoutTenantContext_WritesLastLogin_RefreshDoesNot()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDbWithoutTenant();

        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Gym",
            GymCode = "GYM1",
            IsActive = true
        });

        var identity = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = "sara@gym.test",
            UserName = "sara@gym.test",
            FirstName = "Sara",
            LastName = "Manager",
            TenantId = tenantId,
            IsActive = true,
            SecurityStamp = Guid.NewGuid().ToString()
        };
        db.Users.Add(identity);
        var appUser = new AppUser
        {
            TenantId = tenantId,
            UserId = identity.Id.ToString(),
            FirstName = "Sara",
            LastName = "Manager",
            Email = "sara@gym.test",
            Role = "Manager",
            LastLoginAtUtc = null
        };
        db.AppUsers.Add(appUser);
        await db.SaveChangesAsync();

        var users = new LoginUserManager { User = identity };
        users.Roles[identity.Id] = new List<string> { "Manager" };
        var auth = CreateAuth(db, users);

        var before = DateTime.UtcNow.AddSeconds(-2);
        var login = await auth.LoginAsync(new LoginRequest
        {
            Email = "sara@gym.test",
            Password = "Passw0rd",
            GymCode = "GYM1"
        });

        Assert.True(login.IsSuccess);
        var stored = await db.AppUsers.IgnoreQueryFilters().SingleAsync(a => a.Id == appUser.Id);
        Assert.NotNull(stored.LastLoginAtUtc);
        Assert.True(stored.LastLoginAtUtc >= before);

        var written = stored.LastLoginAtUtc;
        var refresh = await auth.RefreshTokenAsync(new RefreshTokenRequest
        {
            RefreshToken = login.Data!.RefreshToken
        });
        Assert.True(refresh.IsSuccess);

        var afterRefresh = await db.AppUsers.IgnoreQueryFilters().SingleAsync(a => a.Id == appUser.Id);
        Assert.Equal(written, afterRefresh.LastLoginAtUtc);
    }

    [Fact]
    public async Task Login_IgnoreQueryFilters_DoesNotUpdateOtherTenantAppUser()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        await using var db = CreateDbWithoutTenant();

        db.Tenants.Add(new Tenant { Id = tenantA, Name = "A", GymCode = "GYMA", IsActive = true });
        db.Tenants.Add(new Tenant { Id = tenantB, Name = "B", GymCode = "GYMB", IsActive = true });

        var identity = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = "sara@gym.test",
            UserName = "sara@gym.test",
            FirstName = "Sara",
            LastName = "Manager",
            TenantId = tenantA,
            IsActive = true,
            SecurityStamp = Guid.NewGuid().ToString()
        };
        db.Users.Add(identity);

        var foreignSentinel = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        db.AppUsers.Add(new AppUser
        {
            TenantId = tenantB,
            UserId = identity.Id.ToString(),
            FirstName = "Other",
            LastName = "Gym",
            Email = "other@b.test",
            Role = "Manager",
            LastLoginAtUtc = foreignSentinel
        });
        var home = new AppUser
        {
            TenantId = tenantA,
            UserId = identity.Id.ToString(),
            FirstName = "Sara",
            LastName = "Manager",
            Email = "sara@gym.test",
            Role = "Manager",
            LastLoginAtUtc = null
        };
        db.AppUsers.Add(home);
        await db.SaveChangesAsync();

        var users = new LoginUserManager { User = identity };
        users.Roles[identity.Id] = new List<string> { "Manager" };
        var auth = CreateAuth(db, users);

        var result = await auth.LoginAsync(new LoginRequest
        {
            Email = "sara@gym.test",
            Password = "Passw0rd",
            GymCode = "GYMA"
        });

        Assert.True(result.IsSuccess);
        var rows = await db.AppUsers.IgnoreQueryFilters().ToListAsync();
        var homeStored = rows.Single(a => a.Id == home.Id);
        var foreignStored = rows.Single(a => a.TenantId == tenantB);
        Assert.NotNull(homeStored.LastLoginAtUtc);
        Assert.Equal(foreignSentinel, foreignStored.LastLoginAtUtc);
        Assert.Equal(identity.Id.ToString(), homeStored.UserId);
        Assert.Equal(tenantA, homeStored.TenantId);
    }

    [Fact]
    public async Task SuccessfulLogin_WritesAppUserLastLoginAtUtc()
    {
        var tenantId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<GymFlowProDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var tenantContext = new TenantContext();
        tenantContext.SetTenant(tenantId, "Gym", "Africa/Cairo");
        await using var db = new GymFlowProDbContext(options, tenantContext);

        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Gym",
            GymCode = "GYM1",
            IsActive = true
        });

        var identity = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = "sara@gym.test",
            UserName = "sara@gym.test",
            FirstName = "Sara",
            LastName = "Manager",
            TenantId = tenantId,
            IsActive = true,
            SecurityStamp = Guid.NewGuid().ToString()
        };
        db.Users.Add(identity);
        var appUser = new AppUser
        {
            TenantId = tenantId,
            UserId = identity.Id.ToString(),
            FirstName = "Sara",
            LastName = "Manager",
            Email = "sara@gym.test",
            Role = "Manager",
            LastLoginAtUtc = null
        };
        db.AppUsers.Add(appUser);
        await db.SaveChangesAsync();

        var users = new LoginUserManager { User = identity };
        users.Roles[identity.Id] = new List<string> { "Manager" };
        var auth = CreateAuth(db, users);

        var before = DateTime.UtcNow.AddSeconds(-2);
        var result = await auth.LoginAsync(new LoginRequest
        {
            Email = "sara@gym.test",
            Password = "Passw0rd",
            GymCode = "GYM1"
        });

        Assert.True(result.IsSuccess);
        var stored = await db.AppUsers.SingleAsync(a => a.Id == appUser.Id);
        Assert.NotNull(stored.LastLoginAtUtc);
        Assert.True(stored.LastLoginAtUtc >= before);
    }

    [Fact]
    public async Task Refresh_DoesNotUpdateLastLoginAtUtc()
    {
        var tenantId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<GymFlowProDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var tenantContext = new TenantContext();
        tenantContext.SetTenant(tenantId, "Gym", "Africa/Cairo");
        await using var db = new GymFlowProDbContext(options, tenantContext);

        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Gym",
            GymCode = "GYM1",
            IsActive = true
        });

        var identity = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = "sara@gym.test",
            UserName = "sara@gym.test",
            FirstName = "Sara",
            LastName = "Manager",
            TenantId = tenantId,
            IsActive = true,
            SecurityStamp = Guid.NewGuid().ToString()
        };
        db.Users.Add(identity);

        var originalLogin = DateTime.UtcNow.AddDays(-2);
        db.AppUsers.Add(new AppUser
        {
            TenantId = tenantId,
            UserId = identity.Id.ToString(),
            FirstName = "Sara",
            LastName = "Manager",
            Email = "sara@gym.test",
            Role = "Manager",
            LastLoginAtUtc = originalLogin
        });

        var tokens = new StubTokenService();
        db.Set<RefreshToken>().Add(new RefreshToken
        {
            UserId = identity.Id,
            TenantId = tenantId,
            TokenHash = tokens.HashToken("refresh-in"),
            ExpiresAtUtc = DateTime.UtcNow.AddDays(10),
            User = identity
        });
        await db.SaveChangesAsync();

        var users = new LoginUserManager { User = identity };
        users.Roles[identity.Id] = new List<string> { "Manager" };
        var auth = CreateAuth(db, users);

        var result = await auth.RefreshTokenAsync(new RefreshTokenRequest { RefreshToken = "refresh-in" });

        Assert.True(result.IsSuccess);
        var stored = await db.AppUsers.SingleAsync();
        Assert.Equal(originalLogin, stored.LastLoginAtUtc);
    }
}
