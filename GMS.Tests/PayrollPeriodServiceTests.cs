namespace GMS.Tests;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using GMS.Application.DTOs.Hr;
using GMS.Application.Interfaces;
using GMS.Application.Services;
using GMS.Core.Constants;
using GMS.Core.Entities;
using GMS.Core.Interfaces;
using GMS.Infrastructure.Persistence;
using GMS.Infrastructure.Services;

public class PayrollPeriodServiceTests
{
    private sealed class NoOpAudit : IAuditService
    {
        public Task LogAsync(string action, string? entityType = null, Guid? entityId = null, object? before = null, object? after = null, Guid? tenantIdOverride = null)
            => Task.CompletedTask;

        public Task<GMS.Application.Common.Result<GMS.Application.Common.PagedResult<GMS.Application.DTOs.Audit.AuditEventDto>>> GetAuditEventsAsync(
            Guid tenantId, GMS.Application.DTOs.Audit.AuditEventQueryRequest query)
            => Task.FromResult(GMS.Application.Common.Result<GMS.Application.Common.PagedResult<GMS.Application.DTOs.Audit.AuditEventDto>>.Failure("n/a"));
    }

    private sealed class NoOpFileStorage : IFileStorageService
    {
        public Task<string> UploadAsync(Stream stream, string fileName, string folder) => Task.FromResult($"/uploads/{folder}/{fileName}");
        public Task DeleteAsync(string fileUrl) => Task.CompletedTask;
        public Task<bool> ExistsAsync(string fileUrl) => Task.FromResult(true);
    }

    private static async Task<(GymFlowProDbContext ctx, PayrollPeriodService svc, PayrollAdjustmentService adjustments, EmployeeService employees, Guid tenantId, Guid employeeId)> SeedAsync(decimal basicSalary = 12000m)
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

        var employeeSvc = new EmployeeService(ctx, new NoOpAudit(), new NoOpFileStorage(), NullLogger<EmployeeService>.Instance);
        var employeeResult = await employeeSvc.CreateAsync(tenantId, new GMS.Application.DTOs.Hr.CreateEmployeeRequest
        {
            FirstName = "Ahmed", LastName = "Mohamed", HireDate = new DateOnly(2026, 1, 1)
        });
        var employeeId = employeeResult.Data!.Id;
        await employeeSvc.AddContractAsync(tenantId, employeeId, new GMS.Application.DTOs.Hr.CreateEmployeeContractRequest
        {
            EmploymentType = EmploymentTypes.FullTime,
            StartDate = new DateOnly(2026, 1, 1),
            BasicSalary = basicSalary
        });

