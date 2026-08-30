namespace GMS.Tests;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using GMS.Application.DTOs.Hr;
using GMS.Application.Interfaces;
using GMS.Application.Services;
using GMS.Core.Constants;
using GMS.Core.Entities;
using GMS.Core.Utilities;
using GMS.Infrastructure.Persistence;
using GMS.Infrastructure.Services;

public class EmployeeAttendanceServiceTests
{
    private sealed class NoOpAudit : IAuditService
    {
        public Task LogAsync(string action, string? entityType = null, Guid? entityId = null, object? before = null, object? after = null, Guid? tenantIdOverride = null)
            => Task.CompletedTask;

        public Task<GMS.Application.Common.Result<GMS.Application.Common.PagedResult<GMS.Application.DTOs.Audit.AuditEventDto>>> GetAuditEventsAsync(
            Guid tenantId, GMS.Application.DTOs.Audit.AuditEventQueryRequest query)
            => Task.FromResult(GMS.Application.Common.Result<GMS.Application.Common.PagedResult<GMS.Application.DTOs.Audit.AuditEventDto>>.Failure("n/a"));
    }

    private static async Task<(GymFlowProDbContext ctx, EmployeeAttendanceService svc, Guid tenantId, Guid employeeId, Guid appUserId)> SeedAsync(bool withScheduleToday = false)
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

