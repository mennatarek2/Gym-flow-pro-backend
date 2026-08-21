using GMS.Application.Services;
using GMS.Core.Constants;
using GMS.Core.Entities;

namespace GMS.Tests;

public class MembershipRenewalDatingTests
{
    private static readonly DateOnly Today = new(2026, 8, 6);

    private static MembershipPlan MonthlyPlan(int durationDays = 30) => new()
    {
        Name = "Monthly",
        NameAr = "شهري",
        PlanType = "monthly",
        DurationDays = durationDays,
        Price = 500m,
        IsActive = true
    };

    private static MembershipPlan DayPass() => new()
    {
        Name = "Day Pass",
        NameAr = "يوم",
        PlanType = "day_pass",
        DurationDays = 1,
        Price = 50m,
        IsActive = true
    };

    private static Membership Covering(DateOnly end) => new()
    {
        Status = "active",
        StartDate = Today.AddDays(-10),
        EndDate = end
    };

    [Fact]
    public void CancelAndSwitch_WhenCovering_StartsTodayFullDuration()
    {
        var covering = Covering(Today.AddDays(12));
        var (start, end) = MembershipRenewalDating.Calculate(
            covering, MonthlyPlan(30), PlanTransitionModes.CancelAndSwitch, Today);

        Assert.Equal(Today, start);
        Assert.Equal(Today.AddDays(30), end);
    }

    [Fact]
    public void QueueNext_WhenCovering_StartsDayAfterPriorEnd()
    {
        var covering = Covering(Today.AddDays(12));
        var (start, end) = MembershipRenewalDating.Calculate(
            covering, MonthlyPlan(30), PlanTransitionModes.QueueNext, Today);

        Assert.Equal(Today.AddDays(13), start);
        Assert.Equal(Today.AddDays(13).AddDays(30), end);
    }

    [Fact]
    public void ManualRollover_WhenCovering_ExtendsFromPriorEnd()
    {
        var covering = Covering(Today.AddDays(12));
        var (start, end) = MembershipRenewalDating.Calculate(
            covering, MonthlyPlan(30), PlanTransitionModes.ManualRollover, Today);

        Assert.Equal(Today, start);
        Assert.Equal(Today.AddDays(12).AddDays(30), end);
    }

    [Fact]
    public void ExpiredGap_IgnoresMode_RestartsToday()
    {
        var expired = new Membership
        {
            Status = "expired",
            StartDate = Today.AddDays(-40),
            EndDate = Today.AddDays(-5)
        };

        foreach (var mode in new[]
                 {
                     PlanTransitionModes.CancelAndSwitch,
                     PlanTransitionModes.QueueNext,
                     PlanTransitionModes.ManualRollover
                 })
        {
            var (start, end) = MembershipRenewalDating.Calculate(
                expired, MonthlyPlan(30), mode, Today);
            Assert.Equal(Today, start);
            Assert.Equal(Today.AddDays(30), end);
        }
    }

    [Fact]
    public void DayPass_IsSingleCairoDay_RegardlessOfMode()
    {
        var covering = Covering(Today.AddDays(5));
        var (start, end) = MembershipRenewalDating.Calculate(
            covering, DayPass(), PlanTransitionModes.QueueNext, Today);

        Assert.Equal(Today, start);
        Assert.Equal(Today, end);
    }

    [Fact]
    public void IsCoveringToday_RequiresStartOnOrBeforeToday()
    {
        var inWindow = Covering(Today.AddDays(12));
        var scheduledOnly = new Membership
        {
            Status = "active",
            StartDate = Today.AddDays(5),
            EndDate = Today.AddDays(35)
        };

        Assert.True(MembershipRenewalDating.IsCoveringToday(inWindow, Today));
        Assert.False(MembershipRenewalDating.IsCoveringToday(scheduledOnly, Today));
        Assert.False(MembershipRenewalDating.IsCoveringToday(
            new Membership { Status = "cancelled", StartDate = Today.AddDays(-5), EndDate = Today.AddDays(20) },
            Today));
    }

    [Fact]
    public void QueueNext_IgnoresFutureScheduled_RestartsWhenNothingInWindow()
    {
        var scheduledOnly = new Membership
        {
            Status = "active",
            StartDate = Today.AddDays(5),
            EndDate = Today.AddDays(35)
        };

        var (start, end) = MembershipRenewalDating.Calculate(
            scheduledOnly, MonthlyPlan(30), PlanTransitionModes.QueueNext, Today);

        Assert.Equal(Today, start);
        Assert.Equal(Today.AddDays(30), end);
    }

    [Fact]
    public void ApplyPriorOpenHandling_CancelAndSwitch_ClipsEndDate()
    {
        var prior = Covering(Today.AddDays(10));
        var neu = new Membership { Status = "active", StartDate = Today, EndDate = Today.AddDays(30) };

        MembershipRenewalDating.ApplyPriorOpenHandling(
            new[] { prior, neu }, neu.Id, PlanTransitionModes.CancelAndSwitch, Today, apply: true);

        Assert.Equal("expired", prior.Status);
        Assert.Equal(Today, prior.EndDate);
    }

    [Fact]
    public void ApplyPriorOpenHandling_QueueNext_LeavesPriorUntouched()
    {
        var prior = Covering(Today.AddDays(10));
        var originalEnd = prior.EndDate;
        var neu = new Membership
        {
            Status = "active",
            StartDate = prior.EndDate.AddDays(1),
            EndDate = prior.EndDate.AddDays(31)
        };

        MembershipRenewalDating.ApplyPriorOpenHandling(
            new[] { prior, neu }, neu.Id, PlanTransitionModes.QueueNext, Today, apply: true);

        Assert.Equal("active", prior.Status);
        Assert.Equal(originalEnd, prior.EndDate);
    }

    [Fact]
    public void ApplyPriorOpenHandling_ManualRollover_ExpiresWithoutClip()
    {
        var prior = Covering(Today.AddDays(10));
        var originalEnd = prior.EndDate;
        var neu = new Membership { Status = "active", StartDate = Today, EndDate = originalEnd.AddDays(30) };

        MembershipRenewalDating.ApplyPriorOpenHandling(
            new[] { prior, neu }, neu.Id, PlanTransitionModes.ManualRollover, Today, apply: true);

        Assert.Equal("expired", prior.Status);
        Assert.Equal(originalEnd, prior.EndDate);
    }

    [Fact]
    public void ApplyPriorOpenHandling_SkippedWhenNotApply()
    {
        var prior = Covering(Today.AddDays(10));
        var neu = new Membership { Status = "pending", StartDate = Today, EndDate = Today.AddDays(30) };

        MembershipRenewalDating.ApplyPriorOpenHandling(
            new[] { prior, neu }, neu.Id, PlanTransitionModes.CancelAndSwitch, Today, apply: false);

        Assert.Equal("active", prior.Status);
        Assert.Equal(Today.AddDays(10), prior.EndDate);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("cancel_and_switch", true)]
    [InlineData("QUEUE_NEXT", true)]
    [InlineData("manual_rollover", true)]
    [InlineData("bogus", false)]
    public void TryNormalize_ValidatesModes(string? raw, bool ok)
    {
        Assert.Equal(ok, PlanTransitionModes.TryNormalize(raw, out _));
    }
}
