namespace GMS.Tests.Platform;

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using GMS.Api.Filters;
using GMS.Core.Constants;
using GMS.Core.Entities;
using GMS.Core.Entities.Identity;
using GMS.Core.Interfaces;
using GMS.Core.Models;
using GMS.Infrastructure.Persistence;
using GMS.Infrastructure.Services;
using GMS.Platform;
using GMS.Platform.Constants;
using GMS.Platform.DTOs;
using GMS.Platform.Entities;
using GMS.Platform.Interfaces;
using GMS.Platform.Persistence;
using GMS.Platform.Services;

/// <summary>
/// CP6: Platform Console admin actions, coupons on renewal, impersonation JWT constraints.
/// </summary>
public class Cp6PlatformConsoleTests
{
    private const string LocalDb =
        @"Server=(localdb)\mssqllocaldb;Database=GymFlowProDb_PlatformCp6Tests;Trusted_Connection=true;Encrypt=false;";

    [Fact]
    public async Task Coupon_AppliedOnNextRenewal_ExpiredIgnored()
    {
        await EnsureSchemasAsync();
        var tenantId = Guid.NewGuid();

        await using (var infra = CreateInfraDb())
        {
            infra.Tenants.Add(NewTenant(tenantId));
            await infra.SaveChangesAsync();
        }

        await using var platform = CreatePlatformDb();
        var sub = NewSub(tenantId, SubscriptionStatuses.Active, price: 2000m);
        platform.Subscriptions.Add(sub);
        platform.PriceOverrides.Add(new PriceOverride
        {
            TenantId = tenantId,
            DiscountType = "percent",
            Value = 10m,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(7),
            Reason = "loyalty",
            GrantedByPlatformUserId = Guid.NewGuid()
        });
        platform.PriceOverrides.Add(new PriceOverride
        {
            TenantId = tenantId,
            DiscountType = "fixed",
            Value = 500m,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(-1),
            Reason = "expired",
            GrantedByPlatformUserId = Guid.NewGuid()
        });
        await platform.SaveChangesAsync();

        var invoiceSvc = new PlatformInvoiceService(
            platform,
            new CountingPdfRenderer(),
            new NoopFileStorage(),
            new NoopAutomationEnrollment(),
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PlatformBilling:VatRate"] = "0",
                ["PlatformBilling:DueDays"] = "7"
            }).Build(),
            NullLogger<PlatformInvoiceService>.Instance);

        // Allocate sequence year if needed
        var year = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,
            TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time")).Year;
        if (!await platform.PlatformInvoiceSequences.AnyAsync(s => s.Year == year))
        {
            platform.PlatformInvoiceSequences.Add(new PlatformInvoiceSequence { Year = year, LastNumber = 0 });
            await platform.SaveChangesAsync();
        }

        var periodStart = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var invoice = await invoiceSvc.EnsureRenewalInvoiceAsync(
            sub, periodStart, periodStart.AddMonths(1));

        Assert.Equal(1800m, invoice.Subtotal); // 2000 - 10%
        Assert.NotNull(invoice.LinesSnapshot);
        Assert.Contains("Coupon discount", invoice.LinesSnapshot, StringComparison.OrdinalIgnoreCase);

        await CleanupAsync(platform, tenantId);
    }

    [Fact]
    public async Task AdminActions_WritePlatformAuditLog_WithReason()
    {
        await EnsureSchemasAsync();
        var tenantId = Guid.NewGuid();
        var actor = Guid.NewGuid();

        await using (var infra = CreateInfraDb())
        {
            infra.Tenants.Add(NewTenant(tenantId));
            await infra.SaveChangesAsync();
        }

        await using var platform = CreatePlatformDb();
        var sub = NewSub(tenantId, SubscriptionStatuses.Trialing);
        sub.TrialEndsAtUtc = DateTime.UtcNow.AddDays(5);
        platform.Subscriptions.Add(sub);
        await platform.SaveChangesAsync();

        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var audit = new PlatformAuditService(
            platform,
            new HttpContextAccessor(),
            NullLogger<PlatformAuditService>.Instance);
        var admin = new PlatformTenantAdminService(
            platform,
            new SubscriptionWriteRepository(platform),
            new SubscriptionStatusCache(cache, NullLogger<SubscriptionStatusCache>.Instance),
            cache,
            new AlwaysOnFeatureAccess(),
            audit);

        Assert.True((await admin.ApplyCouponAsync(tenantId, actor, new CreateCouponRequest
        {
            DiscountType = "fixed",
            Value = 100m,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(3),
            Reason = "promo"
        }, "127.0.0.1")).Success);

        Assert.True((await admin.ExtendTrialAsync(tenantId, actor, new ExtendTrialRequest
        {
            Days = 7,
            Reason = "sales ask"
        }, "127.0.0.1")).Result.Success);

        var suspend = await admin.ForceSuspendAsync(tenantId, actor, new ForceSuspendRequest
        {
            Reason = "fraud review"
        }, "127.0.0.1");
        Assert.True(suspend.Result.Success);
        Assert.Equal(SubscriptionStatuses.Suspended, suspend.Subscription!.Status);

        Assert.True((await admin.ForceReactivateAsync(tenantId, actor, new ForceReactivateRequest
        {
            Reason = "cleared"
        }, "127.0.0.1")).Result.Success);

        Assert.True((await admin.UpsertFeatureOverrideAsync(tenantId, actor, new UpsertFeatureOverrideRequest
        {
            FeatureKey = "trials",
            Enabled = true,
            Reason = "pilot"
        }, "127.0.0.1")).Result.Success);

        var actions = await platform.PlatformAuditLogs
            .Where(a => a.TenantId == tenantId)
            .Select(a => a.Action)
            .ToListAsync();

        Assert.Contains("platform.tenant.coupon_applied", actions);
        Assert.Contains("platform.tenant.trial_extended", actions);
        Assert.Contains("platform.tenant.force_suspend", actions);
        Assert.Contains("platform.tenant.force_reactivate", actions);
        Assert.Contains("platform.tenant.feature_override_upsert", actions);

        await CleanupAsync(platform, tenantId);
    }

    [Fact]
    public async Task ImpersonationToken_Is30Minutes_HasClaim_CannotExceedLifetime()
    {
        var tokenService = BuildTokenService();
        var platformUserId = Guid.NewGuid();
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = "owner@gym.test",
            FirstName = "Own",
            LastName = "Er"
        };

        var token = await tokenService.GenerateImpersonationAccessTokenAsync(
            user, Guid.NewGuid(), "GYM1", new[] { "Owner" }, new[] { "members.view" },
            platformUserId, lifetimeMinutes: 999);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.Equal(
            platformUserId.ToString(),
            jwt.Claims.First(c => c.Type == ImpersonationClaims.ImpersonatedByPlatformUserId).Value);
        Assert.Equal(
            ImpersonationClaims.TokenUseImpersonation,
            jwt.Claims.First(c => c.Type == ImpersonationClaims.TokenUse).Value);

        var lifetime = jwt.ValidTo - jwt.ValidFrom;
        Assert.True(lifetime <= TimeSpan.FromMinutes(ImpersonationClaims.LifetimeMinutes).Add(TimeSpan.FromSeconds(5)));
        Assert.True(lifetime >= TimeSpan.FromMinutes(ImpersonationClaims.LifetimeMinutes - 1));
    }

    [Fact]
    public async Task ImpersonationExclusion_RejectsPasswordResetAndDeleteStaff()
    {
        var platformUserId = Guid.NewGuid();
        var identity = new ClaimsIdentity("Bearer");
        identity.AddClaim(new Claim(ImpersonationClaims.ImpersonatedByPlatformUserId, platformUserId.ToString()));
        identity.AddClaim(new Claim(ImpersonationClaims.TokenUse, ImpersonationClaims.TokenUseImpersonation));
        identity.AddClaim(new Claim(ClaimTypes.Role, "Owner"));
        var principal = new ClaimsPrincipal(identity);

        Assert.True(ImpersonationPrincipal.IsImpersonating(principal));

        foreach (var _ in new[] { "reset-password", "delete-staff" })
        {
            var http = new DefaultHttpContext { User = principal };
            var actionContext = new ActionContext(http, new Microsoft.AspNetCore.Routing.RouteData(), new ActionDescriptor());
            var executing = new ActionExecutingContext(
                actionContext,
                new List<IFilterMetadata>(),
                new Dictionary<string, object?>(),
                controller: null!);

            var filter = new RejectImpersonationAttribute();
            var ranNext = false;
            await filter.OnActionExecutionAsync(executing, () =>
            {
                ranNext = true;
                return Task.FromResult(new ActionExecutedContext(actionContext, new List<IFilterMetadata>(), null!));
            });

            Assert.False(ranNext);
            var result = Assert.IsType<ObjectResult>(executing.Result);
            Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
            Assert.Contains(RejectImpersonationAttribute.ErrorCode, JsonSerializer.Serialize(result.Value));
        }
    }

    [Fact]
    public void ImpersonationExclusionList_IsExplicit()
    {
        // Documented exclusion list for CP6 acceptance — endpoints that require real owner identity.
        var exclusion = new[]
        {
            "POST /api/admin/staff/{id}/reset-password",
            "DELETE /api/admin/staff/{id}"
        };

        Assert.Equal(2, exclusion.Length);
        Assert.Contains(exclusion, e => e.Contains("reset-password", StringComparison.Ordinal));
        Assert.Contains(exclusion, e => e.Contains("DELETE /api/admin/staff", StringComparison.Ordinal));
    }

    private static TokenService BuildTokenService()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtSettings:SecretKey"] = "Test-Only-Secret-Key-Must-Be-At-Least-32-Characters-Long!",
                ["JwtSettings:Issuer"] = "GymFlowPro.Tests",
                ["JwtSettings:Audience"] = "GymFlowPro.Tests.Clients",
                ["JwtSettings:AccessTokenExpirationMinutes"] = "15"
            })
            .Build();

        return new TokenService(configuration);
    }

    private static async Task EnsureSchemasAsync()
    {
        await using var infra = CreateInfraDb();
        await infra.Database.MigrateAsync();

        await using var platform = CreatePlatformDb();
        await platform.Database.MigrateAsync();
    }

    private static GymFlowProDbContext CreateInfraDb()
    {
        var options = new DbContextOptionsBuilder<GymFlowProDbContext>()
            .UseSqlServer(LocalDb)
            .Options;
        var tc = new TestTenantContext();
        tc.SetTenant(Guid.NewGuid(), "t", "Egypt Standard Time");
        return new GymFlowProDbContext(options, tc);
    }

    private static PlatformDbContext CreatePlatformDb()
    {
        var options = new DbContextOptionsBuilder<PlatformDbContext>()
            .UseSqlServer(LocalDb, sql =>
            {
                sql.MigrationsHistoryTable(
                    PlatformServiceExtensions.MigrationsHistoryTable,
                    PlatformServiceExtensions.Schema);
            })
            .Options;
        return new PlatformDbContext(options);
    }

    private static Tenant NewTenant(Guid id) => new()
    {
        Id = id,
        Name = "CP6 Gym",
        NameAr = "صالة",
        GymCode = $"C6{id.ToString("N")[..6]}",
        City = "Cairo",
        Address = "Test",
        PhoneNumber = "+201000000000",
        Email = $"{id:N}@cp6.test",
        IsActive = true,
        SubscriptionStartDate = DateTime.UtcNow,
        Settings = "{}"
    };

    private static PlatformSubscription NewSub(Guid tenantId, string status, decimal price = 1999m) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        PlanTier = PlanTiers.Growth,
        Status = status,
        BillingCycle = BillingCycles.Monthly,
        PriceEgp = price,
        CurrentPeriodStart = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-10)),
        CurrentPeriodEnd = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(20)),
        UpdatedAtUtc = DateTime.UtcNow
    };

    private static async Task CleanupAsync(PlatformDbContext platform, Guid tenantId)
    {
        await platform.PriceOverrides.Where(p => p.TenantId == tenantId).ExecuteDeleteAsync();
        await platform.FeatureOverrides.Where(f => f.TenantId == tenantId).ExecuteDeleteAsync();
        await platform.PlatformAuditLogs.Where(a => a.TenantId == tenantId).ExecuteDeleteAsync();
        await platform.AutomationEnrollments.Where(e => e.TenantId == tenantId).ExecuteDeleteAsync();
        await platform.PlatformInvoices.Where(i => i.TenantId == tenantId).ExecuteDeleteAsync();
        await platform.SubscriptionChanges.Where(c => c.TenantId == tenantId).ExecuteDeleteAsync();
        await platform.Subscriptions.Where(s => s.TenantId == tenantId).ExecuteDeleteAsync();

        await using var infra = CreateInfraDb();
        await infra.Tenants.Where(t => t.Id == tenantId).ExecuteDeleteAsync();
    }

    private sealed class TestTenantContext : ITenantContext
    {
        public Guid TenantId { get; private set; }
        public string? TenantName { get; private set; }
        public string? TimeZone { get; private set; }
        public bool IsInitialized => TenantId != Guid.Empty;
        public void SetTenant(Guid tenantId, string tenantName, string timeZone)
        {
            TenantId = tenantId;
            TenantName = tenantName;
            TimeZone = timeZone;
        }
        public void Clear()
        {
            TenantId = Guid.Empty;
            TenantName = null;
            TimeZone = null;
        }
    }

    private sealed class AlwaysOnFeatureAccess : IFeatureAccessService
    {
        public Task<bool> IsEnabledAsync(Guid tenantId, string featureKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
        public Task InvalidateAsync(Guid tenantId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class CountingPdfRenderer : IInvoicePdfRenderer
    {
        public byte[] Render(InvoicePdfModel model) => new byte[] { 1, 2, 3 };
    }

    private sealed class NoopFileStorage : IFileStorageService
    {
        public Task<string> UploadAsync(Stream stream, string fileName, string folder) =>
            Task.FromResult($"/uploads/{folder}/{fileName}");
        public Task DeleteAsync(string fileUrl) => Task.CompletedTask;
        public Task<bool> ExistsAsync(string fileUrl) => Task.FromResult(true);
    }

    private sealed class NoopAutomationEnrollment : IAutomationEnrollmentService
    {
        public Task<AutomationEnrollment> EnrollAsync(
            string sequenceKey, string subjectType, Guid subjectId, Guid? tenantId,
            DateTime firstRunAtUtc, int initialStep = 0, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AutomationEnrollment
            {
                SequenceKey = sequenceKey,
                SubjectType = subjectType,
                SubjectId = subjectId,
                TenantId = tenantId,
                Step = initialStep,
                NextRunAtUtc = firstRunAtUtc
            });

        public Task<bool> HaltAsync(
            string subjectType, Guid subjectId, string reason, string? sequenceKey = null,
            CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<AutomationEnrollment?> GetActiveAsync(
            string subjectType, Guid subjectId, string? sequenceKey = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<AutomationEnrollment?>(null);
    }
}
