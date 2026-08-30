namespace GMS.Tests.Platform;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using GMS.Platform.DTOs;
using GMS.Platform.Entities;
using GMS.Platform.Persistence;
using GMS.Platform.Services;

public class PlatformUserAdminServiceTests
{
    private static (PlatformDbContext Db, PlatformUserAdminService Svc, Guid AdminId) Create()
    {
        var db = new PlatformDbContext(
            new DbContextOptionsBuilder<PlatformDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var hasher = new PasswordHasher<PlatformAdminUser>();
        var audit = new PlatformAuditService(db, new HttpContextAccessor(), NullLogger<PlatformAuditService>.Instance);
        var svc = new PlatformUserAdminService(db, hasher, audit);

        var adminId = Guid.NewGuid();
        db.PlatformAdminUsers.Add(new PlatformAdminUser
        {
            Id = adminId, Email = "seed.admin@gymflow.local", FullName = "Seed Admin",
            Role = "platform_admin", PasswordHash = "x", IsActive = true, CreatedAtUtc = DateTime.UtcNow
        });
        db.SaveChanges();

        return (db, svc, adminId);
    }

    [Fact]
    public async Task CreateAsync_HashesPasswordAndStartsWithMfaDisabled()
    {
        var (db, svc, adminId) = Create();

        var (result, user) = await svc.CreateAsync(adminId, new CreatePlatformUserRequest
        {
            Email = "New.Ops@GymFlow.Local",
            FullName = "New Ops",
            Role = "platform_ops",
            Password = "SuperSecret123!"
        }, ipAddress: "127.0.0.1");

        Assert.True(result.Success, result.ErrorMessage);
        Assert.NotNull(user);
        Assert.Equal("new.ops@gymflow.local", user!.Email); // normalized lowercase
        Assert.False(user.MfaEnabled);
        Assert.True(user.IsActive);

        var stored = await db.PlatformAdminUsers.SingleAsync(u => u.Id == user.Id);
        Assert.NotEqual("SuperSecret123!", stored.PasswordHash);
        Assert.NotEmpty(stored.PasswordHash);
    }

    [Fact]
    public async Task CreateAsync_RejectsDuplicateEmail_InvalidRole_AndWeakPassword()
    {
        var (_, svc, adminId) = Create();

        var dup = await svc.CreateAsync(adminId, new CreatePlatformUserRequest
        {
            Email = "seed.admin@gymflow.local", FullName = "Dup", Role = "platform_support", Password = "LongEnough123"
        }, null);
        Assert.False(dup.Result.Success);
        Assert.Equal("EMAIL_TAKEN", dup.Result.ErrorCode);

        var badRole = await svc.CreateAsync(adminId, new CreatePlatformUserRequest
        {
            Email = "x@gymflow.local", FullName = "X", Role = "super_admin", Password = "LongEnough123"
        }, null);
        Assert.False(badRole.Result.Success);
        Assert.Equal("INVALID_ROLE", badRole.Result.ErrorCode);

        var weakPassword = await svc.CreateAsync(adminId, new CreatePlatformUserRequest
        {
            Email = "y@gymflow.local", FullName = "Y", Role = "platform_support", Password = "short"
        }, null);
        Assert.False(weakPassword.Result.Success);
        Assert.Equal("WEAK_PASSWORD", weakPassword.Result.ErrorCode);
    }

    [Fact]
    public async Task DisableAsync_CannotDisableOwnAccount()
    {
        var (_, svc, adminId) = Create();

        var result = await svc.DisableAsync(adminId, adminId, null);

        Assert.False(result.Result.Success);
        Assert.Equal("SELF_PROTECTED", result.Result.ErrorCode);
    }

    [Fact]
    public async Task ChangeRoleAsync_CannotChangeOwnRole()
    {
        var (_, svc, adminId) = Create();

        var result = await svc.ChangeRoleAsync(adminId, adminId, "platform_support", null);

        Assert.False(result.Result.Success);
        Assert.Equal("SELF_PROTECTED", result.Result.ErrorCode);
    }

    [Fact]
    public async Task DisableAndReactivate_RoundTripsCleanly_AndRejectsRedundantCalls()
    {
        var (_, svc, adminId) = Create();
        var (createResult, created) = await svc.CreateAsync(adminId, new CreatePlatformUserRequest
        {
            Email = "target@gymflow.local", FullName = "Target", Role = "platform_support", Password = "LongEnough123"
        }, null);
        Assert.True(createResult.Success, createResult.ErrorMessage);

        var disable = await svc.DisableAsync(adminId, created!.Id, null);
        Assert.True(disable.Result.Success, disable.Result.ErrorMessage);
        Assert.False(disable.User!.IsActive);

        var disableAgain = await svc.DisableAsync(adminId, created.Id, null);
        Assert.False(disableAgain.Result.Success);
        Assert.Equal("ALREADY_DISABLED", disableAgain.Result.ErrorCode);

        var reactivate = await svc.ReactivateAsync(adminId, created.Id, null);
        Assert.True(reactivate.Result.Success, reactivate.Result.ErrorMessage);
        Assert.True(reactivate.User!.IsActive);

        var reactivateAgain = await svc.ReactivateAsync(adminId, created.Id, null);
        Assert.False(reactivateAgain.Result.Success);
        Assert.Equal("ALREADY_ACTIVE", reactivateAgain.Result.ErrorCode);
    }

    [Fact]
    public async Task ChangeRoleAsync_UpdatesRoleForAnotherUser()
    {
        var (_, svc, adminId) = Create();
        var (_, created) = await svc.CreateAsync(adminId, new CreatePlatformUserRequest
        {
            Email = "promote@gymflow.local", FullName = "Promote Me", Role = "platform_support", Password = "LongEnough123"
        }, null);

        var result = await svc.ChangeRoleAsync(adminId, created!.Id, "platform_ops", null);

        Assert.True(result.Result.Success, result.Result.ErrorMessage);
        Assert.Equal("platform_ops", result.User!.Role);
    }

    [Fact]
    public async Task ListAsync_NeverExposesPasswordHash()
    {
        var (_, svc, adminId) = Create();
        await svc.CreateAsync(adminId, new CreatePlatformUserRequest
        {
            Email = "list.check@gymflow.local", FullName = "List Check", Role = "platform_support", Password = "LongEnough123"
        }, null);

        var list = await svc.ListAsync();

        Assert.Contains(list, u => u.Email == "list.check@gymflow.local");
        // PlatformUserDto has no PasswordHash property at all — compile-time guarantee, this just
        // documents the intent for a reader who might otherwise wonder why it isn't asserted.
    }
}
