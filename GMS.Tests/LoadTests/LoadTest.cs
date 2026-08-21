// Load test for the two highest-traffic front-desk endpoints: sale creation and QR check-in.
// k6 is not installed in this environment, so NBomber (a .NET load-testing library) is used instead
// — it needs no external binary, just `dotnet run`, and can reuse GMS.Core/GMS.Infrastructure
// directly for the post-run duplicate-invoice-number assertion against the real database.
//
// Usage:
//   dotnet run --project GMS.Tests/LoadTests -- ^
//     --baseUrl=https://localhost:5001 ^
//     --staffToken="<JWT from POST /api/auth/login>" ^
//     --memberToken="<JWT from POST /api/auth/member-verify>" ^
//     --tenantId=<guid> --planId=<guid> --gymCode=GYM-CAIRO-01 ^
//     --connectionString="Server=(localdb)\mssqllocaldb;Database=GymFlowProDb;Trusted_Connection=true;Encrypt=false;"
//
// Bearer tokens are obtained out-of-band (via real login / member OTP verification against the
// target environment) and passed in, rather than this script re-implementing the auth flow —
// login/OTP delivery is environment-specific (real SMS in staging, seeded users in dev, etc).
//
// Scenarios:
//   create_sale : 20 VUs, cash sale against a sequentially-cycled pool of existing member IDs, 2 min.
//   qr_checkin  : 50 VUs, QR check-in for the same member pool, 2 min.
// Thresholds: p95 sale < 800ms, p95 checkin < 300ms, error rate < 1% (per scenario).
// After the run: queries Invoices for any duplicate InvoiceNumber per tenant (would indicate the
// invoice-numbering sequence broke under concurrent load).

using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using NBomber.Contracts.Stats;
using NBomber.CSharp;
using GMS.Infrastructure.Persistence;

var opts = ParseArgs(args);
if (opts == null)
{
    Console.WriteLine("Usage: LoadTest --baseUrl=... --staffToken=... --memberToken=... --tenantId=<guid> --planId=<guid> --gymCode=... [--connectionString=...]");
    return 1;
}

var memberIds = await LoadMemberIdsAsync(opts.Value);
if (memberIds.Count == 0)
{
    Console.WriteLine("No member IDs available to run against — pass --memberIds=<guid,guid,...> or --connectionString + --tenantId to auto-fetch some.");
    return 1;
}

using var httpClient = new HttpClient();
var saleCounter = 0;
var checkinCounter = 0;

var createSaleScenario = Scenario.Create("create_sale", async _ =>
{
    var index = System.Threading.Interlocked.Increment(ref saleCounter);
    var memberId = memberIds[index % memberIds.Count];

    var request = new HttpRequestMessage(HttpMethod.Post, $"{opts.Value.BaseUrl}/api/sales");
    request.Headers.Add("Authorization", $"Bearer {opts.Value.StaffToken}");
    request.Headers.Add("X-Idempotency-Key", Guid.NewGuid().ToString());
    request.Content = JsonContent.Create(new
    {
        memberId,
        planId = opts.Value.PlanId,
        payments = new[] { new { method = "cash", amount = 500m } }
    });

    using var response = await httpClient.SendAsync(request);
    return response.IsSuccessStatusCode ? Response.Ok(statusCode: ((int)response.StatusCode).ToString()) : Response.Fail(statusCode: ((int)response.StatusCode).ToString());
})
.WithLoadSimulations(Simulation.KeepConstant(copies: 20, during: TimeSpan.FromMinutes(2)));

var qrCheckinScenario = Scenario.Create("qr_checkin", async _ =>
{
    // Member identity for check-in comes from the bearer JWT itself, not the request body —
    // the counter is only kept so this scenario's throughput is independently observable.
    System.Threading.Interlocked.Increment(ref checkinCounter);

    var request = new HttpRequestMessage(HttpMethod.Post, $"{opts.Value.BaseUrl}/api/attendance/qr-checkin");
    request.Headers.Add("Authorization", $"Bearer {opts.Value.MemberToken}");
    request.Content = JsonContent.Create(new { gymCode = opts.Value.GymCode });

    using var response = await httpClient.SendAsync(request);
    return response.IsSuccessStatusCode ? Response.Ok(statusCode: ((int)response.StatusCode).ToString()) : Response.Fail(statusCode: ((int)response.StatusCode).ToString());
})
.WithLoadSimulations(Simulation.KeepConstant(copies: 50, during: TimeSpan.FromMinutes(2)));

var stats = NBomberRunner
    .RegisterScenarios(createSaleScenario, qrCheckinScenario)
    .Run();

var thresholdsOk = EvaluateThresholds(stats);
var duplicatesOk = await CheckNoDuplicateInvoiceNumbersAsync(opts.Value.ConnectionString, opts.Value.TenantId);

Console.WriteLine(new string('=', 70));
Console.WriteLine(thresholdsOk && duplicatesOk ? "RESULT: PASS" : "RESULT: FAIL");

return thresholdsOk && duplicatesOk ? 0 : 1;

// ============================================================================

