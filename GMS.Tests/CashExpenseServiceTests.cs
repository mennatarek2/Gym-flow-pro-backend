namespace GMS.Tests;

using Microsoft.EntityFrameworkCore;
using GMS.Application.DTOs.Expenses;
using GMS.Application.Services;
using GMS.Core.Constants;
using GMS.Core.Entities;
using GMS.Core.Interfaces;
using GMS.Infrastructure.Persistence;

public sealed class CashExpenseServiceTests
{
    [Fact]
    public async Task CreateAndList_IsTenantScoped_AndRoundsAmount()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        await using var db = CreateContext(tenantA);
        var service = new CashExpenseService(db);

        var created = await service.CreateAsync(
            tenantA,
            Guid.NewGuid(),
            new CreateCashExpenseRequest
            {
                ExpenseDate = new DateOnly(2026, 8, 28),
                Category = "Utilities",
                Description = "Electricity",
                Amount = 12.345m,
                PaymentMethod = "bank_transfer"
            });
        await service.CreateAsync(
            tenantB,
            Guid.NewGuid(),
            new CreateCashExpenseRequest
            {
                ExpenseDate = new DateOnly(2026, 8, 28),
                Category = "Rent & Property",
                Description = "Rent",
                Amount = 100m,
                PaymentMethod = "bank_transfer"
            });

        Assert.True(created.IsSuccess, created.Error);
        Assert.Equal(12.35m, created.Data!.Amount);
        var listed = await service.ListAsync(
            tenantA,
            new DateOnly(2026, 8, 1),
            new DateOnly(2026, 8, 31));

