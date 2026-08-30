namespace GMS.Tests;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using GMS.Application.Options;
using GMS.Application.Services;
using GMS.Core.Constants;
using GMS.Core.Entities;
using GMS.Infrastructure.Persistence;
using GMS.Infrastructure.Services;

public class EmployeeAppActivationServiceTests
{
    private static (GymFlowProDbContext ctx, EmployeeAppActivationService svc, Guid tenantId) CreateSut()
    {
        var tenantId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<GymFlowProDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var tenantContext = new TenantContext();
        tenantContext.SetTenant(tenantId, "Test Tenant", "Africa/Cairo");
        var ctx = new GymFlowProDbContext(options, tenantContext);
        var audit = new AuditService(ctx, new Microsoft.AspNetCore.Http.HttpContextAccessor(), tenantContext, NullLogger<AuditService>.Instance);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["JwtSettings:SecretKey"] = "test-pepper-secret-key-at-least-32-chars!!"
        }).Build();
        var svc = new EmployeeAppActivationService(
            ctx,
            tenantContext,
            audit,
            Options.Create(new EmployeeAppActivationOptions { ExpirationHours = 24, CodePepper = "unit-test-pepper" }),
            config,
            NullLogger<EmployeeAppActivationService>.Instance);
        return (ctx, svc, tenantId);
    }

    private static Employee SeedEmployee(GymFlowProDbContext ctx, Guid tenantId, string status = EmployeeStatuses.Active)
    {
        ctx.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Gym",
            NameAr = "صالة",
            GymCode = "GYM-EMP-01",
            City = "Cairo",
            Address = "Addr",
            PhoneNumber = "0100000000",
            Email = $"{tenantId}@t.local",
            SubscriptionStartDate = DateTime.UtcNow,
            IsActive = true
        });
        var e = new Employee
        {
            TenantId = tenantId,
            EmployeeNumber = "EMP-0001",
            FirstName = "Ahmed",
            LastName = "Cleaner",
            Phone = "+201000000099",
            Email = "ahmed@test.local",
            Status = status,
            HireDate = new DateOnly(2024, 1, 1)
        };
        ctx.Employees.Add(e);
        ctx.SaveChanges();
        return e;
    }

    [Fact]
    public async Task Generate_ReturnsPlaintext_AndStoresOnlyHash()
    {
        var (ctx, svc, tenantId) = CreateSut();
        var employee = SeedEmployee(ctx, tenantId);

        var result = await svc.GenerateAsync(employee.Id, Guid.NewGuid());
        Assert.True(result.IsSuccess);
        Assert.False(string.IsNullOrWhiteSpace(result.Data!.ActivationCode));
        Assert.Contains('-', result.Data.ActivationCode);
        Assert.Equal("EMP-0001", result.Data.EmployeeNumber);
        Assert.True(result.Data.ExpiresInMinutes > 0);

        var row = Assert.Single(ctx.EmployeeAppActivationCodes);
        Assert.False(string.IsNullOrWhiteSpace(row.CodeHash));
        Assert.DoesNotContain(result.Data.ActivationCode.Replace("-", ""), row.CodeHash);
    }

    [Fact]
    public async Task Generate_RevokesPreviousUnusedCodes()
    {
        var (ctx, svc, tenantId) = CreateSut();
        var employee = SeedEmployee(ctx, tenantId);

        var first = await svc.GenerateAsync(employee.Id, null);
        var second = await svc.GenerateAsync(employee.Id, null);
        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);

        var rows = ctx.EmployeeAppActivationCodes.OrderBy(c => c.CreatedAtUtc).ToList();
        Assert.Equal(2, rows.Count);
        Assert.NotNull(rows[0].RevokedAtUtc);
        Assert.Null(rows[1].RevokedAtUtc);
    }

    [Fact]
    public async Task Generate_Fails_WhenNotActive()
    {
        var (ctx, svc, tenantId) = CreateSut();
        var employee = SeedEmployee(ctx, tenantId, EmployeeStatuses.Suspended);

        var result = await svc.GenerateAsync(employee.Id, null);
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task Consume_Succeeds_Once()
    {
        var (ctx, svc, tenantId) = CreateSut();
        var employee = SeedEmployee(ctx, tenantId);
        var gen = await svc.GenerateAsync(employee.Id, null);
        var code = gen.Data!.ActivationCode;

        var first = await svc.ConsumeAsync(tenantId, code);
        Assert.True(first.IsSuccess);
        Assert.Equal(employee.Id, first.Data!.Id);

        var second = await svc.ConsumeAsync(tenantId, code);
        Assert.False(second.IsSuccess);
        Assert.Contains("already been used", second.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Consume_Fails_InvalidCode()
    {
        var (ctx, svc, tenantId) = CreateSut();
        SeedEmployee(ctx, tenantId);

        var result = await svc.ConsumeAsync(tenantId, "AAAA-BBBB");
        Assert.False(result.IsSuccess);
        Assert.Equal("Invalid or expired activation code.", result.Error);
    }

    [Fact]
    public async Task Consume_Fails_ExpiredCode()
    {
        var (ctx, svc, tenantId) = CreateSut();
        var employee = SeedEmployee(ctx, tenantId);
        var gen = await svc.GenerateAsync(employee.Id, null);
        var row = Assert.Single(ctx.EmployeeAppActivationCodes);
        row.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
        await ctx.SaveChangesAsync();

        var result = await svc.ConsumeAsync(tenantId, gen.Data!.ActivationCode);
        Assert.False(result.IsSuccess);
        Assert.Equal("Invalid or expired activation code.", result.Error);
    }

    [Fact]
    public async Task Consume_Fails_WrongTenant()
    {
        var (ctx, svc, tenantId) = CreateSut();
        var employee = SeedEmployee(ctx, tenantId);
        var gen = await svc.GenerateAsync(employee.Id, null);

        var result = await svc.ConsumeAsync(Guid.NewGuid(), gen.Data!.ActivationCode);
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task Consume_Fails_WhenEmployeeSuspendedAfterCodeIssued()
    {
        var (ctx, svc, tenantId) = CreateSut();
        var employee = SeedEmployee(ctx, tenantId);
        var gen = await svc.GenerateAsync(employee.Id, null);
        employee.Status = EmployeeStatuses.Terminated;
        await ctx.SaveChangesAsync();

        var result = await svc.ConsumeAsync(tenantId, gen.Data!.ActivationCode);
        Assert.False(result.IsSuccess);
        Assert.Equal("Unable to activate this account.", result.Error);
    }
}
