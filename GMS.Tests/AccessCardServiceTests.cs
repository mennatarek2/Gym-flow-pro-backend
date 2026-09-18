namespace GMS.Tests;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using GMS.Application.DTOs.AccessCards;
using GMS.Application.DTOs.Attendance;
using GMS.Application.Services;
using GMS.Core.Constants;
using GMS.Core.Entities;
using GMS.Infrastructure.Persistence;
using GMS.Infrastructure.Repositories;
using GMS.Infrastructure.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using GMS.Application.Interfaces;
using GMS.Core.Interfaces;

public class AccessCardServiceTests
{
    private static (GymFlowProDbContext ctx, AccessCardService svc, Guid tenantId) CreateSut()
    {
        var tenantId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<GymFlowProDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var tenantContext = new TenantContext();
        tenantContext.SetTenant(tenantId, "Test Tenant", "Africa/Cairo");
        var ctx = new GymFlowProDbContext(options, tenantContext);
        var audit = new AuditService(ctx, new Microsoft.AspNetCore.Http.HttpContextAccessor(), tenantContext, NullLogger<AuditService>.Instance);
        var svc = new AccessCardService(ctx, audit, NullLogger<AccessCardService>.Instance);
        return (ctx, svc, tenantId);
    }

    private static void SeedTenant(GymFlowProDbContext ctx, Guid tenantId)
    {
        ctx.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Test Gym",
            NameAr = "صالة",
            GymCode = $"GYM-{tenantId:N}"[..13],
            City = "Cairo",
            Address = "Addr",
            PhoneNumber = "0100000000",
            Email = $"{tenantId}@test.local",
            SubscriptionStartDate = DateTime.UtcNow
        });
    }

    private static GymMember SeedMember(GymFlowProDbContext ctx, Guid tenantId, string number)
    {
        var m = new GymMember
        {
            TenantId = tenantId,
            MemberNumber = number,
            FullName = "Member " + number,
            FullNameAr = "عضو",
            PhoneNumber = "+20100" + Random.Shared.Next(1000000, 9999999),
            DateOfBirth = new DateOnly(1990, 1, 1),
            IsActive = true
        };
        ctx.GymMembers.Add(m);
        return m;
    }

    [Fact]
    public async Task BulkCreate_UniqueCodes_Succeeds()
    {
        var (ctx, svc, tenantId) = CreateSut();
        SeedTenant(ctx, tenantId);
        await ctx.SaveChangesAsync();

        var result = await svc.BulkCreateAsync(tenantId, new BulkCreateAccessCardsRequest
        {
            Prefix = "CARD",
            StartNumber = 1,
            Quantity = 3
        });

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(3, result.Data!.Created);
        Assert.Equal(new[] { "CARD-0001", "CARD-0002", "CARD-0003" }, result.Data.Codes);
        Assert.Equal(3, await ctx.AccessCards.CountAsync(c => c.Status == AccessCardStatuses.Available));
    }

    [Fact]
    public async Task BulkCreate_DuplicateCodes_Rejected()
    {
        var (ctx, svc, tenantId) = CreateSut();
        SeedTenant(ctx, tenantId);
        await ctx.SaveChangesAsync();
        Assert.True((await svc.BulkCreateAsync(tenantId, new BulkCreateAccessCardsRequest
        {
            Prefix = "CARD", StartNumber = 1, Quantity = 2
        })).IsSuccess);

        var dup = await svc.BulkCreateAsync(tenantId, new BulkCreateAccessCardsRequest
        {
            Prefix = "CARD", StartNumber = 1, Quantity = 1
        });
        Assert.False(dup.IsSuccess);
        Assert.Contains("Duplicate", dup.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BulkCreate_TenantIsolation_SameCodeAllowedOnOtherTenant()
    {
        var (ctxA, svcA, tenantA) = CreateSut();
        SeedTenant(ctxA, tenantA);
        await ctxA.SaveChangesAsync();
        Assert.True((await svcA.BulkCreateAsync(tenantA, new BulkCreateAccessCardsRequest
        {
            Prefix = "CARD", StartNumber = 5, Quantity = 1
        })).IsSuccess);

        var (ctxB, svcB, tenantB) = CreateSut();
        SeedTenant(ctxB, tenantB);
        await ctxB.SaveChangesAsync();
        var other = await svcB.BulkCreateAsync(tenantB, new BulkCreateAccessCardsRequest
        {
            Prefix = "CARD", StartNumber = 5, Quantity = 1
        });
        Assert.True(other.IsSuccess, other.Error);
    }

    [Fact]
    public async Task Assign_Available_Succeeds_AndInventoryCounts()
    {
        var (ctx, svc, tenantId) = CreateSut();
        SeedTenant(ctx, tenantId);
        var member = SeedMember(ctx, tenantId, "GYM-100");
        await ctx.SaveChangesAsync();
        await svc.BulkCreateAsync(tenantId, new BulkCreateAccessCardsRequest
        {
            Prefix = "CARD", StartNumber = 10, Quantity = 2
        });

        var assign = await svc.AssignAsync(tenantId, new AssignAccessCardRequest
        {
            MemberId = member.Id,
            Code = "CARD-0010"
        });
        Assert.True(assign.IsSuccess, assign.Error);
        Assert.Equal(AccessCardStatuses.Assigned, assign.Data!.Status);
        Assert.Equal(member.Id, assign.Data.MemberId);

        var inv = await svc.GetInventoryAsync(tenantId);
        Assert.Equal(1, inv.Data!.Assigned);
        Assert.Equal(1, inv.Data.Available);
        Assert.Equal(2, inv.Data.Total);
    }

    [Fact]
    public async Task Assign_LostOrAssigned_Fails()
    {
        var (ctx, svc, tenantId) = CreateSut();
        SeedTenant(ctx, tenantId);
        var m1 = SeedMember(ctx, tenantId, "GYM-101");
        var m2 = SeedMember(ctx, tenantId, "GYM-102");
        await ctx.SaveChangesAsync();
        await svc.BulkCreateAsync(tenantId, new BulkCreateAccessCardsRequest
        {
            Prefix = "CARD", StartNumber = 20, Quantity = 1
        });
        Assert.True((await svc.AssignAsync(tenantId, new AssignAccessCardRequest
        {
            MemberId = m1.Id, Code = "CARD-0020"
        })).IsSuccess);

        var again = await svc.AssignAsync(tenantId, new AssignAccessCardRequest
        {
            MemberId = m2.Id, Code = "CARD-0020"
        });
        Assert.False(again.IsSuccess);

        var card = await ctx.AccessCards.FirstAsync(c => c.Code == "CARD-0020");
        await svc.MarkLostAsync(tenantId, card.Id, new MarkAccessCardRequest());
        var afterLost = await svc.AssignAsync(tenantId, new AssignAccessCardRequest
        {
            MemberId = m2.Id, Code = "CARD-0020"
        });
        Assert.False(afterLost.IsSuccess);
    }

    [Fact]
    public async Task Assign_CrossTenant_Fails()
    {
        var (ctxA, svcA, tenantA) = CreateSut();
        SeedTenant(ctxA, tenantA);
        await ctxA.SaveChangesAsync();
        await svcA.BulkCreateAsync(tenantA, new BulkCreateAccessCardsRequest
        {
            Prefix = "CARD", StartNumber = 30, Quantity = 1
        });

        var (ctxB, svcB, tenantB) = CreateSut();
        SeedTenant(ctxB, tenantB);
        var memberB = SeedMember(ctxB, tenantB, "GYM-200");
        await ctxB.SaveChangesAsync();

        // Card lives only in tenant A DB; tenant B assign by code finds nothing.
        var result = await svcB.AssignAsync(tenantB, new AssignAccessCardRequest
        {
            MemberId = memberB.Id,
            Code = "CARD-0030"
        });
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task Replace_OldLost_NewAssigned()
    {
        var (ctx, svc, tenantId) = CreateSut();
        SeedTenant(ctx, tenantId);
        var member = SeedMember(ctx, tenantId, "GYM-300");
        await ctx.SaveChangesAsync();
        await svc.BulkCreateAsync(tenantId, new BulkCreateAccessCardsRequest
        {
            Prefix = "CARD", StartNumber = 40, Quantity = 2
        });
        Assert.True((await svc.AssignAsync(tenantId, new AssignAccessCardRequest
        {
            MemberId = member.Id, Code = "CARD-0040"
        })).IsSuccess);

        var replaced = await svc.ReplaceAsync(tenantId, new ReplaceAccessCardRequest
        {
            MemberId = member.Id,
            NewCode = "CARD-0041",
            OldStatus = AccessCardStatuses.Lost,
            Reason = "Lost at desk"
        });
        Assert.True(replaced.IsSuccess, replaced.Error);
        Assert.Equal("CARD-0041", replaced.Data!.Code);

        var old = await ctx.AccessCards.FirstAsync(c => c.Code == "CARD-0040");
        Assert.Equal(AccessCardStatuses.Lost, old.Status);
        Assert.Equal(member.Id, old.MemberId);
    }

    [Fact]
    public async Task LegacyBackfill_Semantics_CodeEqualsMemberNumberAssigned()
    {
        var (ctx, svc, tenantId) = CreateSut();
        SeedTenant(ctx, tenantId);
        var member = SeedMember(ctx, tenantId, "GYM-777");
        await ctx.SaveChangesAsync();

        // Simulate migration backfill (InMemory — no SQL).
        ctx.AccessCards.Add(new AccessCard
        {
            TenantId = tenantId,
            Code = member.MemberNumber,
            Status = AccessCardStatuses.Assigned,
            MemberId = member.Id,
            AssignedAtUtc = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var assigned = await svc.GetAssignedForMemberAsync(tenantId, member.Id);
        Assert.True(assigned.IsSuccess);
        Assert.Equal("GYM-777", assigned.Data!.Code);
        Assert.Equal(AccessCardStatuses.Assigned, assigned.Data.Status);
    }

    [Fact]
    public async Task MembershipExpireDoesNotFreeCard_StatusStaysAssigned()
    {
        var (ctx, svc, tenantId) = CreateSut();
        SeedTenant(ctx, tenantId);
        var member = SeedMember(ctx, tenantId, "GYM-400");
        var plan = new MembershipPlan
        {
            TenantId = tenantId,
            Name = "Monthly",
            NameAr = "شهري",
            PlanType = "time_based",
            DurationDays = 30,
            Price = 500m
        };
        ctx.MembershipPlans.Add(plan);
        ctx.Memberships.Add(new Membership
        {
            TenantId = tenantId,
            MemberId = member.Id,
            PlanId = plan.Id,
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-60),
            EndDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1),
            Status = "expired"
        });
        await ctx.SaveChangesAsync();
        await svc.BulkCreateAsync(tenantId, new BulkCreateAccessCardsRequest
        {
            Prefix = "CARD", StartNumber = 50, Quantity = 1
        });
        Assert.True((await svc.AssignAsync(tenantId, new AssignAccessCardRequest
        {
            MemberId = member.Id, Code = "CARD-0050"
        })).IsSuccess);

        // Expire path does not touch AccessCard — card remains Assigned.
        var card = await ctx.AccessCards.FirstAsync(c => c.Code == "CARD-0050");
        Assert.Equal(AccessCardStatuses.Assigned, card.Status);
        Assert.Equal(member.Id, card.MemberId);
    }

    /// <summary>
    /// Concurrent assign of the same Available card — one winner. Requires LocalDB
    /// (ExecuteUpdate / row locking). Skips cleanly when LocalDB is unavailable.
    /// </summary>
    [Fact]
    public async Task Assign_ConcurrentSameCard_OnlyOneSucceeds()
    {
        const string connectionString =
            "Server=(localdb)\\mssqllocaldb;Database=GymFlowProDb;Trusted_Connection=true;Encrypt=false;";

        var options = new DbContextOptionsBuilder<GymFlowProDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        await using (var probe = new GymFlowProDbContext(options, null))
        {
            try
            {
                if (!await probe.Database.CanConnectAsync())
                    return; // Local Edition / CI without LocalDB — skip
            }
            catch
            {
                return;
            }
        }

        var tenantId = Guid.NewGuid();
        var memberAId = Guid.NewGuid();
        var memberBId = Guid.NewGuid();
        var cardId = Guid.NewGuid();
        var code = "RACE-" + tenantId.ToString("N")[..8];

        await using (var seed = new GymFlowProDbContext(options, null))
        {
            await seed.Database.MigrateAsync();
            seed.Tenants.Add(new Tenant
            {
                Id = tenantId,
                Name = "Card Race",
                NameAr = "سباق",
                GymCode = "CRD-" + tenantId.ToString("N")[..12],
                City = "Cairo",
                Address = "T",
                PhoneNumber = "0100000000",
                Email = $"crd-{tenantId}@test.local",
                SubscriptionStartDate = DateTime.UtcNow
            });
            seed.GymMembers.AddRange(
                new GymMember
                {
                    Id = memberAId,
                    TenantId = tenantId,
                    MemberNumber = "GYM-A" + tenantId.ToString("N")[..4],
                    FullName = "A",
                    FullNameAr = "أ",
                    PhoneNumber = "01011111111",
                    DateOfBirth = new DateOnly(1990, 1, 1),
                    IsActive = true
                },
                new GymMember
                {
                    Id = memberBId,
                    TenantId = tenantId,
                    MemberNumber = "GYM-B" + tenantId.ToString("N")[..4],
                    FullName = "B",
                    FullNameAr = "ب",
                    PhoneNumber = "01022222222",
                    DateOfBirth = new DateOnly(1990, 1, 1),
                    IsActive = true
                });
            seed.AccessCards.Add(new AccessCard
            {
                Id = cardId,
                TenantId = tenantId,
                Code = code,
                Status = AccessCardStatuses.Available
            });
            await seed.SaveChangesAsync();
        }

        try
        {
            var results = new bool[2];
            await Parallel.ForEachAsync(new[] { 0, 1 }, async (i, _) =>
            {
                var tc = new TenantContext();
                tc.SetTenant(tenantId, "Card Race", "Africa/Cairo");
                await using var ctx = new GymFlowProDbContext(options, tc);
                var audit = new AuditService(ctx, new Microsoft.AspNetCore.Http.HttpContextAccessor(), tc, NullLogger<AuditService>.Instance);
                var svc = new AccessCardService(ctx, audit, NullLogger<AccessCardService>.Instance);
                var memberId = i == 0 ? memberAId : memberBId;
                var r = await svc.AssignAsync(tenantId, new AssignAccessCardRequest
                {
                    MemberId = memberId,
                    CardId = cardId
                });
                results[i] = r.IsSuccess;
            });

            Assert.Equal(1, results.Count(x => x));
        }
        finally
        {
            await using var cleanup = new GymFlowProDbContext(options, null);
            await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM audit_events WHERE TenantId = {tenantId}");
            await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM access_cards WHERE TenantId = {tenantId}");
            await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM gym_members WHERE TenantId = {tenantId}");
            await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM tenants WHERE Id = {tenantId}");
        }
    }
}

public class AccessCardCheckinTests
{
    private class NoOpCheckinNotifier : ICheckinNotifier
    {
        public Task NotifyCheckinAsync(Guid tenantId, Guid memberId, string memberName,
            string memberNumber, DateTime checkInTime, string entryMethod) => Task.CompletedTask;
    }

    private static IGymQrTokenService BuildQrTokenService() => new GymQrTokenService(
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtSettings:SecretKey"] = "Test-Only-Secret-Key-Must-Be-At-Least-32-Characters-Long!"
            })
            .Build());

    private static (GymFlowProDbContext ctx, CheckinService svc, Guid tenantId, Guid staffIdentityId) CreateSut()
    {
        var tenantId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<GymFlowProDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var tenantContext = new TenantContext();
        tenantContext.SetTenant(tenantId, "Test Tenant", "Africa/Cairo");
        var ctx = new GymFlowProDbContext(options, tenantContext);
        var audit = new AuditService(ctx, new Microsoft.AspNetCore.Http.HttpContextAccessor(), tenantContext, NullLogger<AuditService>.Instance);
        var cache = new MemoryCache(new MemoryCacheOptions());
        var svc = new CheckinService(
            ctx, new MemberRepository(ctx), new AttendanceRepository(ctx),
            cache, new NoOpCheckinNotifier(), audit, BuildQrTokenService(),
            NullLogger<CheckinService>.Instance);

        ctx.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Test Gym",
            NameAr = "صالة",
            GymCode = $"GYM-{tenantId:N}"[..13],
            City = "Cairo",
            Address = "Addr",
            PhoneNumber = "0100000000",
            Email = $"{tenantId}@test.local",
            SubscriptionStartDate = DateTime.UtcNow
        });
        var identityUserId = Guid.NewGuid();
        ctx.AppUsers.Add(new AppUser
        {
            TenantId = tenantId,
            UserId = identityUserId.ToString(),
            FirstName = "Front",
            LastName = "Desk",
            Email = $"staff-{Guid.NewGuid()}@test.local",
            Role = "Receptionist"
        });
        return (ctx, svc, tenantId, identityUserId);
    }

    private static (GymMember member, Membership membership) SeedActiveMember(
        GymFlowProDbContext ctx, Guid tenantId, string memberNumber)
    {
        var plan = new MembershipPlan
        {
            TenantId = tenantId,
            Name = "Monthly",
            NameAr = "شهري",
            PlanType = "time_based",
            DurationDays = 30,
            Price = 500m
        };
        ctx.MembershipPlans.Add(plan);
        var member = new GymMember
        {
            TenantId = tenantId,
            MemberNumber = memberNumber,
            FullName = "Active Member",
            FullNameAr = "عضو نشط",
            PhoneNumber = "+20100" + Random.Shared.Next(1000000, 9999999),
            DateOfBirth = new DateOnly(1992, 5, 5),
            IsActive = true
        };
        ctx.GymMembers.Add(member);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var membership = new Membership
        {
            TenantId = tenantId,
            MemberId = member.Id,
            PlanId = plan.Id,
            StartDate = today.AddDays(-5),
            EndDate = today.AddDays(25),
            Status = "active"
        };
        ctx.Memberships.Add(membership);
        return (member, membership);
    }

    [Fact]
    public async Task BarcodeCheckin_LegacyMemberNumber_StillWorks_LocalOffline()
    {
        var (ctx, svc, tenantId, staffId) = CreateSut();
        SeedActiveMember(ctx, tenantId, "GYM-901");
        await ctx.SaveChangesAsync();

        var result = await svc.ProcessBarcodeCheckinAsync(
            new BarcodeCheckinRequest { Code = "GYM-901" }, staffId, tenantId);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("barcode", (await ctx.GymAttendances.FirstAsync()).EntryMethod);
    }

    [Fact]
    public async Task BarcodeCheckin_AssignedCardCode_Succeeds()
    {
        var (ctx, svc, tenantId, staffId) = CreateSut();
        var (member, _) = SeedActiveMember(ctx, tenantId, "GYM-902");
        ctx.AccessCards.Add(new AccessCard
        {
            TenantId = tenantId,
            Code = "CARD-9902",
            Status = AccessCardStatuses.Assigned,
            MemberId = member.Id,
            AssignedAtUtc = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var result = await svc.ProcessBarcodeCheckinAsync(
            new BarcodeCheckinRequest { Code = "CARD-9902" }, staffId, tenantId);

        Assert.True(result.IsSuccess, result.Error);
    }

    [Fact]
    public async Task BarcodeCheckin_LostCard_BlockedEvenIfCodeEqualsMemberNumber()
    {
        var (ctx, svc, tenantId, staffId) = CreateSut();
        var (member, _) = SeedActiveMember(ctx, tenantId, "GYM-903");
        ctx.AccessCards.Add(new AccessCard
        {
            TenantId = tenantId,
            Code = member.MemberNumber,
            Status = AccessCardStatuses.Lost,
            MemberId = member.Id,
            LostAtUtc = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var result = await svc.ProcessBarcodeCheckinAsync(
            new BarcodeCheckinRequest { Code = "GYM-903" }, staffId, tenantId);

        Assert.False(result.IsSuccess);
        Assert.Contains("lost", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BarcodeCheckin_AfterReplace_OldFails_NewSucceeds()
    {
        var (ctx, svc, tenantId, staffId) = CreateSut();
        var (member, _) = SeedActiveMember(ctx, tenantId, "GYM-904");
        ctx.AccessCards.Add(new AccessCard
        {
            TenantId = tenantId,
            Code = "CARD-OLD",
            Status = AccessCardStatuses.Lost,
            MemberId = member.Id,
            LostAtUtc = DateTime.UtcNow
        });
        ctx.AccessCards.Add(new AccessCard
        {
            TenantId = tenantId,
            Code = "CARD-NEW",
            Status = AccessCardStatuses.Assigned,
            MemberId = member.Id,
            AssignedAtUtc = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var oldResult = await svc.ProcessBarcodeCheckinAsync(
            new BarcodeCheckinRequest { Code = "CARD-OLD" }, staffId, tenantId);
        Assert.False(oldResult.IsSuccess);

        var newResult = await svc.ProcessBarcodeCheckinAsync(
            new BarcodeCheckinRequest { Code = "CARD-NEW" }, staffId, tenantId);
        Assert.True(newResult.IsSuccess, newResult.Error);
    }
}
