namespace GMS.Tests;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using GMS.Application.DTOs.Hr;
using GMS.Application.Interfaces;
using GMS.Application.Services;
using GMS.Core.Entities;
using GMS.Infrastructure.Persistence;
using GMS.Infrastructure.Services;

public class EmployeeScheduleServiceTests
{
    private sealed class NoOpAudit : IAuditService
    {
        public Task LogAsync(string action, string? entityType = null, Guid? entityId = null, object? before = null, object? after = null, Guid? tenantIdOverride = null)
            => Task.CompletedTask;

        public Task<GMS.Application.Common.Result<GMS.Application.Common.PagedResult<GMS.Application.DTOs.Audit.AuditEventDto>>> GetAuditEventsAsync(
            Guid tenantId, GMS.Application.DTOs.Audit.AuditEventQueryRequest query)
            => Task.FromResult(GMS.Application.Common.Result<GMS.Application.Common.PagedResult<GMS.Application.DTOs.Audit.AuditEventDto>>.Failure("n/a"));
    }

    private static async Task<(GymFlowProDbContext ctx, EmployeeScheduleService svc, Guid tenantId, Guid employeeId, Guid shiftId)> SeedAsync(int employeeCount = 1)
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

        var shift = new EmployeeShift
        {
            TenantId = tenantId,
            Name = "Morning",
            StartTime = new TimeOnly(9, 0),
            EndTime = new TimeOnly(17, 0),
            GraceMinutes = 10,
            CreatedAtUtc = DateTime.UtcNow
        };
        ctx.EmployeeShifts.Add(shift);

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

        var svc = new EmployeeScheduleService(ctx, new NoOpAudit(), NullLogger<EmployeeScheduleService>.Instance);
        return (ctx, svc, tenantId, employee.Id, shift.Id);
    }

    [Fact]
    public async Task AssignAsync_CreatesAssignment()
    {
        var (_, svc, tenantId, employeeId, shiftId) = await SeedAsync();

        var result = await svc.AssignAsync(tenantId, new AssignScheduleRequest
        {
            EmployeeId = employeeId,
            EmployeeShiftId = shiftId,
            Date = new DateOnly(2026, 9, 1)
        });

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("Morning", result.Data!.EmployeeShiftName);
    }

    [Fact]
    public async Task AssignAsync_RejectsDuplicateDateForSameEmployee()
    {
        var (_, svc, tenantId, employeeId, shiftId) = await SeedAsync();
        var date = new DateOnly(2026, 9, 1);
        await svc.AssignAsync(tenantId, new AssignScheduleRequest { EmployeeId = employeeId, EmployeeShiftId = shiftId, Date = date });

        var second = await svc.AssignAsync(tenantId, new AssignScheduleRequest { EmployeeId = employeeId, EmployeeShiftId = shiftId, Date = date });

        Assert.False(second.IsSuccess);
    }

    [Fact]
    public async Task RemoveThenReassign_Succeeds()
    {
        var (_, svc, tenantId, employeeId, shiftId) = await SeedAsync();
        var date = new DateOnly(2026, 9, 1);
        await svc.AssignAsync(tenantId, new AssignScheduleRequest { EmployeeId = employeeId, EmployeeShiftId = shiftId, Date = date });

        var removed = await svc.RemoveAsync(tenantId, employeeId, date);
        Assert.True(removed.IsSuccess, removed.Error);

        var reassigned = await svc.AssignAsync(tenantId, new AssignScheduleRequest { EmployeeId = employeeId, EmployeeShiftId = shiftId, Date = date });
        Assert.True(reassigned.IsSuccess, reassigned.Error);
    }

    [Fact]
    public async Task BulkAssignAsync_SkipsAlreadyAssignedCellsButAssignsTheRest()
    {
        var (_, svc, tenantId, employeeId, shiftId) = await SeedAsync();
        var from = new DateOnly(2026, 9, 1);
        var to = new DateOnly(2026, 9, 3);
        await svc.AssignAsync(tenantId, new AssignScheduleRequest { EmployeeId = employeeId, EmployeeShiftId = shiftId, Date = from });

        var result = await svc.BulkAssignAsync(tenantId, new BulkAssignScheduleRequest
        {
            EmployeeIds = new List<Guid> { employeeId },
            EmployeeShiftId = shiftId,
            DateFrom = from,
            DateTo = to
        });

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(2, result.Data!.AssignedCount); // Sept 2 and 3
        Assert.Equal(1, result.Data.SkippedCount);   // Sept 1 already assigned
    }

    [Fact]
    public async Task ListAsync_IsTenantIsolated()
    {
        var (_, svcA, tenantA, employeeA, shiftA) = await SeedAsync();
        var (_, svcB, tenantB, employeeB, shiftB) = await SeedAsync();
        var date = new DateOnly(2026, 9, 1);

        await svcA.AssignAsync(tenantA, new AssignScheduleRequest { EmployeeId = employeeA, EmployeeShiftId = shiftA, Date = date });
        await svcB.AssignAsync(tenantB, new AssignScheduleRequest { EmployeeId = employeeB, EmployeeShiftId = shiftB, Date = date });

        var listA = await svcA.ListAsync(tenantA, date, date);
        Assert.Single(listA.Data!);
        Assert.Equal(employeeA, listA.Data![0].EmployeeId);
    }
}
