namespace GMS.Tests;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using GMS.Application.Options;
using GMS.Application.Services;
using GMS.Core.Entities;
using GMS.Infrastructure.Persistence;
using GMS.Infrastructure.Services;

public class MemberAppActivationServiceTests
{
    private static (GymFlowProDbContext ctx, MemberAppActivationService svc, Guid tenantId) CreateSut()
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
        var svc = new MemberAppActivationService(
            ctx,
            tenantContext,
            audit,
            Options.Create(new MemberAppActivationOptions { ExpirationHours = 24, CodePepper = "unit-test-pepper" }),
            config,
            NullLogger<MemberAppActivationService>.Instance);
        return (ctx, svc, tenantId);
    }

    private static GymMember SeedMember(GymFlowProDbContext ctx, Guid tenantId, bool active = true)
    {
        ctx.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Gym",
            NameAr = "صالة",
            GymCode = "GYM-ACT-01",
            City = "Cairo",
            Address = "Addr",
            PhoneNumber = "0100000000",
            Email = $"{tenantId}@t.local",
            SubscriptionStartDate = DateTime.UtcNow,
            IsActive = true
        });
        var m = new GymMember
        {
            TenantId = tenantId,
            MemberNumber = "GYM-001",
            FullName = "Test Member",
            PhoneNumber = "+201000000001",
            Email = "m@test.local",
            IsActive = active,
            DateOfBirth = new DateOnly(1990, 1, 1)
        };
        ctx.GymMembers.Add(m);
        ctx.SaveChanges();
        return m;
    }

    [Fact]
    public async Task Generate_ReturnsPlaintext_AndStoresOnlyHash()
    {
        var (ctx, svc, tenantId) = CreateSut();
        var member = SeedMember(ctx, tenantId);

        var result = await svc.GenerateAsync(member.Id, Guid.NewGuid());
        Assert.True(result.IsSuccess);
        Assert.False(string.IsNullOrWhiteSpace(result.Data!.ActivationCode));
        Assert.Contains('-', result.Data.ActivationCode);
        Assert.True(result.Data.ExpiresInMinutes > 0);

        var row = await ctx.MemberAppActivationCodes.SingleAsync();
        Assert.False(string.IsNullOrEmpty(row.CodeHash));
        Assert.DoesNotContain(result.Data.ActivationCode.Replace("-", ""), row.CodeHash, StringComparison.OrdinalIgnoreCase);
        Assert.Null(row.ConsumedAtUtc);
    }

    [Fact]
    public async Task Generate_RevokesPreviousActiveCode()
    {
        var (ctx, svc, tenantId) = CreateSut();
        var member = SeedMember(ctx, tenantId);

        var first = await svc.GenerateAsync(member.Id, null);
        var second = await svc.GenerateAsync(member.Id, null);
        Assert.True(first.IsSuccess && second.IsSuccess);
        Assert.NotEqual(first.Data!.ActivationCode, second.Data!.ActivationCode);

        var rows = await ctx.MemberAppActivationCodes.OrderBy(c => c.CreatedAtUtc).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.NotNull(rows[0].RevokedAtUtc);
        Assert.Null(rows[1].RevokedAtUtc);

        var oldConsume = await svc.ConsumeAsync(tenantId, first.Data.ActivationCode);
        Assert.False(oldConsume.IsSuccess);

        var newConsume = await svc.ConsumeAsync(tenantId, second.Data.ActivationCode);
        Assert.True(newConsume.IsSuccess);
    }

    [Fact]
    public async Task Consume_SucceedsOnce_ThenFails()
    {
        var (ctx, svc, tenantId) = CreateSut();
        var member = SeedMember(ctx, tenantId);
        var gen = await svc.GenerateAsync(member.Id, null);

        var a = await svc.ConsumeAsync(tenantId, gen.Data!.ActivationCode);
        Assert.True(a.IsSuccess);
        Assert.Equal(member.Id, a.Data!.Id);

        var b = await svc.ConsumeAsync(tenantId, gen.Data.ActivationCode);
        Assert.False(b.IsSuccess);
        Assert.Contains("already been used", b.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Consume_WrongTenant_Fails()
    {
        var (ctx, svc, tenantId) = CreateSut();
        var member = SeedMember(ctx, tenantId);
        var gen = await svc.GenerateAsync(member.Id, null);

        var otherTenant = Guid.NewGuid();
        var fail = await svc.ConsumeAsync(otherTenant, gen.Data!.ActivationCode);
        Assert.False(fail.IsSuccess);
    }

    [Fact]
    public async Task Generate_ArchivedMember_Fails()
    {
        var (ctx, svc, tenantId) = CreateSut();
        var member = SeedMember(ctx, tenantId, active: false);
        var result = await svc.GenerateAsync(member.Id, null);
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task Consume_Expired_Fails()
    {
        var (ctx, svc, tenantId) = CreateSut();
        var member = SeedMember(ctx, tenantId);
        var gen = await svc.GenerateAsync(member.Id, null);
        var row = await ctx.MemberAppActivationCodes.SingleAsync();
        row.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
        await ctx.SaveChangesAsync();

        var fail = await svc.ConsumeAsync(tenantId, gen.Data!.ActivationCode);
        Assert.False(fail.IsSuccess);
        Assert.Contains("expired", fail.Error!, StringComparison.OrdinalIgnoreCase);
    }
}
