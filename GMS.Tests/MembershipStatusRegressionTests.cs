namespace GMS.Tests;

using Microsoft.EntityFrameworkCore;
using GMS.Core.Entities;
using GMS.Core.Enums;
using GMS.Core.Utilities;
using GMS.Infrastructure.Persistence;

/// <summary>
/// REM-F8 / check-in integrity regression: membership operational status rules and
/// session semantics that the check-in gauntlet depends on.
/// </summary>
public class MembershipStatusRegressionTests
{
    private static Membership Make(string status, DateOnly start, DateOnly end, int? sessions = null) =>
        new() { Id = Guid.NewGuid(), TenantId = TestData.TenantA, MemberId = Guid.NewGuid(), PlanId = Guid.NewGuid(), Status = status, StartDate = start, EndDate = end, SessionsRemaining = sessions };

    [Fact]
    public void ActiveMembership_Today_IsActive()
    {
        var today = MembershipOperational.TodayCairo();
        Assert.Equal("active", MembershipOperational.GetEffectiveStatus(Make("active", today.AddDays(-1), today.AddDays(1))));
    }

    [Fact]
    public void FutureStart_ActiveRow_ReportsScheduled()
    {
        var today = MembershipOperational.TodayCairo();
        Assert.Equal("scheduled", MembershipOperational.GetEffectiveStatus(Make("active", today.AddDays(1), today.AddDays(10))));
    }

    [Fact]
    public void PastEnd_ActiveRow_ReportsExpired()
    {
        var today = MembershipOperational.TodayCairo();
        Assert.Equal("expired", MembershipOperational.GetEffectiveStatus(Make("active", today.AddDays(-10), today.AddDays(-1))));
    }

    [Fact]
    public void FrozenNotExpired_StaysFrozen_FrozenPastEnd_IsExpired()
    {
        var today = MembershipOperational.TodayCairo();
        Assert.Equal("frozen", MembershipOperational.GetEffectiveStatus(Make("frozen", today.AddDays(-5), today.AddDays(5))));
        Assert.Equal("expired", MembershipOperational.GetEffectiveStatus(Make("frozen", today.AddDays(-10), today.AddDays(-1))));
    }

    [Fact]
    public void CancelledAndPending_PassThrough()
    {
        Assert.Equal("cancelled", MembershipOperational.GetEffectiveStatus(Make("cancelled", DateOnly.MinValue, DateOnly.MaxValue)));
        Assert.Equal("pending", MembershipOperational.GetEffectiveStatus(Make("pending", DateOnly.MinValue, DateOnly.MaxValue)));
    }

    [Fact]
    public void CheckinEligible_RequiresExactDateWindow()
    {
        var today = MembershipOperational.TodayCairo();
        Assert.True(MembershipOperational.IsCheckinEligible(Make("active", today, today)));
        Assert.False(MembershipOperational.IsCheckinEligible(Make("active", today.AddDays(1), today.AddDays(2))));
        Assert.False(MembershipOperational.IsCheckinEligible(Make("pending", today, today.AddDays(2))));
    }
}