        var identityUserId = Guid.NewGuid();
        var appUser = new AppUser
        {
            TenantId = tenantId,
            UserId = identityUserId.ToString(),
            FirstName = "Ahmed",
            LastName = "Trainer",
            Email = "trainer@test.local",
            Role = "Trainer",
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        ctx.AppUsers.Add(appUser);

        var employee = new Employee
        {
            TenantId = tenantId,
            EmployeeNumber = "EMP-0001",
            FirstName = "Ahmed",
            LastName = "Mohamed",
            HireDate = new DateOnly(2026, 1, 1),
            AppUserId = appUser.Id,
            CreatedAtUtc = DateTime.UtcNow
        };
        ctx.Employees.Add(employee);

        if (withScheduleToday)
        {
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
            ctx.EmployeeScheduleAssignments.Add(new EmployeeScheduleAssignment
            {
                TenantId = tenantId,
                EmployeeId = employee.Id,
                EmployeeShiftId = shift.Id,
                Date = MembershipOperational.TodayCairo(),
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        await ctx.SaveChangesAsync();

        var svc = new EmployeeAttendanceService(ctx, new NoOpAudit(), NullLogger<EmployeeAttendanceService>.Instance);
        return (ctx, svc, tenantId, employee.Id, appUser.Id);
    }

    [Fact]
    public async Task CheckInAsync_CreatesAttendanceRowWithComputedStatus()
    {
        // Deliberately does not assert a fixed "Present"/"Late" outcome: the seeded shift is
        // 09:00-17:00 Cairo with a 10-minute grace, and CheckInAsync stamps the real DateTime.UtcNow,
        // so whether "now" is on-time or late depends on the wall-clock time the suite happens to run
        // at. An earlier version hardcoded "Present" and passed only when run before ~09:10 Cairo —
        // a real, discovered defect (not something Phase 4 introduced), now fixed by computing the
        // same expectation AttendanceCalculator itself would, independently of time of day.
        var (_, svc, tenantId, employeeId, appUserId) = await SeedAsync(withScheduleToday: true);

        var result = await svc.CheckInAsync(tenantId, employeeId, "on time", AttendanceSources.Manual, appUserId);

        Assert.True(result.IsSuccess, result.Error);
        Assert.NotNull(result.Data!.CheckInAtUtc);

        var (expectedLateMinutes, expectedStatus) = AttendanceCalculator.ComputeCheckIn(
            result.Data.CheckInAtUtc!.Value, MembershipOperational.TodayCairo(), new TimeOnly(9, 0), graceMinutes: 10);
        Assert.Equal(expectedStatus, result.Data.Status);
        Assert.Equal(expectedLateMinutes, result.Data.LateMinutes);
    }

    [Fact]
    public async Task CheckInAsync_RejectsDoubleCheckIn()
    {
        var (_, svc, tenantId, employeeId, appUserId) = await SeedAsync();
        await svc.CheckInAsync(tenantId, employeeId, null, AttendanceSources.Manual, appUserId);

        var second = await svc.CheckInAsync(tenantId, employeeId, null, AttendanceSources.Manual, appUserId);

        Assert.False(second.IsSuccess);
    }

    [Fact]
    public async Task CheckOutAsync_RejectsCheckoutWithoutCheckin()
    {
        var (_, svc, tenantId, employeeId, appUserId) = await SeedAsync();

        var result = await svc.CheckOutAsync(tenantId, employeeId, appUserId);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task CheckOutAsync_ComputesWorkedMinutes()
    {
        var (_, svc, tenantId, employeeId, appUserId) = await SeedAsync();
        await svc.CheckInAsync(tenantId, employeeId, null, AttendanceSources.Manual, appUserId);

        var result = await svc.CheckOutAsync(tenantId, employeeId, appUserId);

        Assert.True(result.IsSuccess, result.Error);
        Assert.NotNull(result.Data!.CheckOutAtUtc);
        Assert.True(result.Data.WorkedMinutes >= 0);
    }

    [Fact]
    public async Task CheckOutAsync_RejectsDoubleCheckOut()
    {
        var (_, svc, tenantId, employeeId, appUserId) = await SeedAsync();
        await svc.CheckInAsync(tenantId, employeeId, null, AttendanceSources.Manual, appUserId);
        await svc.CheckOutAsync(tenantId, employeeId, appUserId);

        var second = await svc.CheckOutAsync(tenantId, employeeId, appUserId);

        Assert.False(second.IsSuccess);
    }

    [Fact]
    public async Task CorrectAsync_UpdatesStatusAndRecomputesWorkedMinutes()
    {
        var (_, svc, tenantId, employeeId, appUserId) = await SeedAsync();
        var checkIn = await svc.CheckInAsync(tenantId, employeeId, null, AttendanceSources.Manual, appUserId);

        var checkInAt = checkIn.Data!.CheckInAtUtc!.Value;
        var corrected = await svc.CorrectAsync(tenantId, checkIn.Data.Id, new CorrectAttendanceRequest
        {
            Status = AttendanceStatuses.HalfDay,
            CheckOutAtUtc = checkInAt.AddHours(4)
        }, appUserId);

        Assert.True(corrected.IsSuccess, corrected.Error);
        Assert.Equal(AttendanceStatuses.HalfDay, corrected.Data!.Status);
        Assert.Equal(240, corrected.Data.WorkedMinutes);
    }

    [Fact]
    public async Task ResolveEmployeeIdForCallerAsync_ResolvesLinkedEmployee()
    {
        var (ctx, svc, tenantId, employeeId, appUserId) = await SeedAsync();
        var appUser = await ctx.AppUsers.FirstAsync(a => a.Id == appUserId);
        var identityUserId = Guid.Parse(appUser.UserId);

        var resolved = await svc.ResolveEmployeeIdForCallerAsync(tenantId, identityUserId);

        Assert.Equal(employeeId, resolved);
    }

    [Fact]
    public async Task ResolveEmployeeIdForCallerAsync_ReturnsNullForUnlinkedIdentity()
    {
        var (_, svc, tenantId, _, _) = await SeedAsync();

        var resolved = await svc.ResolveEmployeeIdForCallerAsync(tenantId, Guid.NewGuid());

        Assert.Null(resolved);
    }

    [Fact]
    public async Task Attendance_IsTenantIsolated()
    {
        var (_, svcA, tenantA, employeeA, appUserA) = await SeedAsync();
        var (_, svcB, tenantB, employeeB, appUserB) = await SeedAsync();

        await svcA.CheckInAsync(tenantA, employeeA, null, AttendanceSources.Manual, appUserA);
        await svcB.CheckInAsync(tenantB, employeeB, null, AttendanceSources.Manual, appUserB);

        var today = MembershipOperational.TodayCairo();
        var listA = await svcA.ListAsync(tenantA, today, today);
        Assert.Single(listA.Data!);
        Assert.Equal(employeeA, listA.Data![0].EmployeeId);
    }
}
