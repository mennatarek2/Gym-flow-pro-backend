namespace HyMotion.Desktop;

/// <summary>
/// Gym desk = HyMotion service starting/running, or TCP :7140. A leftover GMS.Api on another
/// port (dev :5001) is not the desk and must not block starting Local :7140.
/// </summary>
internal enum GymDeskFailKind
{
    None,
    NotInstalled,
    TimedOut,
    ProcessExited,
}

internal enum UserApiStartKind
{
    DeskAlreadyUp,
    Started,
    NotInstalled,
    StartFailed,
}

internal static class GymDeskGate
{
    public const int Port = 7140;
    public const string BaseUrl = "http://127.0.0.1:7140";
    public const string HealthUrl = "http://127.0.0.1:7140/health";
    public const string SetupStatusUrl = "http://127.0.0.1:7140/api/local-setup/status";
    public const string LoginUrl = "http://127.0.0.1:7140/auth/login/";
    public const string FirstRunUrl = "http://127.0.0.1:7140/auth/local-setup/";

    public static bool IsDeskPort(int port) => port == Port;

    public static bool DeskAlreadyUp(bool serviceRunningOrPending, bool port7140Open) =>
        serviceRunningOrPending || port7140Open;

    public static bool ShouldStartUserApi(bool deskAlreadyUp) => !deskAlreadyUp;

    public static bool IsNotInstalled(bool apiExeFound, bool serviceRegistered) =>
        !apiExeFound && !serviceRegistered;

    public static GymDeskFailKind ClassifyAfterWait(
        bool healthOk,
        bool startedUserProcess,
        bool userProcessStillAlive)
    {
        if (healthOk) return GymDeskFailKind.None;
        if (startedUserProcess && !userProcessStillAlive) return GymDeskFailKind.ProcessExited;
        return GymDeskFailKind.TimedOut;
    }

    public static GymDeskFailKind ClassifyLaunchMiss(UserApiStartKind kind) => kind switch
    {
        UserApiStartKind.NotInstalled => GymDeskFailKind.NotInstalled,
        UserApiStartKind.StartFailed => GymDeskFailKind.ProcessExited,
        _ => GymDeskFailKind.TimedOut,
    };

    public static bool ShowOpenSetup(GymDeskFailKind kind) => kind == GymDeskFailKind.NotInstalled;

    public static bool IsHealthUp(int statusCode) =>
        statusCode is >= 200 and < 300 or >= 400 and < 500;
}
