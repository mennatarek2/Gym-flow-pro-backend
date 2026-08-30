namespace GMS.Tests;

using Microsoft.EntityFrameworkCore;
using GMS.Application.Services;
using GMS.Core.Constants;
using GMS.Core.Entities;
using GMS.Core.Utilities;
using GMS.Infrastructure.Persistence;
using GMS.Infrastructure.Services;

public class HrDashboardServiceTests
{
    private static async Task<(GymFlowProDbContext ctx, HrDashboardService svc, Guid tenantId)> SeedAsync()
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

        return (ctx, new HrDashboardService(ctx), tenantId);
    }

    private static Employee NewEmployee(Guid tenantId, string number) => new()
    {
        TenantId = tenantId,
        EmployeeNumber = number,
        FirstName = "E",
        LastName = number,
        HireDate = new DateOnly(2026, 1, 1),
        Status = EmployeeStatuses.Active,
        CreatedAtUtc = DateTime.UtcNow
    };

    [Fact]
    public async Task GetAsync_CountsActiveEmployeesOnly()
    {
        var (ctx, svc, tenantId) = await SeedAsync();
        var active = NewEmployee(tenantId, "EMP-0001");
        var terminated = NewEmployee(tenantId, "EMP-0002");
        terminated.Status = EmployeeStatuses.Terminated;
        ctx.Employees.AddRange(active, terminated);
        await ctx.SaveChangesAsync();

        var result = await svc.GetAsync(tenantId, includePayroll: false);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Data!.EmployeeCount);
    }

    [Fact]
    public async Task GetAsync_CountsPresentLateAndOvertimeFromRealAttendanceRows()
    {
        var (ctx, svc, tenantId) = await SeedAsync();
        var today = MembershipOperational.TodayCairo();
        var e1 = NewEmployee(tenantId, "EMP-0001");
        var e2 = NewEmployee(tenantId, "EMP-0002");
        ctx.Employees.AddRange(e1, e2);
        await ctx.SaveChangesAsync();

        ctx.EmployeeAttendances.Add(new EmployeeAttendance
        {
            TenantId = tenantId, EmployeeId = e1.Id, AttendanceDate = today,
            CheckInAtUtc = DateTime.UtcNow, Status = AttendanceStatuses.Present, Source = AttendanceSources.Manual,
            OvertimeMinutes = 30, CreatedAtUtc = DateTime.UtcNow
        });
        ctx.EmployeeAttendances.Add(new EmployeeAttendance
        {
            TenantId = tenantId, EmployeeId = e2.Id, AttendanceDate = today,
            CheckInAtUtc = DateTime.UtcNow, Status = AttendanceStatuses.Late, Source = AttendanceSources.Manual,
            OvertimeMinutes = 15, CreatedAtUtc = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var result = await svc.GetAsync(tenantId, includePayroll: false);

        Assert.Equal(2, result.Data!.PresentToday);
        Assert.Equal(1, result.Data.LateToday);
        Assert.Equal(45, result.Data.OvertimeMinutesToday);
    }

    [Fact]
    public async Task GetAsync_AbsentToday_IsScheduledWithNoCheckInAndNotOnLeave()
    {
        var (ctx, svc, tenantId) = await SeedAsync();
        var today = MembershipOperational.TodayCairo();
        var scheduledNoShow = NewEmployee(tenantId, "EMP-0001");
        var scheduledOnLeave = NewEmployee(tenantId, "EMP-0002");
        var notScheduled = NewEmployee(tenantId, "EMP-0003");
        ctx.Employees.AddRange(scheduledNoShow, scheduledOnLeave, notScheduled);
        var shift = new EmployeeShift { TenantId = tenantId, Name = "Morning", StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(17, 0), CreatedAtUtc = DateTime.UtcNow };
        ctx.EmployeeShifts.Add(shift);
        await ctx.SaveChangesAsync();

        ctx.EmployeeScheduleAssignments.Add(new EmployeeScheduleAssignment { TenantId = tenantId, EmployeeId = scheduledNoShow.Id, EmployeeShiftId = shift.Id, Date = today, CreatedAtUtc = DateTime.UtcNow });
        ctx.EmployeeScheduleAssignments.Add(new EmployeeScheduleAssignment { TenantId = tenantId, EmployeeId = scheduledOnLeave.Id, EmployeeShiftId = shift.Id, Date = today, CreatedAtUtc = DateTime.UtcNow });
        ctx.LeaveRequests.Add(new LeaveRequest
        {
            TenantId = tenantId, EmployeeId = scheduledOnLeave.Id, LeaveType = LeaveTypes.Annual,
            StartDate = today, EndDate = today, DurationDays = 1, Status = LeaveRequestStatuses.Approved,
            RequestedAtUtc = DateTime.UtcNow, CreatedAtUtc = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var result = await svc.GetAsync(tenantId, includePayroll: false);

        Assert.Equal(1, result.Data!.AbsentToday); // only scheduledNoShow — onLeave and notScheduled excluded
        Assert.Equal(1, result.Data.OnLeaveToday);
    }

    [Fact]
    public async Task GetAsync_CountsPendingLeaveAndUpcomingContractExpirationsAndDocuments()
    {
        var (ctx, svc, tenantId) = await SeedAsync();
        var today = MembershipOperational.TodayCairo();
        var e1 = NewEmployee(tenantId, "EMP-0001");
        ctx.Employees.Add(e1);
        await ctx.SaveChangesAsync();

        ctx.LeaveRequests.Add(new LeaveRequest
        {
            TenantId = tenantId, EmployeeId = e1.Id, LeaveType = LeaveTypes.Sick, StartDate = today.AddDays(5), EndDate = today.AddDays(6),
            DurationDays = 2, Status = LeaveRequestStatuses.Pending, RequestedAtUtc = DateTime.UtcNow, CreatedAtUtc = DateTime.UtcNow
        });
        ctx.EmployeeContracts.Add(new EmployeeContract
        {
            TenantId = tenantId, EmployeeId = e1.Id, ContractNumber = "CT-0001", EmploymentType = EmploymentTypes.FullTime,
            StartDate = new DateOnly(2026, 1, 1), EndDate = today.AddDays(10), BasicSalary = 10000m,
            Status = ContractStatuses.Active, CreatedAtUtc = DateTime.UtcNow
        });
        ctx.EmployeeDocuments.Add(new EmployeeDocument
        {
            TenantId = tenantId, EmployeeId = e1.Id, DocumentType = EmployeeDocumentTypes.Contract,
            FileUrl = "/x", FileName = "x.pdf", ContentType = "application/pdf", ExpiryDate = today.AddDays(10), CreatedAtUtc = DateTime.UtcNow
        });
        ctx.EmployeeDocuments.Add(new EmployeeDocument
        {
            TenantId = tenantId, EmployeeId = e1.Id, DocumentType = EmployeeDocumentTypes.NationalId,
            FileUrl = "/y", FileName = "y.pdf", ContentType = "application/pdf", ExpiryDate = today.AddDays(-2), CreatedAtUtc = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var result = await svc.GetAsync(tenantId, includePayroll: false);

        Assert.Equal(1, result.Data!.PendingLeaveRequests);
        Assert.Equal(1, result.Data.UpcomingContractExpirations);
        Assert.Equal(1, result.Data.ExpiringDocuments);
        Assert.Equal(1, result.Data.ExpiredDocuments);
    }

    [Fact]
    public async Task GetAsync_PayrollOmittedUnlessIncludePayrollTrue()
    {
        var (ctx, svc, tenantId) = await SeedAsync();
        var today = MembershipOperational.TodayCairo();
        var e1 = NewEmployee(tenantId, "EMP-0001");
        ctx.Employees.Add(e1);
        var period = new PayrollPeriod { TenantId = tenantId, Year = today.Year, Month = today.Month, Status = PayrollPeriodStatuses.Calculated, CreatedAtUtc = DateTime.UtcNow };
        ctx.PayrollPeriods.Add(period);
        await ctx.SaveChangesAsync();
        ctx.PayrollLines.Add(new PayrollLine { TenantId = tenantId, PayrollPeriodId = period.Id, EmployeeId = e1.Id, BasicSalary = 10000m, NetSalary = 10000m, CreatedAtUtc = DateTime.UtcNow });
        await ctx.SaveChangesAsync();

        var withoutPayroll = await svc.GetAsync(tenantId, includePayroll: false);
        var withPayroll = await svc.GetAsync(tenantId, includePayroll: true);

        Assert.Null(withoutPayroll.Data!.PayrollNetThisMonth);
        Assert.Equal(10000m, withPayroll.Data!.PayrollNetThisMonth);
        Assert.Equal(PayrollPeriodStatuses.Calculated, withPayroll.Data.PayrollStatusThisMonth);
    }

    [Fact]
    public async Task Dashboard_IsTenantIsolated()
    {
        var (ctxA, svcA, tenantA) = await SeedAsync();
        var (ctxB, _, tenantB) = await SeedAsync();
        ctxA.Employees.Add(NewEmployee(tenantA, "EMP-0001"));
        ctxB.Employees.Add(NewEmployee(tenantB, "EMP-0001"));
        ctxB.Employees.Add(NewEmployee(tenantB, "EMP-0002"));
        await ctxA.SaveChangesAsync();
        await ctxB.SaveChangesAsync();

        var result = await svcA.GetAsync(tenantA, includePayroll: false);

        Assert.Equal(1, result.Data!.EmployeeCount); // not 3 — tenant B's employees are invisible
    }
}
