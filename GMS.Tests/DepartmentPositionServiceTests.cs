namespace GMS.Tests;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using GMS.Application.DTOs.Hr;
using GMS.Application.Interfaces;
using GMS.Application.Services;
using GMS.Core.Entities;
using GMS.Infrastructure.Persistence;
using GMS.Infrastructure.Services;

public class DepartmentPositionServiceTests
{
    private sealed class NoOpAudit : IAuditService
    {
        public Task LogAsync(string action, string? entityType = null, Guid? entityId = null, object? before = null, object? after = null, Guid? tenantIdOverride = null)
            => Task.CompletedTask;

        public Task<GMS.Application.Common.Result<GMS.Application.Common.PagedResult<GMS.Application.DTOs.Audit.AuditEventDto>>> GetAuditEventsAsync(
            Guid tenantId, GMS.Application.DTOs.Audit.AuditEventQueryRequest query)
            => Task.FromResult(GMS.Application.Common.Result<GMS.Application.Common.PagedResult<GMS.Application.DTOs.Audit.AuditEventDto>>.Failure("n/a"));
    }

    private static async Task<(GymFlowProDbContext ctx, DepartmentService departments, PositionService positions, Guid tenantId)> SeedAsync()
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

        var departments = new DepartmentService(ctx, new NoOpAudit(), NullLogger<DepartmentService>.Instance);
        var positions = new PositionService(ctx, new NoOpAudit(), NullLogger<PositionService>.Instance);
        return (ctx, departments, positions, tenantId);
    }

    [Fact]
    public async Task CreateDepartment_RejectsDuplicateNameWithinTenant()
    {
        var (_, departments, _, tenantId) = await SeedAsync();

        var first = await departments.CreateAsync(tenantId, new CreateDepartmentRequest { Name = "Reception" });
        var duplicate = await departments.CreateAsync(tenantId, new CreateDepartmentRequest { Name = "Reception" });

        Assert.True(first.IsSuccess, first.Error);
        Assert.False(duplicate.IsSuccess);
    }

    [Fact]
    public async Task SameDepartmentName_AllowedAcrossDifferentTenants()
    {
        var (_, departmentsA, _, tenantA) = await SeedAsync();
        var (_, departmentsB, _, tenantB) = await SeedAsync();

        var a = await departmentsA.CreateAsync(tenantA, new CreateDepartmentRequest { Name = "Reception" });
        var b = await departmentsB.CreateAsync(tenantB, new CreateDepartmentRequest { Name = "Reception" });

        Assert.True(a.IsSuccess);
        Assert.True(b.IsSuccess);
    }

    [Fact]
    public async Task UpdateDepartment_CanDeactivateAndReactivate()
    {
        var (_, departments, _, tenantId) = await SeedAsync();
        var created = await departments.CreateAsync(tenantId, new CreateDepartmentRequest { Name = "Cleaning" });

        var deactivated = await departments.UpdateAsync(tenantId, created.Data!.Id, new UpdateDepartmentRequest { Name = "Cleaning", IsActive = false });
        Assert.True(deactivated.IsSuccess);
        Assert.False(deactivated.Data!.IsActive);

        var listActiveOnly = await departments.ListAsync(tenantId, includeInactive: false);
        Assert.DoesNotContain(listActiveOnly.Data!, d => d.Id == created.Data.Id);

        var listAll = await departments.ListAsync(tenantId, includeInactive: true);
        Assert.Contains(listAll.Data!, d => d.Id == created.Data.Id);
    }

    [Fact]
    public async Task CreatePosition_RejectsUnknownDepartment()
    {
        var (_, _, positions, tenantId) = await SeedAsync();

        var result = await positions.CreateAsync(tenantId, new CreatePositionRequest
        {
            Name = "Personal Trainer",
            DepartmentId = Guid.NewGuid()
        });

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task CreatePosition_LinksToDepartmentInSameTenant()
    {
        var (_, departments, positions, tenantId) = await SeedAsync();
        var department = await departments.CreateAsync(tenantId, new CreateDepartmentRequest { Name = "Training" });

        var position = await positions.CreateAsync(tenantId, new CreatePositionRequest
        {
            Name = "Personal Trainer",
            DepartmentId = department.Data!.Id
        });

        Assert.True(position.IsSuccess, position.Error);
        Assert.Equal("Training", position.Data!.DepartmentName);
    }
}