        var payrollSvc = new PayrollPeriodService(ctx, employeeSvc, new NoOpAudit(), NullLogger<PayrollPeriodService>.Instance);
        var adjustmentSvc = new PayrollAdjustmentService(ctx, new NoOpAudit(), NullLogger<PayrollAdjustmentService>.Instance);
        return (ctx, payrollSvc, adjustmentSvc, employeeSvc, tenantId, employeeId);
    }

    [Fact]
    public async Task CreateAsync_RejectsDuplicatePeriod()
    {
        var (_, svc, _, _, tenantId, _) = await SeedAsync();
        var first = await svc.CreateAsync(tenantId, new CreatePayrollPeriodRequest { Year = 2026, Month = 9 });
        Assert.True(first.IsSuccess, first.Error);

        var duplicate = await svc.CreateAsync(tenantId, new CreatePayrollPeriodRequest { Year = 2026, Month = 9 });

        Assert.False(duplicate.IsSuccess);
    }

    [Fact]
    public async Task CalculateAsync_UsesCurrentContractBasicSalary()
    {
        var (_, svc, _, _, tenantId, employeeId) = await SeedAsync(basicSalary: 12000m);
        var period = await svc.CreateAsync(tenantId, new CreatePayrollPeriodRequest { Year = 2026, Month = 9 });

        var calculated = await svc.CalculateAsync(tenantId, period.Data!.Id);

        Assert.True(calculated.IsSuccess, calculated.Error);
        Assert.Equal(PayrollPeriodStatuses.Calculated, calculated.Data!.Status);
        var lines = await svc.ListLinesAsync(tenantId, period.Data.Id);
        var line = lines.Data!.Single(l => l.EmployeeId == employeeId);
        Assert.Equal(12000m, line.BasicSalary);
        Assert.Equal(12000m, line.NetSalary); // no overtime/adjustments yet
    }

    [Fact]
    public async Task CalculateAsync_UsesContractCoveringThePeriod_NotContractCurrentAsOfToday()
    {
        var (ctx, svc, _, employeeSvc, tenantId, employeeId) = await SeedAsync();

        // Move the contract so it starts mid-way through a payroll period a few months out —
        // reproduces a live bug where an employee's contract (e.g. starting the 22nd of the payroll
        // month) was invisible to Calculate because it looked up "current as of real today" instead
        // of "current during the period being calculated".
        var contract = await ctx.EmployeeContracts.SingleAsync(c => c.EmployeeId == employeeId);
        var periodMonthStart = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(3));
        periodMonthStart = new DateOnly(periodMonthStart.Year, periodMonthStart.Month, 1);
        contract.StartDate = periodMonthStart.AddDays(21);
        contract.BasicSalary = 20000m;
        await ctx.SaveChangesAsync();

        var todayLookup = await employeeSvc.GetCurrentContractAsync(tenantId, employeeId);
        Assert.True(todayLookup.IsSuccess);
        Assert.Null(todayLookup.Data);

        var period = await svc.CreateAsync(tenantId, new CreatePayrollPeriodRequest { Year = periodMonthStart.Year, Month = periodMonthStart.Month });
        var calculated = await svc.CalculateAsync(tenantId, period.Data!.Id);

        Assert.True(calculated.IsSuccess, calculated.Error);
        var lines = await svc.ListLinesAsync(tenantId, period.Data.Id);
        var line = lines.Data!.Single(l => l.EmployeeId == employeeId);
        Assert.Equal(20000m, line.BasicSalary);
    }

    [Fact]
    public async Task CalculateAsync_AppliesBonusAllowanceDeductionAndManualOvertime()
    {
        var (_, svc, adjustments, _, tenantId, employeeId) = await SeedAsync(basicSalary: 12000m);
        var period = await svc.CreateAsync(tenantId, new CreatePayrollPeriodRequest { Year = 2026, Month = 9 });

        await adjustments.CreateAsync(tenantId, period.Data!.Id, new CreatePayrollAdjustmentRequest { EmployeeId = employeeId, Type = PayrollAdjustmentTypes.Bonus, Amount = 500m }, null);
        await adjustments.CreateAsync(tenantId, period.Data.Id, new CreatePayrollAdjustmentRequest { EmployeeId = employeeId, Type = PayrollAdjustmentTypes.Allowance, Amount = 300m }, null);
        await adjustments.CreateAsync(tenantId, period.Data.Id, new CreatePayrollAdjustmentRequest { EmployeeId = employeeId, Type = PayrollAdjustmentTypes.Deduction, Amount = 200m }, null);
        await adjustments.CreateAsync(tenantId, period.Data.Id, new CreatePayrollAdjustmentRequest { EmployeeId = employeeId, Type = PayrollAdjustmentTypes.Overtime, Amount = 100m }, null);

        var calculated = await svc.CalculateAsync(tenantId, period.Data.Id);
        Assert.True(calculated.IsSuccess, calculated.Error);

        var lines = await svc.ListLinesAsync(tenantId, period.Data.Id);
        var line = lines.Data!.Single(l => l.EmployeeId == employeeId);
        Assert.Equal(12000m, line.BasicSalary);
        Assert.Equal(500m, line.BonusAmount);
        Assert.Equal(300m, line.AllowanceAmount);
        Assert.Equal(200m, line.DeductionAmount);
        Assert.Equal(100m, line.OvertimeAmount); // no attendance overtime this period, so purely the manual adjustment
        Assert.Equal(12000m + 100m + 500m + 300m - 200m, line.NetSalary);
    }

    [Fact]
    public async Task CalculateAsync_IncludesAttendanceOvertimeMinutesConvertedToMoney()
    {
        var (ctx, svc, _, _, tenantId, employeeId) = await SeedAsync(basicSalary: 24000m); // hourly rate = 100 (24000/240)
        ctx.EmployeeAttendances.Add(new EmployeeAttendance
        {
            TenantId = tenantId,
            EmployeeId = employeeId,
            AttendanceDate = new DateOnly(2026, 9, 15),
            CheckInAtUtc = DateTime.UtcNow,
            CheckOutAtUtc = DateTime.UtcNow,
            OvertimeMinutes = 60, // 1 hour => 100 EGP at this salary
            Status = AttendanceStatuses.Present,
            Source = AttendanceSources.Manual,
            CreatedAtUtc = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();
        var period = await svc.CreateAsync(tenantId, new CreatePayrollPeriodRequest { Year = 2026, Month = 9 });

        await svc.CalculateAsync(tenantId, period.Data!.Id);

        var lines = await svc.ListLinesAsync(tenantId, period.Data.Id);
        var line = lines.Data!.Single(l => l.EmployeeId == employeeId);
        Assert.Equal(100m, line.OvertimeAmount);
    }

    [Fact]
    public async Task Lifecycle_DraftCalculateApproveClose_WorksInOrder()
    {
        var (_, svc, _, _, tenantId, _) = await SeedAsync();
        var period = await svc.CreateAsync(tenantId, new CreatePayrollPeriodRequest { Year = 2026, Month = 9 });
        Assert.Equal(PayrollPeriodStatuses.Draft, period.Data!.Status);

        var calculated = await svc.CalculateAsync(tenantId, period.Data.Id);
        Assert.Equal(PayrollPeriodStatuses.Calculated, calculated.Data!.Status);

        var approved = await svc.ApproveAsync(tenantId, period.Data.Id, Guid.NewGuid());
        Assert.True(approved.IsSuccess, approved.Error);
        Assert.Equal(PayrollPeriodStatuses.Approved, approved.Data!.Status);

        var closed = await svc.CloseAsync(tenantId, period.Data.Id, Guid.NewGuid());
        Assert.True(closed.IsSuccess, closed.Error);
        Assert.Equal(PayrollPeriodStatuses.Closed, closed.Data!.Status);
    }

    [Fact]
    public async Task ApproveAsync_RejectsDraftPeriod()
    {
        var (_, svc, _, _, tenantId, _) = await SeedAsync();
        var period = await svc.CreateAsync(tenantId, new CreatePayrollPeriodRequest { Year = 2026, Month = 9 });

        var approved = await svc.ApproveAsync(tenantId, period.Data!.Id, null);

        Assert.False(approved.IsSuccess);
    }

    [Fact]
    public async Task CalculateAsync_BlockedOnceApproved()
    {
        var (_, svc, _, _, tenantId, _) = await SeedAsync();
        var period = await svc.CreateAsync(tenantId, new CreatePayrollPeriodRequest { Year = 2026, Month = 9 });
        await svc.CalculateAsync(tenantId, period.Data!.Id);
        await svc.ApproveAsync(tenantId, period.Data.Id, null);

        var recalculated = await svc.CalculateAsync(tenantId, period.Data.Id);

        Assert.False(recalculated.IsSuccess);
    }

    [Fact]
    public async Task ClosedPayroll_CannotBeRecalculatedOrHaveNewAdjustments()
    {
        var (_, svc, adjustments, _, tenantId, employeeId) = await SeedAsync();
        var period = await svc.CreateAsync(tenantId, new CreatePayrollPeriodRequest { Year = 2026, Month = 9 });
        await svc.CalculateAsync(tenantId, period.Data!.Id);
        await svc.ApproveAsync(tenantId, period.Data.Id, null);
        await svc.CloseAsync(tenantId, period.Data.Id, null);

        var recalculated = await svc.CalculateAsync(tenantId, period.Data.Id);
        Assert.False(recalculated.IsSuccess);

        var newAdjustment = await adjustments.CreateAsync(tenantId, period.Data.Id,
            new CreatePayrollAdjustmentRequest { EmployeeId = employeeId, Type = PayrollAdjustmentTypes.Bonus, Amount = 100m }, null);
        Assert.False(newAdjustment.IsSuccess);
    }

    [Fact]
    public async Task HistoricalPayrollLine_UnaffectedByLaterContractChange()
    {
        var (_, svc, _, employees, tenantId, employeeId) = await SeedAsync(basicSalary: 12000m);
        var period = await svc.CreateAsync(tenantId, new CreatePayrollPeriodRequest { Year = 2026, Month = 9 });
        await svc.CalculateAsync(tenantId, period.Data!.Id);
        await svc.ApproveAsync(tenantId, period.Data.Id, null);
        await svc.CloseAsync(tenantId, period.Data.Id, null);

        // A raise takes effect after the period closed.
        await employees.AddContractAsync(tenantId, employeeId, new GMS.Application.DTOs.Hr.CreateEmployeeContractRequest
        {
            EmploymentType = EmploymentTypes.FullTime,
            StartDate = new DateOnly(2026, 10, 1),
            BasicSalary = 15000m
        });

        var lines = await svc.ListLinesAsync(tenantId, period.Data.Id);
        Assert.Equal(12000m, lines.Data!.Single(l => l.EmployeeId == employeeId).BasicSalary); // untouched
    }

    [Fact]
    public async Task PayrollPeriods_AreTenantIsolated()
    {
        var (_, svcA, _, _, tenantA, _) = await SeedAsync();
        var (_, svcB, _, _, tenantB, _) = await SeedAsync();

        await svcA.CreateAsync(tenantA, new CreatePayrollPeriodRequest { Year = 2026, Month = 9 });
        await svcB.CreateAsync(tenantB, new CreatePayrollPeriodRequest { Year = 2026, Month = 9 });

        var listA = await svcA.ListAsync(tenantA);
        Assert.Single(listA.Data!);
    }
}
