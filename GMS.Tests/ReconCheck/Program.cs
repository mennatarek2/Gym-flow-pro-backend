// ReconCheck — a standalone CLI smoke check running the same money-path reconciliation invariants
// as GMS.Tests/Reconciliation/ReconciliationInvariantTests.cs, but against a real (e.g. staging or
// production) database for a specific Cairo business day, with no test framework involved.
//
// Usage:
//   dotnet run --project GMS.Tests/ReconCheck -- --connectionString="Server=...;Database=...;..." --date=2026-01-15
//   dotnet run --project GMS.Tests/ReconCheck -- --connectionString="..." --date=2026-01-15 --tenantId=<guid>
//
// Exit code 0 = every invariant held for every tenant checked; 1 = at least one violation (or a
// usage/connection error) — suitable for wiring into a CI job or a daily ops cron alert.

using Microsoft.EntityFrameworkCore;
using GMS.Infrastructure.Persistence;

var options = ParseArgs(args);
if (options == null)
{
    Console.WriteLine("Usage: ReconCheck --connectionString=\"...\" --date=yyyy-MM-dd [--tenantId=<guid>]");
    return 1;
}

var cairoTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time");
var (utcStart, utcEnd) = CairoBusinessDayUtcRange(options.Value.Date, cairoTimeZone);

var dbOptions = new DbContextOptionsBuilder<GymFlowProDbContext>()
    .UseSqlServer(options.Value.ConnectionString)
    .Options;

await using var ctx = new GymFlowProDbContext(dbOptions, null);

var tenantsQuery = ctx.Tenants.IgnoreQueryFilters().Where(t => t.IsActive);
if (options.Value.TenantId.HasValue)
    tenantsQuery = tenantsQuery.Where(t => t.Id == options.Value.TenantId.Value);

var tenants = await tenantsQuery.Select(t => new { t.Id, t.Name }).ToListAsync();

if (tenants.Count == 0)
{
    Console.WriteLine("No matching active tenant(s) found.");
    return 1;
}

Console.WriteLine($"ReconCheck — {options.Value.Date:yyyy-MM-dd} (Cairo business day: {utcStart:u} to {utcEnd:u} UTC)");
Console.WriteLine(new string('=', 70));

var anyFailure = false;

foreach (var tenant in tenants)
{
    Console.WriteLine($"\nTenant: {tenant.Name} ({tenant.Id})");
    var tenantOk = await CheckTenantAsync(ctx, tenant.Id, utcStart, utcEnd);
    anyFailure |= !tenantOk;
}

Console.WriteLine(new string('=', 70));
Console.WriteLine(anyFailure ? "RESULT: FAIL — one or more invariants were violated." : "RESULT: PASS — all invariants held.");

return anyFailure ? 1 : 0;

// ============================================================================

