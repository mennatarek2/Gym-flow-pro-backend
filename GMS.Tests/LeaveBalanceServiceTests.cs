namespace GMS.Tests;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using GMS.Application.Interfaces;
using GMS.Application.Services;
using GMS.Core.Constants;
using GMS.Core.Entities;
using GMS.Infrastructure.Persistence;
using GMS.Infrastructure.Services;

public class LeaveBalanceServiceTests
{
    private sealed class NoOpAudit : IAuditService
    {
        public Task LogAsync(string action, string? entityType = null, Guid? entityId = null, object? before = null, object? after = null, Guid? tenantIdOverride = null)
            => Task.CompletedTask;

        public Task<GMS.Application.Common.Result<GMS.Application.Common.PagedResult<GMS.Application.DTOs.Audit.AuditEventDto>>> GetAuditEventsAsync(
            Guid tenantId, GMS.Application.DTOs.Audit.AuditEventQueryRequest query)
            => Task.FromResult(GMS.Application.Common.Result<GMS.Application.Common.PagedResult<GMS.Application.DTOs.Audit.AuditEventDto>>.Failure("n/a"));
    }

    private static async Task<(LeaveBalanceService svc, Guid tenantId, Guid employeeId)> SeedAsync()
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
        var employee = new Employee
        {
            TenantId = tenantId,
            EmployeeNumber = "EMP-0001",
            FirstName = "Ahmed",
            LastName = "Mohamed",
            HireDate = new DateOnly(2026, 1, 1),
            CreatedAtUtc = DateTime.UtcNow
        };
        ctx.Employees.Add(employee);
        await ctx.SaveChangesAsync();

        return (new LeaveBalanceService(ctx, new NoOpAudit(), NullLogger<LeaveBalanceService>.Instance), tenantId, employee.Id);
    }

    [Fact]
    public async Task ListAsync_AutoProvisionsDefaultEntitlementsForTrackableTypes()
    {
        var (svc, tenantId, employeeId) = await SeedAsync();

        var result = await svc.ListAsync(tenantId, employeeId, 2026);

        Assert.True(result.IsSuccess, result.Error);
        var byType = result.Data!.ToDictionary(b => b.LeaveType);
        Assert.Equal(LeaveTypes.DefaultEntitlementDays(LeaveTypes.Annual), byType[LeaveTypes.Annual].EntitledDays);
        Assert.Equal(LeaveTypes.DefaultEntitlementDays(LeaveTypes.Sick), byType[LeaveTypes.Sick].EntitledDays);
        Assert.DoesNotContain(LeaveTypes.Unpaid, byType.Keys); // never tracked
        Assert.Equal(0, byType[LeaveTypes.Annual].UsedDays);
        Assert.Equal(byType[LeaveTypes.Annual].EntitledDays, byType[LeaveTypes.Annual].RemainingDays);
    }

    [Fact]
    public async Task SetEntitlementAsync_OverridesDefault()
    {
        var (svc, tenantId, employeeId) = await SeedAsync();

        var result = await svc.SetEntitlementAsync(tenantId, employeeId, LeaveTypes.Annual, 2026, 30m);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(30m, result.Data!.EntitledDays);

        var list = await svc.ListAsync(tenantId, employeeId, 2026);
        Assert.Equal(30m, list.Data!.Single(b => b.LeaveType == LeaveTypes.Annual).EntitledDays);
    }

    [Fact]
    public async Task SetEntitlementAsync_RejectsUnpaidLeaveType()
    {
        var (svc, tenantId, employeeId) = await SeedAsync();

        var result = await svc.SetEntitlementAsync(tenantId, employeeId, LeaveTypes.Unpaid, 2026, 10m);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task SetEntitlementAsync_RejectsNegativeDays()
    {
        var (svc, tenantId, employeeId) = await SeedAsync();

        var result = await svc.SetEntitlementAsync(tenantId, employeeId, LeaveTypes.Annual, 2026, -1m);

        Assert.False(result.IsSuccess);
    }
}
