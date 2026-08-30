namespace GMS.Tests;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using GMS.Application.DTOs.Hr;
using GMS.Application.Interfaces;
using GMS.Application.Services;
using GMS.Core.Entities;
using GMS.Infrastructure.Persistence;
using GMS.Infrastructure.Services;

public class EmployeeShiftServiceTests
{
    private sealed class NoOpAudit : IAuditService
    {
        public Task LogAsync(string action, string? entityType = null, Guid? entityId = null, object? before = null, object? after = null, Guid? tenantIdOverride = null)
            => Task.CompletedTask;

        public Task<GMS.Application.Common.Result<GMS.Application.Common.PagedResult<GMS.Application.DTOs.Audit.AuditEventDto>>> GetAuditEventsAsync(
            Guid tenantId, GMS.Application.DTOs.Audit.AuditEventQueryRequest query)
            => Task.FromResult(GMS.Application.Common.Result<GMS.Application.Common.PagedResult<GMS.Application.DTOs.Audit.AuditEventDto>>.Failure("n/a"));
    }

    private static async Task<(EmployeeShiftService svc, Guid tenantId)> SeedAsync()
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
        await ctx.SaveChangesAsync();

        return (new EmployeeShiftService(ctx, new NoOpAudit(), NullLogger<EmployeeShiftService>.Instance), tenantId);
    }

    [Fact]
    public async Task CreateAsync_PersistsShiftTemplate()
    {
        var (svc, tenantId) = await SeedAsync();

        var result = await svc.CreateAsync(tenantId, new CreateEmployeeShiftRequest
        {
            Name = "Morning",
            StartTime = new TimeOnly(8, 0),
            EndTime = new TimeOnly(16, 0),
            BreakMinutes = 30,
            GraceMinutes = 10
        });

        Assert.True(result.IsSuccess, result.Error);
        Assert.False(result.Data!.CrossesMidnight);
    }

    [Fact]
    public async Task CreateAsync_RejectsZeroLengthShift()
    {
        var (svc, tenantId) = await SeedAsync();

        var result = await svc.CreateAsync(tenantId, new CreateEmployeeShiftRequest
        {
            Name = "Broken",
            StartTime = new TimeOnly(8, 0),
            EndTime = new TimeOnly(8, 0)
        });

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task CreateAsync_EveningShiftEndingAtMidnight_FlagsCrossesMidnight()
    {
        var (svc, tenantId) = await SeedAsync();

        var result = await svc.CreateAsync(tenantId, new CreateEmployeeShiftRequest
        {
            Name = "Evening",
            StartTime = new TimeOnly(16, 0),
            EndTime = new TimeOnly(0, 0)
        });

        Assert.True(result.IsSuccess, result.Error);
        Assert.True(result.Data!.CrossesMidnight);
    }

    [Fact]
    public async Task Shifts_AreIsolatedPerTenant()
    {
        var (svcA, tenantA) = await SeedAsync();
        var (svcB, tenantB) = await SeedAsync();

        await svcA.CreateAsync(tenantA, new CreateEmployeeShiftRequest { Name = "Morning", StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(16, 0) });
        await svcB.CreateAsync(tenantB, new CreateEmployeeShiftRequest { Name = "Morning", StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(17, 0) });

        var listA = await svcA.ListAsync(tenantA);
        Assert.Single(listA.Data!);
        Assert.Equal(new TimeOnly(8, 0), listA.Data![0].StartTime);
    }
}
