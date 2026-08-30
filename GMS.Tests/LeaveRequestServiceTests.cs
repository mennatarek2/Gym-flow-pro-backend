namespace GMS.Tests;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using GMS.Application.DTOs.Hr;
using GMS.Application.Interfaces;
using GMS.Application.Services;
using GMS.Core.Constants;
using GMS.Core.Entities;
using GMS.Infrastructure.Persistence;
using GMS.Infrastructure.Services;

public class LeaveRequestServiceTests
{
    private sealed class NoOpAudit : IAuditService
    {
        public Task LogAsync(string action, string? entityType = null, Guid? entityId = null, object? before = null, object? after = null, Guid? tenantIdOverride = null)
            => Task.CompletedTask;

        public Task<GMS.Application.Common.Result<GMS.Application.Common.PagedResult<GMS.Application.DTOs.Audit.AuditEventDto>>> GetAuditEventsAsync(
            Guid tenantId, GMS.Application.DTOs.Audit.AuditEventQueryRequest query)
            => Task.FromResult(GMS.Application.Common.Result<GMS.Application.Common.PagedResult<GMS.Application.DTOs.Audit.AuditEventDto>>.Failure("n/a"));
    }

    private static async Task<(GymFlowProDbContext ctx, LeaveRequestService svc, LeaveBalanceService balances, Guid tenantId, Guid employeeId)> SeedAsync()
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

