namespace GMS.Tests;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using GMS.Application.DTOs.Admin;
using GMS.Application.Services;
using GMS.Core.Constants;
using GMS.Core.Entities;
using GMS.Infrastructure.Persistence;
using GMS.Infrastructure.Services;

public class GymOccupancyTests
{
    private static Tenant NewTenant(Guid id, string? settingsJson = null, bool active = true) => new()
    {
        Id = id,
        Name = "Fitness Hub",
        NameAr = "فتنس هب",
        GymCode = "GYM-" + id.ToString("N")[..8],
        City = "Cairo",
        Address = "x",
        PhoneNumber = "01000000000",
        Email = id.ToString("N")[..8] + "@test.local",
        SubscriptionStartDate = DateTime.UtcNow,
        CreatedAtUtc = DateTime.UtcNow,
        IsActive = active,
        Settings = settingsJson
    };

    private static (GymFlowProDbContext ctx, GymOccupancyService occ, TenantSettingsService settings, Guid tenantId)
        CreateSut(string? settingsJson = null, bool active = true)
    {
        var tenantId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<GymFlowProDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var tenantContext = new TenantContext();
        tenantContext.SetTenant(tenantId, "Fitness Hub", "Africa/Cairo");
        var ctx = new GymFlowProDbContext(options, tenantContext);
        ctx.Tenants.Add(NewTenant(tenantId, settingsJson, active));
        ctx.SaveChanges();

        var occ = new GymOccupancyService(ctx, NullLogger<GymOccupancyService>.Instance);
        var settings = new TenantSettingsService(ctx, new NoOpAudit(), NullLogger<TenantSettingsService>.Instance);
        return (ctx, occ, settings, tenantId);
    }

    private sealed class NoOpAudit : GMS.Application.Interfaces.IAuditService
    {
        public Task LogAsync(
            string action,
            string? entityType = null,
            Guid? entityId = null,
            object? before = null,
            object? after = null,
            Guid? tenantIdOverride = null) => Task.CompletedTask;

        public Task<GMS.Application.Common.Result<GMS.Application.Common.PagedResult<GMS.Application.DTOs.Audit.AuditEventDto>>> GetAuditEventsAsync(
            Guid tenantId, GMS.Application.DTOs.Audit.AuditEventQueryRequest query)
            => Task.FromResult(
                GMS.Application.Common.Result<GMS.Application.Common.PagedResult<GMS.Application.DTOs.Audit.AuditEventDto>>.Failure("n/a"));
    }

    private static void AddVisit(GymFlowProDbContext ctx, Guid tenantId, DateTime checkIn, DateTime? checkOut = null)
    {
        ctx.GymAttendances.Add(new GymAttendance
        {
            TenantId = tenantId,
            MemberId = Guid.NewGuid(),
            MembershipId = Guid.NewGuid(),
            CheckInAtUtc = checkIn,
            CheckOutAtUtc = checkOut,
            EntryMethod = "qr"
        });
    }

    [Fact]
    public async Task UnsetCapacity_ReturnsNullMax_StillCountsInside()
    {
        var (ctx, occ, _, tenantId) = CreateSut();
        AddVisit(ctx, tenantId, DateTime.UtcNow.Date.AddHours(8));
        AddVisit(ctx, tenantId, DateTime.UtcNow.Date.AddHours(9));
        await ctx.SaveChangesAsync();

        var r = await occ.GetOccupancyAsync(tenantId);
        Assert.True(r.IsSuccess, r.Error);
        Assert.Null(r.Data!.MaxCapacity);
        Assert.Null(r.Data.Available);
        Assert.Null(r.Data.OccupancyPercent);
        Assert.Equal(2, r.Data.CurrentlyInside);
        Assert.Equal("attendance_open_visits", r.Data.Source);
    }

    [Fact]
    public async Task ClassCheckins_DoNotCountTowardOccupancy()
    {
        var (ctx, occ, _, tenantId) = CreateSut("""{"gym_max_capacity":10}""");
        AddVisit(ctx, tenantId, DateTime.UtcNow.Date.AddHours(8));
        ctx.GymAttendances.Add(new GymAttendance
        {
            TenantId = tenantId,
            MemberId = Guid.NewGuid(),
            MembershipId = Guid.NewGuid(),
            CheckInAtUtc = DateTime.UtcNow.Date.AddHours(9),
            EntryMethod = "class"
        });
        await ctx.SaveChangesAsync();

        var r = await occ.GetOccupancyAsync(tenantId);
        Assert.True(r.IsSuccess, r.Error);
        Assert.Equal(1, r.Data!.CurrentlyInside);
    }

