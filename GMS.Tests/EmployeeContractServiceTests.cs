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

public class EmployeeContractServiceTests
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

    private static async Task<(EmployeeService svc, Guid tenantId, Guid employeeId)> SeedAsync()
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

        var svc = new EmployeeService(ctx, new NoOpAudit(), new NoOpFileStorage(), NullLogger<EmployeeService>.Instance);
        var employee = await svc.CreateAsync(tenantId, new CreateEmployeeRequest
        {
            FirstName = "Ahmed",
            LastName = "Mohamed",
            HireDate = new DateOnly(2026, 1, 1)
        });

        return (svc, tenantId, employee.Data!.Id);
    }

    [Fact]
    public async Task AddContractAsync_MultipleHistoricalContractsPreserveSalaryHistory()
    {
        var (svc, tenantId, employeeId) = await SeedAsync();

        var firstContract = await svc.AddContractAsync(tenantId, employeeId, new CreateEmployeeContractRequest
        {
            EmploymentType = EmploymentTypes.FullTime,
            StartDate = new DateOnly(2026, 1, 1),
            EndDate = new DateOnly(2026, 6, 30),
            BasicSalary = 12000m,
            Status = ContractStatuses.Ended
        });
        var secondContract = await svc.AddContractAsync(tenantId, employeeId, new CreateEmployeeContractRequest
        {
            EmploymentType = EmploymentTypes.FullTime,
            StartDate = new DateOnly(2026, 7, 1),
            BasicSalary = 14000m
        });

        Assert.True(firstContract.IsSuccess, firstContract.Error);
        Assert.True(secondContract.IsSuccess, secondContract.Error);

        var all = await svc.ListContractsAsync(tenantId, employeeId);
        Assert.Equal(2, all.Data!.Count);
        var reloadedFirst = all.Data.Single(c => c.Id == firstContract.Data!.Id);
        var reloadedSecond = all.Data.Single(c => c.Id == secondContract.Data!.Id);
        Assert.Equal(12000m, reloadedFirst.BasicSalary);
        Assert.Equal(14000m, reloadedSecond.BasicSalary);
        Assert.NotEqual(firstContract.Data!.ContractNumber, secondContract.Data!.ContractNumber);
    }

    [Fact]
    public async Task AddContractAsync_RejectsEndDateNotAfterStartDate()
    {
        var (svc, tenantId, employeeId) = await SeedAsync();

        var result = await svc.AddContractAsync(tenantId, employeeId, new CreateEmployeeContractRequest
        {
            EmploymentType = EmploymentTypes.FullTime,
            StartDate = new DateOnly(2026, 1, 1),
            EndDate = new DateOnly(2026, 1, 1),
            BasicSalary = 10000m
        });

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task AddContractAsync_RejectsOverlappingDateRange()
    {
        var (svc, tenantId, employeeId) = await SeedAsync();
        await svc.AddContractAsync(tenantId, employeeId, new CreateEmployeeContractRequest
        {
            EmploymentType = EmploymentTypes.FullTime,
            StartDate = new DateOnly(2026, 1, 1),
            EndDate = new DateOnly(2026, 12, 31),
            BasicSalary = 12000m
        });

        var overlapping = await svc.AddContractAsync(tenantId, employeeId, new CreateEmployeeContractRequest
        {
            EmploymentType = EmploymentTypes.FullTime,
            StartDate = new DateOnly(2026, 6, 1),
            BasicSalary = 14000m
        });

        Assert.False(overlapping.IsSuccess);
    }

    [Fact]
    public async Task AddContractAsync_AllowsAdjacentNonOverlappingDateRange()
    {
        var (svc, tenantId, employeeId) = await SeedAsync();
        await svc.AddContractAsync(tenantId, employeeId, new CreateEmployeeContractRequest
        {
            EmploymentType = EmploymentTypes.FullTime,
            StartDate = new DateOnly(2026, 1, 1),
            EndDate = new DateOnly(2026, 6, 30),
            BasicSalary = 12000m,
            Status = ContractStatuses.Ended
        });

        var adjacent = await svc.AddContractAsync(tenantId, employeeId, new CreateEmployeeContractRequest
        {
            EmploymentType = EmploymentTypes.FullTime,
            StartDate = new DateOnly(2026, 7, 1),
            BasicSalary = 14000m
        });

        Assert.True(adjacent.IsSuccess, adjacent.Error);
    }

    [Fact]
    public async Task ListContractsAsync_MarksOnlyActiveCurrentDatedContractAsCurrent()
    {
        var (svc, tenantId, employeeId) = await SeedAsync();
        await svc.AddContractAsync(tenantId, employeeId, new CreateEmployeeContractRequest
        {
            EmploymentType = EmploymentTypes.FullTime,
            StartDate = new DateOnly(2020, 1, 1),
            EndDate = new DateOnly(2020, 12, 31),
            BasicSalary = 9000m,
            Status = ContractStatuses.Ended
        });
        var current = await svc.AddContractAsync(tenantId, employeeId, new CreateEmployeeContractRequest
        {
            EmploymentType = EmploymentTypes.FullTime,
            StartDate = new DateOnly(2021, 1, 1),
            BasicSalary = 15000m
        });

        var all = await svc.ListContractsAsync(tenantId, employeeId);
        var currentDto = all.Data!.Single(c => c.Id == current.Data!.Id);
        var pastDto = all.Data!.First(c => c.Id != current.Data!.Id);

        Assert.True(currentDto.IsCurrent);
        Assert.False(pastDto.IsCurrent);
    }

    [Fact]
    public async Task AddContractAsync_RejectsInvalidEmploymentType()
    {
        var (svc, tenantId, employeeId) = await SeedAsync();

        var result = await svc.AddContractAsync(tenantId, employeeId, new CreateEmployeeContractRequest
        {
            EmploymentType = "NotARealType",
            StartDate = new DateOnly(2026, 1, 1),
            BasicSalary = 10000m
        });

        Assert.False(result.IsSuccess);
    }
}