static async Task<bool> CheckTenantAsync(GymFlowProDbContext ctx, Guid tenantId, DateTime utcStart, DateTime utcEnd)
{
    var ok = true;

    var saleIds = await ctx.Sales.IgnoreQueryFilters()
        .Where(s => s.TenantId == tenantId && s.CreatedAtUtc >= utcStart && s.CreatedAtUtc < utcEnd)
        .Select(s => s.Id)
        .ToListAsync();

    // (a) Net revenue: payments minus non-credit refunds must equal invoices minus credit notes.
    var paymentsSum = await ctx.PaymentTransactions.IgnoreQueryFilters()
        .Where(p => p.SaleId != null && saleIds.Contains(p.SaleId.Value) && p.Status == "success")
        .SumAsync(p => (decimal?)p.Amount) ?? 0m;

    var nonCreditRefundsSum = await ctx.Refunds.IgnoreQueryFilters()
        .Where(r => r.TenantId == tenantId && r.Status == "executed" && r.Method != "credit"
                 && r.ExecutedAt != null && r.ExecutedAt >= utcStart && r.ExecutedAt < utcEnd)
        .SumAsync(r => (decimal?)r.Amount) ?? 0m;

    var invoiceTotalsSum = await ctx.Invoices.IgnoreQueryFilters()
        .Where(i => i.TenantId == tenantId && i.Type == "invoice" && i.IssuedAt >= utcStart && i.IssuedAt < utcEnd)
        .SumAsync(i => (decimal?)i.Total) ?? 0m;

    var creditNoteTotalsSum = await ctx.Invoices.IgnoreQueryFilters()
        .Where(i => i.TenantId == tenantId && i.Type == "credit_note" && i.IssuedAt >= utcStart && i.IssuedAt < utcEnd)
        .SumAsync(i => (decimal?)i.Total) ?? 0m;

    var lhsA = paymentsSum - nonCreditRefundsSum;
    var rhsA = invoiceTotalsSum - creditNoteTotalsSum;
    ok &= Report("(a) payments - non-credit refunds == invoices - credit notes", lhsA, rhsA);

    // (b) Per shift opened that day: cash movements must equal ExpectedCash - OpeningFloat.
    var shifts = await ctx.Shifts.IgnoreQueryFilters()
        .Include(s => s.Movements)
        .Where(s => s.TenantId == tenantId && s.OpenedAt >= utcStart && s.OpenedAt < utcEnd && s.ExpectedCash != null)
        .ToListAsync();

    foreach (var shift in shifts)
    {
        var movementsSum = shift.Movements.Sum(m => m.Amount);
        ok &= Report($"(b) shift {shift.Id} movements == ExpectedCash - OpeningFloat", movementsSum, shift.ExpectedCash!.Value - shift.OpeningFloat);
    }

    // (c) Member credit balances (cumulative, not day-scoped) must never go negative.
    var negativeBalances = await ctx.MemberCredits.IgnoreQueryFilters()
        .Where(c => c.TenantId == tenantId)
        .GroupBy(c => c.MemberId)
        .Select(g => new { MemberId = g.Key, Balance = g.Sum(c => c.Amount) })
        .Where(x => x.Balance < 0m)
        .ToListAsync();

    if (negativeBalances.Count > 0)
    {
        ok = false;
        foreach (var nb in negativeBalances)
            Console.WriteLine($"  [FAIL] (c) member {nb.MemberId} has a negative credit balance: {nb.Balance:F2}");
    }
    else
    {
        Console.WriteLine("  [PASS] (c) no negative member credit balances");
    }

    // (d) Per sale that day: AmountDue must equal Total minus successfully-received payments.
    var sales = await ctx.Sales.IgnoreQueryFilters()
        .Where(s => s.TenantId == tenantId && s.CreatedAtUtc >= utcStart && s.CreatedAtUtc < utcEnd)
        .ToListAsync();

    foreach (var sale in sales)
    {
        var paidForSale = await ctx.PaymentTransactions.IgnoreQueryFilters()
            .Where(p => p.SaleId == sale.Id && p.Status == "success")
            .SumAsync(p => (decimal?)p.Amount) ?? 0m;

        var expectedAmountDue = Math.Max(0m, sale.Total - paidForSale);
        if (sale.AmountDue != expectedAmountDue)
        {
            ok = false;
            Console.WriteLine($"  [FAIL] (d) sale {sale.Id} AmountDue={sale.AmountDue:F2} but Total-Paid={expectedAmountDue:F2}");
        }
    }

    if (sales.Count > 0 && sales.All(s => s.AmountDue == Math.Max(0m, s.Total - (paymentsSum))))
    {
        // (per-sale loop above already reports individual failures; this is just a summary line)
    }
    Console.WriteLine($"  [INFO] (d) checked {sales.Count} sale(s) for AmountDue consistency");

    return ok;
}

static bool Report(string label, decimal lhs, decimal rhs)
{
    var pass = lhs == rhs;
    Console.WriteLine(pass
        ? $"  [PASS] {label} ({lhs:F2} == {rhs:F2})"
        : $"  [FAIL] {label}: {lhs:F2} != {rhs:F2}");
    return pass;
}

static (DateTime UtcStart, DateTime UtcEnd) CairoBusinessDayUtcRange(DateOnly date, TimeZoneInfo cairoTimeZone)
{
    var cairoLocalStart = DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
    var cairoLocalEnd = DateTime.SpecifyKind(date.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);

    return (TimeZoneInfo.ConvertTimeToUtc(cairoLocalStart, cairoTimeZone), TimeZoneInfo.ConvertTimeToUtc(cairoLocalEnd, cairoTimeZone));
}

static (string ConnectionString, DateOnly Date, Guid? TenantId)? ParseArgs(string[] args)
{
    string? connectionString = null;
    DateOnly? date = null;
    Guid? tenantId = null;

    foreach (var arg in args)
    {
        if (arg.StartsWith("--connectionString=", StringComparison.OrdinalIgnoreCase))
            connectionString = arg["--connectionString=".Length..].Trim('"');
        else if (arg.StartsWith("--date=", StringComparison.OrdinalIgnoreCase))
        {
            if (DateOnly.TryParse(arg["--date=".Length..], out var parsedDate))
                date = parsedDate;
        }
        else if (arg.StartsWith("--tenantId=", StringComparison.OrdinalIgnoreCase))
        {
            if (Guid.TryParse(arg["--tenantId=".Length..], out var parsedTenantId))
                tenantId = parsedTenantId;
        }
    }

    if (string.IsNullOrWhiteSpace(connectionString) || date == null)
        return null;

    return (connectionString, date.Value, tenantId);
}