        Assert.True(listed.IsSuccess, listed.Error);
        var expense = Assert.Single(listed.Data!);
        Assert.Equal("Utilities", expense.Category);
        Assert.Equal("Electricity", expense.Description);
    }

    [Fact]
    public async Task Update_CanVoidExpense_WithoutDeletingAuditRow()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateContext(tenantId);
        var service = new CashExpenseService(db);
        var created = await service.CreateAsync(
            tenantId,
            Guid.NewGuid(),
            new CreateCashExpenseRequest
            {
                ExpenseDate = new DateOnly(2026, 8, 28),
                Category = "Operations",
                Description = "Supplies",
                Amount = 50m,
                PaymentMethod = "bank_transfer"
            });

        var updated = await service.UpdateAsync(
            tenantId,
            created.Data!.Id,
            new UpdateCashExpenseRequest { Status = "void" });

        Assert.True(updated.IsSuccess, updated.Error);
        Assert.Equal("void", updated.Data!.Status);
        Assert.NotNull(await db.CashExpenses.SingleOrDefaultAsync(e => e.Id == created.Data.Id));
    }

    [Fact]
    public async Task PostedUtilityExpense_IncreasesOperatingExpenses()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateContext(tenantId);
        var expenseService = new CashExpenseService(db);
        var created = await expenseService.CreateAsync(
            tenantId,
            Guid.NewGuid(),
            new CreateCashExpenseRequest
            {
                ExpenseDate = new DateOnly(2026, 9, 1),
                Category = "Utilities",
                Description = "Electricity",
                Amount = 3000m,
                PaymentMethod = "bank_transfer"
            });
        Assert.True(created.IsSuccess, created.Error);

        var profit = await new ProfitabilityService(db).GetAsync(
            tenantId,
            new DateOnly(2026, 9, 1),
            new DateOnly(2026, 9, 1));

        Assert.True(profit.IsSuccess, profit.Error);
        Assert.Equal(3000m, profit.Data!.OperatingExpenses);
    }

    [Fact]
    public async Task VoidedExpense_DoesNotAffectOperatingExpenses()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateContext(tenantId);
        var expenseService = new CashExpenseService(db);
        var created = await expenseService.CreateAsync(
            tenantId,
            Guid.NewGuid(),
            new CreateCashExpenseRequest
            {
                ExpenseDate = new DateOnly(2026, 9, 1),
                Category = "Utilities",
                Description = "Water",
                Amount = 500m,
                PaymentMethod = "bank_transfer"
            });
        Assert.True(created.IsSuccess, created.Error);
        var voided = await expenseService.UpdateAsync(
            tenantId,
            created.Data!.Id,
            new UpdateCashExpenseRequest { Status = "void" });
        Assert.True(voided.IsSuccess, voided.Error);

        var profit = await new ProfitabilityService(db).GetAsync(
            tenantId,
            new DateOnly(2026, 9, 1),
            new DateOnly(2026, 9, 1));

        Assert.True(profit.IsSuccess, profit.Error);
        Assert.Equal(0m, profit.Data!.OperatingExpenses);
    }

    [Fact]
    public async Task Create_RejectsUnknownCategory()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateContext(tenantId);
        var service = new CashExpenseService(db);
        var result = await service.CreateAsync(
            tenantId,
            Guid.NewGuid(),
            new CreateCashExpenseRequest
            {
                ExpenseDate = new DateOnly(2026, 9, 1),
                Category = "payroll",
                Description = "Salaries",
                Amount = 100m,
                PaymentMethod = "bank_transfer"
            });

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task Create_RejectsCashWithoutOpenShift()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateContext(tenantId);
        var service = new CashExpenseService(db);
        var result = await service.CreateAsync(
            tenantId,
            Guid.NewGuid(),
            new CreateCashExpenseRequest
            {
                ExpenseDate = new DateOnly(2026, 9, 1),
                Category = "Marketing",
                Description = "Social media",
                Amount = 3000m,
                PaymentMethod = "cash"
            });

        Assert.False(result.IsSuccess);
        Assert.Contains("open shift", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Create_DefaultsSourceType_ForManualRunningCost()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateContext(tenantId);
        var service = new CashExpenseService(db);
        var created = await service.CreateAsync(
            tenantId,
            Guid.NewGuid(),
            new CreateCashExpenseRequest
            {
                ExpenseDate = new DateOnly(2026, 9, 1),
                Category = "Software & Technology",
                Description = "Gym management software",
                Amount = 4000m,
                PaymentMethod = "bank_transfer"
            });

        Assert.True(created.IsSuccess, created.Error);
        var row = await db.CashExpenses.SingleAsync(e => e.Id == created.Data!.Id);
        Assert.Equal(CashExpenseCatalog.ManualRunningCostSourceType, row.SourceType);
    }

    [Fact]
    public async Task Payroll_Payment_SourceType_Is_Excluded_From_OperatingExpenses()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateContext(tenantId);
        db.CashExpenses.Add(new CashExpense
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ExpenseDate = new DateOnly(2026, 9, 1),
            Category = "Operations",
            Description = "Payroll disbursement",
            Amount = 9000m,
            PaymentMethod = "bank_transfer",
            Status = "posted",
            SourceType = "payroll_payment",
            CreatedAtUtc = DateTime.UtcNow,
            RecordedByUserId = Guid.NewGuid()
        });
        db.CashExpenses.Add(new CashExpense
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ExpenseDate = new DateOnly(2026, 9, 1),
            Category = "Utilities",
            Description = "Electricity",
            Amount = 500m,
            PaymentMethod = "bank_transfer",
            Status = "posted",
            SourceType = CashExpenseCatalog.ManualRunningCostSourceType,
            CreatedAtUtc = DateTime.UtcNow,
            RecordedByUserId = Guid.NewGuid()
        });
        await db.SaveChangesAsync();

        var profit = await new ProfitabilityService(db).GetAsync(
            tenantId,
            new DateOnly(2026, 9, 1),
            new DateOnly(2026, 9, 1));

        Assert.True(profit.IsSuccess, profit.Error);
        Assert.Equal(500m, profit.Data!.OperatingExpenses);
    }

    private static GymFlowProDbContext CreateContext(Guid tenantId) =>
        new(new DbContextOptionsBuilder<GymFlowProDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options,
            new TestTenantContext(tenantId));

    private sealed class TestTenantContext : ITenantContext
    {
        public Guid TenantId { get; }
        public string? TenantName => "Test";
        public string? TimeZone => "Africa/Cairo";
        public bool IsInitialized => true;

        public TestTenantContext(Guid tenantId) => TenantId = tenantId;
        public void SetTenant(Guid tenantId, string tenantName, string timeZone) { }
        public void Clear() { }
    }
}
