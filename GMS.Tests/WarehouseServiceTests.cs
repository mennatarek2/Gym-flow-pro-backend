namespace GMS.Tests;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using GMS.Application.DTOs.Inventory;
using GMS.Application.Interfaces;
using GMS.Application.Services;
using GMS.Core.Entities;
using GMS.Core.Interfaces;
using GMS.Infrastructure.Persistence;
using GMS.Infrastructure.Services;

public class WarehouseServiceTests
{
    private sealed class NoOpAudit : IAuditService
    {
        public Task LogAsync(string action, string? entityType = null, Guid? entityId = null, object? before = null, object? after = null, Guid? tenantIdOverride = null)
            => Task.CompletedTask;

        public Task<GMS.Application.Common.Result<GMS.Application.Common.PagedResult<GMS.Application.DTOs.Audit.AuditEventDto>>> GetAuditEventsAsync(
            Guid tenantId, GMS.Application.DTOs.Audit.AuditEventQueryRequest query)
            => Task.FromResult(GMS.Application.Common.Result<GMS.Application.Common.PagedResult<GMS.Application.DTOs.Audit.AuditEventDto>>.Failure("n/a"));
    }

    private static (GymFlowProDbContext ctx, WarehouseService svc, Guid tenantId) CreateSut()
    {
        var tenantId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<GymFlowProDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var tenantContext = new TenantContext();
        tenantContext.SetTenant(tenantId, "Test Gym", "Africa/Cairo");
        var ctx = new GymFlowProDbContext(options, tenantContext);
        ctx.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Test Gym",
            NameAr = "صالة",
            GymCode = $"T-{tenantId:N}"[..12],
            City = "Cairo",
            Address = "x",
            PhoneNumber = "01000000000",
            Email = $"{tenantId:N}@test.local",
            SubscriptionStartDate = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow
        });
        ctx.SaveChanges();
        var svc = new WarehouseService(ctx, new NoOpAudit(), NullLogger<WarehouseService>.Instance);
        return (ctx, svc, tenantId);
    }

    [Fact]
    public async Task FirstWarehouse_BecomesDefault_EvenIfNotRequested()
    {
        var (_, svc, tenantId) = CreateSut();

        var created = await svc.CreateAsync(tenantId, new CreateWarehouseRequest
        {
            Code = "main",
            Name = "Main Store",
            IsDefault = false
        });

        Assert.True(created.IsSuccess, created.Error);
        Assert.True(created.Data!.IsDefault);
        Assert.Equal("MAIN", created.Data.Code);

        var def = await svc.GetDefaultAsync(tenantId);
        Assert.True(def.IsSuccess);
        Assert.NotNull(def.Data);
        Assert.Equal(created.Data.Id, def.Data!.Id);
    }

    [Fact]
    public async Task CreateTwo_SetDefault_SwitchesDefault()
    {
        var (_, svc, tenantId) = CreateSut();

        var main = await svc.CreateAsync(tenantId, new CreateWarehouseRequest
        {
            Code = "MAIN",
            Name = "Main Store"
        });
        Assert.True(main.IsSuccess, main.Error);

        var desk = await svc.CreateAsync(tenantId, new CreateWarehouseRequest
        {
            Code = "DESK",
            Name = "Front Desk",
            IsDefault = false
        });
        Assert.True(desk.IsSuccess, desk.Error);
        Assert.False(desk.Data!.IsDefault);

        var switched = await svc.SetDefaultAsync(tenantId, desk.Data.Id);
        Assert.True(switched.IsSuccess, switched.Error);
        Assert.True(switched.Data!.IsDefault);

        var mainAgain = await svc.GetAsync(tenantId, main.Data!.Id);
        Assert.False(mainAgain.Data!.IsDefault);

        var def = await svc.GetDefaultAsync(tenantId);
        Assert.Equal(desk.Data.Id, def.Data!.Id);
    }

    [Fact]
    public async Task CannotDeactivate_OnlyActive_OrDefault()
    {
        var (_, svc, tenantId) = CreateSut();

        var only = await svc.CreateAsync(tenantId, new CreateWarehouseRequest
        {
            Code = "ONLY",
            Name = "Only"
        });
        Assert.True(only.IsSuccess, only.Error);

        var deactivateOnly = await svc.UpdateAsync(tenantId, only.Data!.Id, new UpdateWarehouseRequest
        {
            Name = "Only",
            IsActive = false
        });
        Assert.False(deactivateOnly.IsSuccess);
        Assert.Contains("default", deactivateOnly.Error!, StringComparison.OrdinalIgnoreCase);

        var second = await svc.CreateAsync(tenantId, new CreateWarehouseRequest
        {
            Code = "TWO",
            Name = "Two"
        });
        Assert.True(second.IsSuccess, second.Error);

        // Deactivate non-default while another active exists — OK
        var ok = await svc.UpdateAsync(tenantId, second.Data!.Id, new UpdateWarehouseRequest
        {
            Name = "Two",
            IsActive = false
        });
        Assert.True(ok.IsSuccess, ok.Error);

        // Cannot deactivate last remaining active (default)
        var fail = await svc.UpdateAsync(tenantId, only.Data.Id, new UpdateWarehouseRequest
        {
            Name = "Only",
            IsActive = false
        });
        Assert.False(fail.IsSuccess);
    }

    [Fact]
    public async Task DuplicateCode_Rejected()
    {
        var (_, svc, tenantId) = CreateSut();

        Assert.True((await svc.CreateAsync(tenantId, new CreateWarehouseRequest
        {
            Code = "MAIN",
            Name = "A"
        })).IsSuccess);

        var dup = await svc.CreateAsync(tenantId, new CreateWarehouseRequest
        {
            Code = "main",
            Name = "B"
        });
        Assert.False(dup.IsSuccess);
        Assert.Contains("code", dup.Error!, StringComparison.OrdinalIgnoreCase);
    }
}