    [Fact]
    public async Task OpenVisits_MatchAttendanceInGym_NotCheckedOutOrYesterday()
    {
        var (ctx, occ, _, tenantId) = CreateSut("""{"gym_max_capacity":300}""");
        var today = DateTime.UtcNow.Date;
        AddVisit(ctx, tenantId, today.AddHours(8));
        AddVisit(ctx, tenantId, today.AddHours(9));
        AddVisit(ctx, tenantId, today.AddHours(10));
        AddVisit(ctx, tenantId, today.AddHours(7), checkOut: today.AddHours(8));
        AddVisit(ctx, tenantId, today.AddDays(-1).AddHours(10));
        await ctx.SaveChangesAsync();

        var r = await occ.GetOccupancyAsync(tenantId);
        Assert.True(r.IsSuccess, r.Error);
        Assert.Equal(300, r.Data!.MaxCapacity);
        Assert.Equal(3, r.Data.CurrentlyInside);
        Assert.Equal(297, r.Data.Available);
        Assert.Equal(1, r.Data.OccupancyPercent);
    }

    [Fact]
    public async Task OccupancyPercent_184Of300_Is61()
    {
        var (ctx, occ, _, tenantId) = CreateSut("""{"gym_max_capacity":300}""");
        var today = DateTime.UtcNow.Date;
        for (var i = 0; i < 184; i++)
            AddVisit(ctx, tenantId, today.AddMinutes(i));
        await ctx.SaveChangesAsync();

        var r = await occ.GetOccupancyAsync(tenantId);
        Assert.Equal(184, r.Data!.CurrentlyInside);
        Assert.Equal(116, r.Data.Available);
        Assert.Equal(61, r.Data.OccupancyPercent);
    }

    [Fact]
    public async Task OverCapacity_DoesNotBreak_PercentOver100_AvailableZero()
    {
        var (ctx, occ, _, tenantId) = CreateSut("""{"gym_max_capacity":300}""");
        var today = DateTime.UtcNow.Date;
        for (var i = 0; i < 315; i++)
            AddVisit(ctx, tenantId, today.AddMinutes(i % 1400));
        await ctx.SaveChangesAsync();

        var r = await occ.GetOccupancyAsync(tenantId);
        Assert.Equal(315, r.Data!.CurrentlyInside);
        Assert.Equal(0, r.Data.Available);
        Assert.Equal(105, r.Data.OccupancyPercent);
    }

    [Fact]
    public async Task InactiveGym_StillReturnsGymActiveFalse()
    {
        var (_, occ, _, tenantId) = CreateSut("""{"gym_max_capacity":300}""", active: false);
        var r = await occ.GetOccupancyAsync(tenantId);
        Assert.True(r.IsSuccess, r.Error);
        Assert.False(r.Data!.GymActive);
    }

    [Fact]
    public async Task PutCapacity_ThenGetSettings_Returns300()
    {
        var (_, _, settings, tenantId) = CreateSut();
        var put = await settings.UpdateTenantSettingsAsync(tenantId, BaseIdentity(300));
        Assert.True(put.IsSuccess, put.Error);
        Assert.Equal(300, put.Data!.GymMaxCapacity);

        var get = await settings.GetTenantSettingsAsync(tenantId);
        Assert.Equal(300, get.Data!.GymMaxCapacity);
    }

    [Fact]
    public async Task PutNullCapacity_ClearsConfiguredMax()
    {
        var (_, _, settings, tenantId) = CreateSut("""{"gym_max_capacity":300,"vat_enabled":true}""");
        var put = await settings.UpdateTenantSettingsAsync(tenantId, BaseIdentity(null));
        Assert.True(put.IsSuccess, put.Error);
        Assert.Null(put.Data!.GymMaxCapacity);

        var get = await settings.GetTenantSettingsAsync(tenantId);
        Assert.Null(get.Data!.GymMaxCapacity);
    }

    [Fact]
    public async Task PutZeroOrTooLarge_Fails()
    {
        var (_, _, settings, tenantId) = CreateSut();
        var zero = await settings.UpdateTenantSettingsAsync(tenantId, BaseIdentity(0));
        Assert.False(zero.IsSuccess);
        var big = await settings.UpdateTenantSettingsAsync(tenantId, BaseIdentity(10000));
        Assert.False(big.IsSuccess);
    }

    [Fact]
    public async Task PutCapacity_PreservesOtherSettingsKeys()
    {
        var (ctx, _, settings, tenantId) = CreateSut("""{"vat_enabled":true,"short_name":"Hub"}""");
        var put = await settings.UpdateTenantSettingsAsync(tenantId, BaseIdentity(150));
        Assert.True(put.IsSuccess, put.Error);
        var json = ctx.Tenants.First(t => t.Id == tenantId).Settings;
        Assert.Contains("vat_enabled", json);
        Assert.Contains("gym_max_capacity", json);
    }

    private static UpdateTenantSettingsRequest BaseIdentity(int? cap) => new()
    {
        GymName = "Fitness Hub",
        GymNameAr = "فتنس هب",
        GymMaxCapacity = cap
    };
}