static bool EvaluateThresholds(NodeStats stats)
{
    var ok = true;

    foreach (var scenario in stats.ScenarioStats)
    {
        var p95Ms = scenario.Ok.Latency.Percent95;
        var totalRequests = scenario.Ok.Request.Count + scenario.Fail.Request.Count;
        var errorRate = totalRequests == 0 ? 0 : (double)scenario.Fail.Request.Count / totalRequests;

        var p95Threshold = scenario.ScenarioName switch
        {
            "create_sale" => 800d,
            "qr_checkin" => 300d,
            _ => double.MaxValue
        };

        var p95Ok = p95Ms <= p95Threshold;
        var errorRateOk = errorRate < 0.01;

        Console.WriteLine($"[{scenario.ScenarioName}] p95={p95Ms:F0}ms (threshold {p95Threshold:F0}ms) — {(p95Ok ? "PASS" : "FAIL")}");
        Console.WriteLine($"[{scenario.ScenarioName}] error_rate={errorRate:P2} (threshold 1.00%) — {(errorRateOk ? "PASS" : "FAIL")}");

        ok &= p95Ok && errorRateOk;
    }

    return ok;
}

static async Task<bool> CheckNoDuplicateInvoiceNumbersAsync(string? connectionString, Guid? tenantId)
{
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        Console.WriteLine("[duplicate-invoice-check] skipped — no --connectionString supplied");
        return true;
    }

    var dbOptions = new DbContextOptionsBuilder<GymFlowProDbContext>().UseSqlServer(connectionString).Options;
    await using var ctx = new GymFlowProDbContext(dbOptions, null);

    var query = ctx.Invoices.IgnoreQueryFilters().AsQueryable();
    if (tenantId.HasValue)
        query = query.Where(i => i.TenantId == tenantId.Value);

    var duplicates = await query
        .GroupBy(i => new { i.TenantId, i.InvoiceNumber })
        .Where(g => g.Count() > 1)
        .Select(g => new { g.Key.TenantId, g.Key.InvoiceNumber, Count = g.Count() })
        .ToListAsync();

    if (duplicates.Count == 0)
    {
        Console.WriteLine("[duplicate-invoice-check] PASS — no duplicate invoice numbers");
        return true;
    }

    foreach (var d in duplicates)
        Console.WriteLine($"[duplicate-invoice-check] FAIL — tenant {d.TenantId}: {d.InvoiceNumber} appears {d.Count} times");

    return false;
}

static async Task<List<Guid>> LoadMemberIdsAsync(LoadTestOptions opts)
{
    if (opts.MemberIds.Count > 0)
        return opts.MemberIds;

    if (string.IsNullOrWhiteSpace(opts.ConnectionString) || opts.TenantId == null)
        return new List<Guid>();

    var dbOptions = new DbContextOptionsBuilder<GymFlowProDbContext>().UseSqlServer(opts.ConnectionString).Options;
    await using var ctx = new GymFlowProDbContext(dbOptions, null);

    return await ctx.GymMembers.IgnoreQueryFilters()
        .Where(m => m.TenantId == opts.TenantId.Value)
        .OrderBy(m => m.Id)
        .Select(m => m.Id)
        .Take(5000)
        .ToListAsync();
}

static LoadTestOptions? ParseArgs(string[] args)
{
    string? baseUrl = null, staffToken = null, memberToken = null, gymCode = null, connectionString = null;
    Guid? tenantId = null, planId = null;
    var memberIds = new List<Guid>();

    foreach (var arg in args)
    {
        if (TryGetValue(arg, "--baseUrl=", out var v)) baseUrl = v.TrimEnd('/');
        else if (TryGetValue(arg, "--staffToken=", out v)) staffToken = v;
        else if (TryGetValue(arg, "--memberToken=", out v)) memberToken = v;
        else if (TryGetValue(arg, "--gymCode=", out v)) gymCode = v;
        else if (TryGetValue(arg, "--connectionString=", out v)) connectionString = v.Trim('"');
        else if (TryGetValue(arg, "--tenantId=", out v) && Guid.TryParse(v, out var t)) tenantId = t;
        else if (TryGetValue(arg, "--planId=", out v) && Guid.TryParse(v, out var p)) planId = p;
        else if (TryGetValue(arg, "--memberIds=", out v))
            memberIds.AddRange(v.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => Guid.TryParse(s, out var id) ? id : (Guid?)null)
                .Where(id => id.HasValue)
                .Select(id => id!.Value));
    }

    if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(staffToken)
        || string.IsNullOrWhiteSpace(memberToken) || planId == null || string.IsNullOrWhiteSpace(gymCode))
        return null;

    return new LoadTestOptions(baseUrl, staffToken, memberToken, gymCode, planId.Value, tenantId, connectionString, memberIds);
}

static bool TryGetValue(string arg, string prefix, out string value)
{
    if (arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
    {
        value = arg[prefix.Length..];
        return true;
    }
    value = string.Empty;
    return false;
}

internal record struct LoadTestOptions(
    string BaseUrl, string StaffToken, string MemberToken, string GymCode,
    Guid PlanId, Guid? TenantId, string? ConnectionString, List<Guid> MemberIds);
