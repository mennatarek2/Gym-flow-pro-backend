namespace GMS.Tests;

using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using GMS.Application.DTOs.Attendance;
using GMS.Application.DTOs.Audit;
using GMS.Application.Services;
using GMS.Core.Attributes;
using GMS.Core.Entities;
using GMS.Core.Enums;
using GMS.Core.Interfaces;
using GMS.Infrastructure.Persistence;
using GMS.Infrastructure.Repositories;
using GMS.Infrastructure.Services;

public class AuditServiceTests
{
    private class SampleSnapshot
    {
        public string Name { get; set; } = string.Empty;

        [Redact]
        public string NationalId { get; set; } = string.Empty;
    }

    private class NoOpCheckinNotifier : ICheckinNotifier
    {
        public Task NotifyCheckinAsync(Guid tenantId, Guid memberId, string memberName,
            string memberNumber, DateTime checkInTime, string entryMethod) => Task.CompletedTask;
    }

    private static GymFlowProDbContext CreateContext(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<GymFlowProDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var tenantContext = new TenantContext();
        tenantContext.SetTenant(tenantId, "Test Tenant", "Africa/Cairo");

        return new GymFlowProDbContext(options, tenantContext);
    }

    private static AuditService CreateAuditService(GymFlowProDbContext ctx, Guid tenantId, HttpContext? httpContext = null)
    {
        var tenantContext = new TenantContext();
        tenantContext.SetTenant(tenantId, "Test Tenant", "Africa/Cairo");

        var accessor = new HttpContextAccessor { HttpContext = httpContext };

        return new AuditService(ctx, accessor, tenantContext, NullLogger<AuditService>.Instance);
    }

    [Fact]
    public async Task LogAsync_RedactsAnnotatedProperty_ButKeepsOthers()
    {
        var tenantId = Guid.NewGuid();
        await using var ctx = CreateContext(tenantId);
        var auditService = CreateAuditService(ctx, tenantId);

        await auditService.LogAsync(
            "member.update",
            "GymMember",
            Guid.NewGuid(),
            before: new SampleSnapshot { Name = "Ali", NationalId = "29001011234567" });

        var stored = await ctx.AuditEvents.SingleAsync();

        Assert.Contains("\"Name\":\"Ali\"", stored.BeforeJson);
        Assert.Contains("***REDACTED***", stored.BeforeJson);
        Assert.DoesNotContain("29001011234567", stored.BeforeJson);
    }

    [Fact]
    public async Task LogAsync_WhenDbContextThrows_DoesNotPropagateException()
    {
        var tenantId = Guid.NewGuid();
        var ctx = CreateContext(tenantId);
        var auditService = CreateAuditService(ctx, tenantId);

        await ctx.DisposeAsync();

        var exception = await Record.ExceptionAsync(() =>
            auditService.LogAsync("checkin.manual", "GymAttendance", Guid.NewGuid()));

        Assert.Null(exception);
    }

    [Fact]
    public async Task GetAuditEventsAsync_OnlyReturnsCurrentTenantsRecords()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await using var ctx = CreateContext(tenantA);
        ctx.AuditEvents.Add(new AuditEvent { TenantId = tenantA, Action = "checkin.manual" });
        ctx.AuditEvents.Add(new AuditEvent { TenantId = tenantA, Action = "sale.discount.override" });
        ctx.AuditEvents.Add(new AuditEvent { TenantId = tenantB, Action = "checkin.manual" });
        await ctx.SaveChangesAsync();

        var auditService = CreateAuditService(ctx, tenantA);

        var result = await auditService.GetAuditEventsAsync(tenantA, new AuditEventQueryRequest());

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Data!.TotalCount);
        Assert.Equal(2, result.Data.Items.Count);
        Assert.Equal(
            new[] { "checkin.manual", "sale.discount.override" },
            result.Data.Items.Select(i => i.Action).OrderBy(a => a));
    }

    [Fact]
    public async Task ManualCheckin_WritesAuditEventForTheAttendance()
    {
        var tenantId = Guid.NewGuid();
        var identityUserId = Guid.NewGuid();

        await using var ctx = CreateContext(tenantId);

        var staffUser = new AppUser
        {
            TenantId = tenantId,
            UserId = identityUserId.ToString(),
            FirstName = "Laila",
            LastName = "Reception",
            Email = "laila@gymflow.test",
            Role = "Receptionist"
        };
        ctx.AppUsers.Add(staffUser);

        var plan = new MembershipPlan
        {
            TenantId = tenantId,
            Name = "Monthly Unlimited",
            NameAr = "شهري",
            PlanType = "monthly_unlimited",
            DurationDays = 30,
            Price = 500
        };
        ctx.MembershipPlans.Add(plan);

        var member = new GymMember
        {
            TenantId = tenantId,
            MemberNumber = "M1",
            FullName = "Test Member",
            FullNameAr = "عضو تجريبي",
            PhoneNumber = "01000000000"
        };
        ctx.GymMembers.Add(member);

        var membership = new Membership
        {
            TenantId = tenantId,
            MemberId = member.Id,
            PlanId = plan.Id,
            Status = "active",
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),
            EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(29))
        };
        ctx.Memberships.Add(membership);

        await ctx.SaveChangesAsync();

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.NameIdentifier, identityUserId.ToString()) }, "Test"))
        };

        var auditService = CreateAuditService(ctx, tenantId, httpContext);
        var checkinService = new CheckinService(
            ctx,
            new MemberRepository(ctx),
            new AttendanceRepository(ctx),
            new MemoryCache(new MemoryCacheOptions()),
            new NoOpCheckinNotifier(),
            auditService,
            NullLogger<CheckinService>.Instance);

        var result = await checkinService.ProcessManualCheckinAsync(
            new ManualCheckinRequest { MemberId = member.Id, Reason = ManualCheckinReason.NoAppYet },
            identityUserId,
            tenantId);

        Assert.True(result.IsSuccess, result.Error);

        var auditEvent = await ctx.AuditEvents.SingleOrDefaultAsync(a => a.Action == "checkin.manual");
        Assert.NotNull(auditEvent);
        Assert.Equal("GymAttendance", auditEvent!.EntityType);
        Assert.Equal(result.Data!.AttendanceId, auditEvent.EntityId);
        Assert.Equal(staffUser.Id, auditEvent.ActorUserId);
    }
}
