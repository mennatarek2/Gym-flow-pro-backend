namespace GMS.Tests;

using GMS.Application.Services;
using GMS.Core.Constants;

public class AttendanceCalculatorTests
{
    private static readonly DateOnly Day = new(2026, 9, 1);

    [Fact]
    public void ComputeCheckIn_OnTimeWithinGrace_IsPresent()
    {
        var shiftStart = new TimeOnly(9, 0);
        var shiftStartUtc = AttendanceCalculator.ComputeShiftStartUtc(Day, shiftStart);
        var checkIn = shiftStartUtc.AddMinutes(5); // within 10-minute grace

        var (lateMinutes, status) = AttendanceCalculator.ComputeCheckIn(checkIn, Day, shiftStart, graceMinutes: 10);

        Assert.Equal(0, lateMinutes);
        Assert.Equal(AttendanceStatuses.Present, status);
    }

    [Fact]
    public void ComputeCheckIn_PastGrace_IsLateWithCorrectMinutes()
    {
        var shiftStart = new TimeOnly(9, 0);
        var shiftStartUtc = AttendanceCalculator.ComputeShiftStartUtc(Day, shiftStart);
        var checkIn = shiftStartUtc.AddMinutes(15); // 10 min grace + 5 min late

        var (lateMinutes, status) = AttendanceCalculator.ComputeCheckIn(checkIn, Day, shiftStart, graceMinutes: 10);

        Assert.Equal(5, lateMinutes);
        Assert.Equal(AttendanceStatuses.Late, status);
    }

    [Fact]
    public void ComputeCheckIn_NoShiftAssigned_IsAlwaysPresent()
    {
        var (lateMinutes, status) = AttendanceCalculator.ComputeCheckIn(DateTime.UtcNow, Day, null, graceMinutes: 10);

        Assert.Equal(0, lateMinutes);
        Assert.Equal(AttendanceStatuses.Present, status);
    }

    [Fact]
    public void ComputeCheckOut_NormalShift_ComputesWorkedAndOvertime()
    {
        var shiftStart = new TimeOnly(9, 0);
        var shiftEnd = new TimeOnly(17, 0);
        var checkInAtUtc = AttendanceCalculator.ComputeShiftStartUtc(Day, shiftStart);
        var shiftEndUtc = AttendanceCalculator.ComputeShiftEndUtc(Day, shiftStart, shiftEnd);
        var checkOutAtUtc = shiftEndUtc.AddMinutes(45);

        var result = AttendanceCalculator.ComputeCheckOut(checkInAtUtc, checkOutAtUtc, Day, shiftStart, shiftEnd);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(8 * 60 + 45, result.Data.WorkedMinutes);
        Assert.Equal(45, result.Data.OvertimeMinutes);
    }

    [Fact]
    public void ComputeCheckOut_MidnightCrossingShift_RollsEndToNextDay()
    {
        var shiftStart = new TimeOnly(16, 0); // Evening 16:00 -> 00:00
        var shiftEnd = new TimeOnly(0, 0);
        var checkInAtUtc = AttendanceCalculator.ComputeShiftStartUtc(Day, shiftStart);
        var shiftEndUtc = AttendanceCalculator.ComputeShiftEndUtc(Day, shiftStart, shiftEnd);

        // The shift crosses midnight, so its end must land on the next calendar day.
        Assert.True(shiftEndUtc > checkInAtUtc);
        Assert.True((shiftEndUtc - checkInAtUtc).TotalHours is > 7 and < 9);

        var checkOutAtUtc = shiftEndUtc.AddMinutes(10);
        var result = AttendanceCalculator.ComputeCheckOut(checkInAtUtc, checkOutAtUtc, Day, shiftStart, shiftEnd);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(10, result.Data.OvertimeMinutes);
    }

    [Fact]
    public void ComputeCheckOut_NoShiftAssigned_NoOvertimeButWorkedStillComputed()
    {
        var checkInAtUtc = DateTime.UtcNow;
        var checkOutAtUtc = checkInAtUtc.AddHours(3);

        var result = AttendanceCalculator.ComputeCheckOut(checkInAtUtc, checkOutAtUtc, Day, null, null);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(180, result.Data.WorkedMinutes);
        Assert.Equal(0, result.Data.OvertimeMinutes);
    }

    [Fact]
    public void ComputeCheckOut_CheckoutNotAfterCheckin_Fails()
    {
        var now = DateTime.UtcNow;

        var equal = AttendanceCalculator.ComputeCheckOut(now, now, Day, null, null);
        var before = AttendanceCalculator.ComputeCheckOut(now, now.AddMinutes(-1), Day, null, null);

        Assert.False(equal.IsSuccess);
        Assert.False(before.IsSuccess);
    }
}
