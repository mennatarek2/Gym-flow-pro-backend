namespace HyMotion.Desktop;

public sealed class GymDeskGateTests
{
    [Fact]
    public void LeftoverApiOn5001_IsNotTheGymDesk_SoUserModeMayStart()
    {
        Assert.False(GymDeskGate.IsDeskPort(5001));
        Assert.False(GymDeskGate.DeskAlreadyUp(serviceRunningOrPending: false, port7140Open: false));
        Assert.True(GymDeskGate.ShouldStartUserApi(deskAlreadyUp: false));
    }

    [Fact]
    public void Port7140Occupied_IsTheGymDesk_MustNotStartASecondDesk()
    {
        Assert.True(GymDeskGate.IsDeskPort(7140));
        Assert.True(GymDeskGate.DeskAlreadyUp(serviceRunningOrPending: false, port7140Open: true));
        Assert.False(GymDeskGate.ShouldStartUserApi(deskAlreadyUp: true));
    }

    [Fact]
    public void HyMotionServicePending_IsTheGymDesk_MustNotStartASecondDesk()
    {
        Assert.True(GymDeskGate.DeskAlreadyUp(serviceRunningOrPending: true, port7140Open: false));
        Assert.False(GymDeskGate.ShouldStartUserApi(deskAlreadyUp: true));
    }

    [Fact]
    public void NotInstalled_OnlyWhenExeAndServiceAreBothMissing()
    {
        Assert.True(GymDeskGate.IsNotInstalled(apiExeFound: false, serviceRegistered: false));
        Assert.False(GymDeskGate.IsNotInstalled(apiExeFound: true, serviceRegistered: false));
        Assert.False(GymDeskGate.IsNotInstalled(apiExeFound: false, serviceRegistered: true));
        Assert.True(GymDeskGate.ShowOpenSetup(GymDeskFailKind.NotInstalled));
        Assert.False(GymDeskGate.ShowOpenSetup(GymDeskFailKind.TimedOut));
        Assert.False(GymDeskGate.ShowOpenSetup(GymDeskFailKind.ProcessExited));
    }

    [Fact]
    public void ClassifyAfterWait_HealthOk_Wins()
    {
        Assert.Equal(GymDeskFailKind.None, GymDeskGate.ClassifyAfterWait(
            healthOk: true, startedUserProcess: true, userProcessStillAlive: false));
    }

    [Fact]
    public void ClassifyAfterWait_StartedProcessDied_IsSqlOrStartupExit_NotSetup()
    {
        var kind = GymDeskGate.ClassifyAfterWait(
            healthOk: false, startedUserProcess: true, userProcessStillAlive: false);
        Assert.Equal(GymDeskFailKind.ProcessExited, kind);
        Assert.False(GymDeskGate.ShowOpenSetup(kind));
    }

    [Fact]
    public void ClassifyAfterWait_StillAliveButUnhealthy_IsTimeout_NotSetup()
    {
        var kind = GymDeskGate.ClassifyAfterWait(
            healthOk: false, startedUserProcess: true, userProcessStillAlive: true);
        Assert.Equal(GymDeskFailKind.TimedOut, kind);
        Assert.False(GymDeskGate.ShowOpenSetup(kind));
    }

    [Fact]
    public void ClassifyLaunchMiss_MapsNotInstalledAndStartFailed()
    {
        Assert.Equal(GymDeskFailKind.NotInstalled, GymDeskGate.ClassifyLaunchMiss(UserApiStartKind.NotInstalled));
        Assert.Equal(GymDeskFailKind.ProcessExited, GymDeskGate.ClassifyLaunchMiss(UserApiStartKind.StartFailed));
        Assert.Equal(GymDeskFailKind.TimedOut, GymDeskGate.ClassifyLaunchMiss(UserApiStartKind.DeskAlreadyUp));
    }

    [Theory]
    [InlineData(200, true)]
    [InlineData(204, true)]
    [InlineData(404, true)]
    [InlineData(499, true)]
    [InlineData(301, false)]
    [InlineData(302, false)]
    [InlineData(307, false)]
    [InlineData(500, false)]
    public void HealthUp_Accepts2xxAnd4xx_NotRedirectsOr5xx(int status, bool up)
    {
        Assert.Equal(up, GymDeskGate.IsHealthUp(status));
    }

    [Fact]
    public void HealthUrl_UsesLoopbackNotLocalhost()
    {
        Assert.StartsWith("http://127.0.0.1:7140", GymDeskGate.HealthUrl, StringComparison.Ordinal);
        Assert.DoesNotContain("localhost", GymDeskGate.HealthUrl, StringComparison.OrdinalIgnoreCase);
    }
}
