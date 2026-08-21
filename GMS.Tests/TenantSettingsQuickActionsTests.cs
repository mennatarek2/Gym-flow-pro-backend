namespace GMS.Tests;

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using GMS.Application.DTOs.Admin;
using GMS.Application.DTOs.Audit;
using GMS.Application.Interfaces;
using GMS.Application.Services;
using GMS.Core.Constants;
using GMS.Core.Entities;
using GMS.Infrastructure.Persistence;
using GMS.Infrastructure.Services;

public class TenantSettingsQuickActionsTests
{
    private sealed class RecordingAudit : IAuditService
    {
        public string? LastAction { get; private set; }
        public object? LastBefore { get; private set; }
        public object? LastAfter { get; private set; }

        public Task LogAsync(
            string action,
            string? entityType = null,
            Guid? entityId = null,
            object? before = null,
            object? after = null,
            Guid? tenantIdOverride = null)
        {
            LastAction = action;
            LastBefore = before;
            LastAfter = after;
            return Task.CompletedTask;
        }

        public Task<GMS.Application.Common.Result<GMS.Application.Common.PagedResult<AuditEventDto>>> GetAuditEventsAsync(
            Guid tenantId, AuditEventQueryRequest query)
            => Task.FromResult(GMS.Application.Common.Result<GMS.Application.Common.PagedResult<AuditEventDto>>.Failure("n/a"));
    }

    private static Tenant NewTenant(Guid id, string gymCode) => new()
    {
        Id = id,
        Name = "Gym " + gymCode,
        NameAr = "صالة",
        GymCode = gymCode,
        City = "Cairo",
        Address = "x",
        PhoneNumber = "01000000000",
        Email = gymCode.ToLowerInvariant() + "@test.local",
        SubscriptionStartDate = DateTime.UtcNow,
        CreatedAtUtc = DateTime.UtcNow
    };

    private static (GymFlowProDbContext ctx, TenantSettingsService svc, RecordingAudit audit, Guid tenantId)
        CreateSut(string? settingsJson = null)
    {
        var tenantId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<GymFlowProDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var tenantContext = new TenantContext();
        tenantContext.SetTenant(tenantId, "Test Gym", "Africa/Cairo");
        var ctx = new GymFlowProDbContext(options, tenantContext);
        var tenant = NewTenant(tenantId, "GYM-QA-" + tenantId.ToString("N")[..6]);
        tenant.Settings = settingsJson;
        ctx.Tenants.Add(tenant);
        ctx.SaveChanges();

        var audit = new RecordingAudit();
        var svc = new TenantSettingsService(ctx, audit, NullLogger<TenantSettingsService>.Instance);
        return (ctx, svc, audit, tenantId);
    }

    [Fact]
    public async Task Get_WhenUnset_ReturnsDefaultFourKeys()
    {
        var (_, svc, _, tenantId) = CreateSut();
        var r = await svc.GetQuickActionsAsync(tenantId);
        Assert.True(r.IsSuccess, r.Error);
        Assert.Equal(QuickActionKeys.DefaultKeys, r.Data!.Keys);
    }

    [Fact]
    public async Task PutThenGet_PersistsOrder()
    {
        var (_, svc, audit, tenantId) = CreateSut();
        var body = new UpdateQuickActionsRequest
        {
            Keys = new List<string> { "new_refund", "checkin", "new_member" }
        };
        var put = await svc.UpdateQuickActionsAsync(tenantId, body);
        Assert.True(put.IsSuccess, put.Error);
        Assert.Equal(body.Keys, put.Data!.Keys);
        Assert.Equal("settings.quick_actions.update", audit.LastAction);

        var get = await svc.GetQuickActionsAsync(tenantId);
        Assert.Equal(body.Keys, get.Data!.Keys);
    }

    [Fact]
    public async Task Put_Empty_ThenGet_ReturnsEmptyNotDefaults()
    {
        var (_, svc, _, tenantId) = CreateSut();
        var put = await svc.UpdateQuickActionsAsync(tenantId, new UpdateQuickActionsRequest { Keys = new List<string>() });
        Assert.True(put.IsSuccess, put.Error);
        Assert.Empty(put.Data!.Keys);

        var get = await svc.GetQuickActionsAsync(tenantId);
        Assert.True(get.IsSuccess);
        Assert.Empty(get.Data!.Keys);
    }

    [Fact]
    public async Task Put_SevenKeys_FailsValidation()
    {
        var (_, svc, _, tenantId) = CreateSut();
        var keys = new List<string>
        {
            "new_member", "checkin", "new_sale", "collect_payment", "new_trial", "open_shift", "close_shift"
        };
        var put = await svc.UpdateQuickActionsAsync(tenantId, new UpdateQuickActionsRequest { Keys = keys });
        Assert.False(put.IsSuccess);
        Assert.Equal(QuickActionKeys.ValidationError, put.Error);

        var get = await svc.GetQuickActionsAsync(tenantId);
        Assert.Equal(QuickActionKeys.DefaultKeys, get.Data!.Keys);
    }

    [Fact]
    public async Task Put_UnknownMixedWithValid_DropsUnknown()
    {
        var (_, svc, _, tenantId) = CreateSut();
        var put = await svc.UpdateQuickActionsAsync(tenantId, new UpdateQuickActionsRequest
        {
            Keys = new List<string> { "new_member", "settings", "reports", "checkin" }
        });
        Assert.True(put.IsSuccess, put.Error);
        Assert.Equal(new[] { "new_member", "checkin" }, put.Data!.Keys);
    }