        var balances = new LeaveBalanceService(ctx, new NoOpAudit(), NullLogger<LeaveBalanceService>.Instance);
        var svc = new LeaveRequestService(ctx, balances, new NoOpAudit(), NullLogger<LeaveRequestService>.Instance);
        return (ctx, svc, balances, tenantId, employee.Id);
    }

    private static CreateLeaveRequestRequest Annual(DateOnly start, DateOnly end) => new()
    {
        LeaveType = LeaveTypes.Annual,
        StartDate = start,
        EndDate = end
    };

    [Fact]
    public async Task CreateAsync_ComputesWholeDayDuration()
    {
        var (_, svc, _, tenantId, employeeId) = await SeedAsync();

        var result = await svc.CreateAsync(tenantId, employeeId, Annual(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 5)));

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(5, result.Data!.DurationDays);
        Assert.Equal(LeaveRequestStatuses.Pending, result.Data.Status);
    }

    [Fact]
    public async Task CreateAsync_RejectsEndBeforeStart()
    {
        var (_, svc, _, tenantId, employeeId) = await SeedAsync();

        var result = await svc.CreateAsync(tenantId, employeeId, Annual(new DateOnly(2026, 9, 5), new DateOnly(2026, 9, 1)));

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task CreateAsync_PermissionType_RequiresSingleDayAndFractionalDuration()
    {
        var (_, svc, _, tenantId, employeeId) = await SeedAsync();

        var multiDay = await svc.CreateAsync(tenantId, employeeId, new CreateLeaveRequestRequest
        {
            LeaveType = LeaveTypes.Permission, StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2026, 9, 2)
        });
        Assert.False(multiDay.IsSuccess);

        var ok = await svc.CreateAsync(tenantId, employeeId, new CreateLeaveRequestRequest
        {
            LeaveType = LeaveTypes.Permission, StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2026, 9, 1), DurationDays = 0.5m
        });
        Assert.True(ok.IsSuccess, ok.Error);
        Assert.Equal(0.5m, ok.Data!.DurationDays);
    }

    [Fact]
    public async Task ApproveAsync_ConsumesBalanceAndWritesOnLeaveAttendance()
    {
        var (ctx, svc, balances, tenantId, employeeId) = await SeedAsync();
        var created = await svc.CreateAsync(tenantId, employeeId, Annual(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 3)));

        var approved = await svc.ApproveAsync(tenantId, created.Data!.Id, Guid.NewGuid(), "ok");

        Assert.True(approved.IsSuccess, approved.Error);
        Assert.Equal(LeaveRequestStatuses.Approved, approved.Data!.Status);

        var balanceList = await balances.ListAsync(tenantId, employeeId, 2026);
        Assert.Equal(3m, balanceList.Data!.Single(b => b.LeaveType == LeaveTypes.Annual).UsedDays);

        var attendanceRows = await ctx.EmployeeAttendances
            .Where(a => a.TenantId == tenantId && a.EmployeeId == employeeId)
            .OrderBy(a => a.AttendanceDate)
            .ToListAsync();
        Assert.Equal(3, attendanceRows.Count);
        Assert.All(attendanceRows, a =>
        {
            Assert.Equal(AttendanceStatuses.OnLeave, a.Status);
            Assert.Equal(AttendanceSources.System, a.Source);
            Assert.Equal(created.Data.Id, a.LeaveRequestId);
        });
    }

    [Fact]
    public async Task ApproveAsync_NeverOverwritesRealCheckIn()
    {
        var (ctx, svc, _, tenantId, employeeId) = await SeedAsync();
        var leaveDate = new DateOnly(2026, 9, 1);
        ctx.EmployeeAttendances.Add(new EmployeeAttendance
        {
            TenantId = tenantId,
            EmployeeId = employeeId,
            AttendanceDate = leaveDate,
            CheckInAtUtc = DateTime.UtcNow,
            Status = AttendanceStatuses.Present,
            Source = AttendanceSources.Manual,
            CreatedAtUtc = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var created = await svc.CreateAsync(tenantId, employeeId, Annual(leaveDate, leaveDate));
        var approved = await svc.ApproveAsync(tenantId, created.Data!.Id, null, null);

        Assert.True(approved.IsSuccess, approved.Error);
        var row = await ctx.EmployeeAttendances.SingleAsync(a => a.TenantId == tenantId && a.AttendanceDate == leaveDate);
        Assert.Equal(AttendanceStatuses.Present, row.Status); // untouched — real check-in wins
    }

    [Fact]
    public async Task ApproveAsync_PermissionType_DoesNotWriteOnLeaveAttendance()
    {
        var (ctx, svc, _, tenantId, employeeId) = await SeedAsync();
        var created = await svc.CreateAsync(tenantId, employeeId, new CreateLeaveRequestRequest
        {
            LeaveType = LeaveTypes.Permission, StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2026, 9, 1), DurationDays = 0.25m
        });

        var approved = await svc.ApproveAsync(tenantId, created.Data!.Id, null, null);

        Assert.True(approved.IsSuccess, approved.Error);
        var rows = await ctx.EmployeeAttendances.Where(a => a.TenantId == tenantId && a.EmployeeId == employeeId).ToListAsync();
        Assert.Empty(rows);
    }

    [Fact]
    public async Task CreateAsync_RejectsOverlapWithApprovedLeave()
    {
        var (_, svc, _, tenantId, employeeId) = await SeedAsync();
        var first = await svc.CreateAsync(tenantId, employeeId, Annual(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 10)));
        await svc.ApproveAsync(tenantId, first.Data!.Id, null, null);

        var overlapping = await svc.CreateAsync(tenantId, employeeId, Annual(new DateOnly(2026, 9, 5), new DateOnly(2026, 9, 15)));

        Assert.False(overlapping.IsSuccess);
    }

    [Fact]
    public async Task PendingOverlappingRequests_CanCoexist_ButOnlyOneCanBeApproved()
    {
        var (_, svc, _, tenantId, employeeId) = await SeedAsync();
        var first = await svc.CreateAsync(tenantId, employeeId, Annual(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 10)));
        var second = await svc.CreateAsync(tenantId, employeeId, Annual(new DateOnly(2026, 9, 5), new DateOnly(2026, 9, 15)));
        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess); // both pending requests allowed to coexist

        var approveFirst = await svc.ApproveAsync(tenantId, first.Data!.Id, null, null);
        Assert.True(approveFirst.IsSuccess, approveFirst.Error);

        var approveSecond = await svc.ApproveAsync(tenantId, second.Data!.Id, null, null);
        Assert.False(approveSecond.IsSuccess); // now blocked by the first one's approval
    }

    [Fact]
    public async Task RejectAsync_DoesNotConsumeBalance()
    {
        var (_, svc, balances, tenantId, employeeId) = await SeedAsync();
        var created = await svc.CreateAsync(tenantId, employeeId, Annual(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 3)));

        var rejected = await svc.RejectAsync(tenantId, created.Data!.Id, null, "not enough coverage");

        Assert.True(rejected.IsSuccess, rejected.Error);
        Assert.Equal(LeaveRequestStatuses.Rejected, rejected.Data!.Status);
        var balanceList = await balances.ListAsync(tenantId, employeeId, 2026);
        Assert.Equal(0, balanceList.Data!.Single(b => b.LeaveType == LeaveTypes.Annual).UsedDays);
    }

    [Fact]
    public async Task CancelAsync_PendingRequest_NoBalanceImpact()
    {
        var (_, svc, balances, tenantId, employeeId) = await SeedAsync();
        var created = await svc.CreateAsync(tenantId, employeeId, Annual(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 3)));

        var cancelled = await svc.CancelAsync(tenantId, created.Data!.Id, null, isSelfService: true, selfEmployeeId: employeeId);

        Assert.True(cancelled.IsSuccess, cancelled.Error);
        Assert.Equal(LeaveRequestStatuses.Cancelled, cancelled.Data!.Status);
        var balanceList = await balances.ListAsync(tenantId, employeeId, 2026);
        Assert.Equal(0, balanceList.Data!.Single(b => b.LeaveType == LeaveTypes.Annual).UsedDays);
    }

    [Fact]
    public async Task CancelAsync_ApprovedRequest_RestoresBalanceAndRemovesOnLeaveAttendance()
    {
        var (ctx, svc, balances, tenantId, employeeId) = await SeedAsync();
        var created = await svc.CreateAsync(tenantId, employeeId, Annual(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 3)));
        await svc.ApproveAsync(tenantId, created.Data!.Id, null, null);

        var cancelled = await svc.CancelAsync(tenantId, created.Data.Id, null, isSelfService: false, selfEmployeeId: null);

        Assert.True(cancelled.IsSuccess, cancelled.Error);
        var balanceList = await balances.ListAsync(tenantId, employeeId, 2026);
        Assert.Equal(0, balanceList.Data!.Single(b => b.LeaveType == LeaveTypes.Annual).UsedDays);

        var rows = await ctx.EmployeeAttendances.Where(a => a.TenantId == tenantId && a.EmployeeId == employeeId).ToListAsync();
        Assert.Empty(rows); // soft-deleted, query-filtered out
    }

    [Fact]
    public async Task CancelAsync_SelfService_CannotCancelApproved()
    {
        var (_, svc, _, tenantId, employeeId) = await SeedAsync();
        var created = await svc.CreateAsync(tenantId, employeeId, Annual(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 3)));
        await svc.ApproveAsync(tenantId, created.Data!.Id, null, null);

        var result = await svc.CancelAsync(tenantId, created.Data.Id, null, isSelfService: true, selfEmployeeId: employeeId);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task CancelAsync_SelfService_CannotCancelAnotherEmployeesRequest()
    {
        var (_, svc, _, tenantId, employeeId) = await SeedAsync();
        var created = await svc.CreateAsync(tenantId, employeeId, Annual(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 3)));

        var result = await svc.CancelAsync(tenantId, created.Data!.Id, null, isSelfService: true, selfEmployeeId: Guid.NewGuid());

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task ApproveAsync_AfterCancelledLeave_RestoresSoftDeletedAttendanceRow()
    {
        var (ctx, svc, _, tenantId, employeeId) = await SeedAsync();
        var leaveDate = new DateOnly(2026, 8, 27);
        var first = await svc.CreateAsync(tenantId, employeeId, Annual(leaveDate, leaveDate));
        await svc.ApproveAsync(tenantId, first.Data!.Id, null, null);
        await svc.CancelAsync(tenantId, first.Data.Id, null, isSelfService: false, selfEmployeeId: null);

        var second = await svc.CreateAsync(tenantId, employeeId, Annual(leaveDate, leaveDate));
        var approved = await svc.ApproveAsync(tenantId, second.Data!.Id, null, null);

        Assert.True(approved.IsSuccess, approved.Error);
        var row = await ctx.EmployeeAttendances
            .IgnoreQueryFilters()
            .SingleAsync(a => a.TenantId == tenantId && a.EmployeeId == employeeId && a.AttendanceDate == leaveDate);
        Assert.False(row.IsDeleted);
        Assert.Equal(AttendanceStatuses.OnLeave, row.Status);
        Assert.Equal(second.Data.Id, row.LeaveRequestId);
    }

    [Fact]
    public async Task LeaveRequests_AreTenantIsolated()
    {
        var (_, svcA, _, tenantA, employeeA) = await SeedAsync();
        var (_, svcB, _, tenantB, employeeB) = await SeedAsync();

        await svcA.CreateAsync(tenantA, employeeA, Annual(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 3)));
        await svcB.CreateAsync(tenantB, employeeB, Annual(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 3)));

        var listA = await svcA.ListAsync(tenantA);
        Assert.Single(listA.Data!);
        Assert.Equal(employeeA, listA.Data![0].EmployeeId);
    }
}
