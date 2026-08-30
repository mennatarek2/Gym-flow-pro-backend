namespace GMS.Tests;

using Microsoft.EntityFrameworkCore;
using GMS.Application.DTOs.Expenses;
using GMS.Application.Services;
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
                Amount = 12.345m
            });
        await service.CreateAsync(
            tenantB,
            Guid.NewGuid(),
            new CreateCashExpenseRequest
            {
                ExpenseDate = new DateOnly(2026, 8, 28),
                Category = "Rent",
                Amount = 100m
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
                Category = "Maintenance",
                Amount = 50m
            });

        var updated = await service.UpdateAsync(
            tenantId,
            created.Data!.Id,
            new UpdateCashExpenseRequest { Status = "void" });

        Assert.True(updated.IsSuccess, updated.Error);
        Assert.Equal("void", updated.Data!.Status);
        Assert.NotNull(await db.CashExpenses.SingleOrDefaultAsync(e => e.Id == created.Data.Id));
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