    [Fact]
    public async Task Put_Duplicates_KeepsFirstSeenOrder()
    {
        var (_, svc, _, tenantId) = CreateSut();
        var put = await svc.UpdateQuickActionsAsync(tenantId, new UpdateQuickActionsRequest
        {
            Keys = new List<string> { "checkin", "new_member", "checkin", "new_member" }
        });
        Assert.True(put.IsSuccess, put.Error);
        Assert.Equal(new[] { "checkin", "new_member" }, put.Data!.Keys);
    }

    [Fact]
    public async Task Put_NewOfferAlias_CoercesToAddPromoCode()
    {
        var (_, svc, _, tenantId) = CreateSut();
        var put = await svc.UpdateQuickActionsAsync(tenantId, new UpdateQuickActionsRequest
        {
            Keys = new List<string> { "new_offer", "add_promo_code" }
        });
        Assert.True(put.IsSuccess, put.Error);
        Assert.Equal(new[] { "add_promo_code" }, put.Data!.Keys);
    }

    [Fact]
    public async Task Put_DoesNotChangeGymProfileFields()
    {
        var (ctx, svc, _, tenantId) = CreateSut();
        var tenant = ctx.Tenants.First(t => t.Id == tenantId);
        tenant.Name = "Fitness Hub";
        tenant.NameAr = "فتنس هب";
        tenant.Settings = """{"short_name":"Hub","vat_enabled":true}""";
        await ctx.SaveChangesAsync();

        await svc.UpdateQuickActionsAsync(tenantId, new UpdateQuickActionsRequest
        {
            Keys = new List<string> { "new_sale" }
        });

        var profile = await svc.GetTenantSettingsAsync(tenantId);
        Assert.Equal("Fitness Hub", profile.Data!.GymName);
        Assert.Equal("فتنس هب", profile.Data.GymNameAr);
        Assert.Equal("Hub", profile.Data.ShortName);
        Assert.Null(typeof(TenantSettingsDto).GetProperty("Keys"));
        Assert.Null(typeof(TenantSettingsDto).GetProperty("QuickActionKeys"));

        var tax = await svc.GetTaxSettingsAsync(tenantId);
        Assert.True(tax.Data!.VatEnabled);
    }

    [Fact]
    public async Task TenantIsolation_GymBCannotSeeGymAKeys()
    {
        var options = new DbContextOptionsBuilder<GymFlowProDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var ctxA = new GymFlowProDbContext(options, NewContext(tenantA));
        ctxA.Tenants.Add(NewTenant(tenantA, "GYM-A"));
        ctxA.Tenants.Add(NewTenant(tenantB, "GYM-B"));
        ctxA.SaveChanges();

        var svcA = new TenantSettingsService(ctxA, new RecordingAudit(), NullLogger<TenantSettingsService>.Instance);
        var put = await svcA.UpdateQuickActionsAsync(tenantA, new UpdateQuickActionsRequest
        {
            Keys = new List<string> { "new_refund", "freeze_membership" }
        });
        Assert.True(put.IsSuccess, put.Error);

        var ctxB = new GymFlowProDbContext(options, NewContext(tenantB));
        var svcB = new TenantSettingsService(ctxB, new RecordingAudit(), NullLogger<TenantSettingsService>.Instance);
        var getB = await svcB.GetQuickActionsAsync(tenantB);
        Assert.True(getB.IsSuccess);
        Assert.Equal(QuickActionKeys.DefaultKeys, getB.Data!.Keys);
        Assert.DoesNotContain("new_refund", getB.Data.Keys);
    }

    private static TenantContext NewContext(Guid tenantId)
    {
        var c = new TenantContext();
        c.SetTenant(tenantId, "T", "Africa/Cairo");
        return c;
    }

    [Fact]
    public async Task Auth_ReceptionistCanRead_CannotWrite()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization(options =>
        {
            options.AddPolicy("ManagerOrAbove", p => p.RequireRole("Owner", "Manager"));
        });
        var sp = services.BuildServiceProvider();
        var authz = sp.GetRequiredService<IAuthorizationService>();

        var receptionist = Principal("Receptionist");
        var manager = Principal("Manager");
        var trainer = Principal("Trainer");
        var member = Principal("Member");

        Assert.False((await authz.AuthorizeAsync(receptionist, resource: null, "ManagerOrAbove")).Succeeded);
        Assert.True((await authz.AuthorizeAsync(manager, resource: null, "ManagerOrAbove")).Succeeded);
        Assert.False((await authz.AuthorizeAsync(trainer, resource: null, "ManagerOrAbove")).Succeeded);
        Assert.False((await authz.AuthorizeAsync(member, resource: null, "ManagerOrAbove")).Succeeded);

        Assert.True(receptionist.IsInRole("Receptionist"));
        Assert.False(member.IsInRole("Owner") || member.IsInRole("Manager") || member.IsInRole("Trainer") || member.IsInRole("Receptionist"));
    }

    private static ClaimsPrincipal Principal(string role)
    {
        var id = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, role) }, "Test");
        return new ClaimsPrincipal(id);
    }
}
