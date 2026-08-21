namespace GMS.Tests;

using GMS.Core.Entities;
using GMS.Core.Utilities;

public class MembershipOperationalTests
{
    private static readonly DateOnly Today = new(2026, 8, 18);

    private static Membership Row(string status, DateOnly start, DateOnly end, Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        Status = status,
        StartDate = start,
        EndDate = end
    };

    [Fact]
    public void SelectCoveringToday_IgnoresCancelledEvenWhenDatesStillCover()
    {
        var cancelled = Row("cancelled", Today.AddDays(-10), Today.AddDays(20));
        var covering = MembershipOperational.SelectCoveringToday(new[] { cancelled }, Today);
        Assert.Null(covering);
        Assert.False(MembershipOperational.IsCoveringToday(cancelled, Today));
    }

    [Fact]
    public void SelectCoveringToday_PrefersNewActiveOverRefundedHistorical()
    {
        var refunded = Row("cancelled", Today.AddDays(-10), Today.AddDays(20));
        var current = Row("active", Today, Today.AddDays(30));

        var covering = MembershipOperational.SelectCoveringToday(new[] { refunded, current }, Today);
        Assert.Equal(current.Id, covering!.Id);
    }

    [Fact]
    public void SelectOperational_AfterRefundAndRenew_ReturnsNewActiveNotCancelled()
    {
        var refunded = Row("cancelled", Today.AddDays(-10), Today.AddDays(25));
        var current = Row("active", Today, Today.AddDays(30));

        var selected = MembershipOperational.SelectOperational(new[] { refunded, current }, Today);
        Assert.Equal(current.Id, selected!.Id);
        Assert.Equal("active", MembershipOperational.GetEffectiveStatus(selected, Today));
    }

    [Fact]
    public void SelectOperational_RefundOnly_FallsBackToCancelledForDisplay()
    {
        var refunded = Row("cancelled", Today.AddDays(-10), Today.AddDays(20));
        var selected = MembershipOperational.SelectOperational(new[] { refunded }, Today);
        Assert.Equal(refunded.Id, selected!.Id);
        Assert.Equal("cancelled", MembershipOperational.GetEffectiveStatus(selected, Today));
    }
}
