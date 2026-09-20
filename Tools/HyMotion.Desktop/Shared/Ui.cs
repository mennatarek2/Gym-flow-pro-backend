namespace HyMotion.Desktop;

internal static class Ui
{
    public static bool Arabic { get; set; } =
        System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar";

    public static string T(string en, string ar) => Arabic ? ar : en;

    public static readonly Color Bg = Color.FromArgb(243, 245, 242);
    public static readonly Color Card = Color.FromArgb(255, 254, 251);
    public static readonly Color Line = Color.FromArgb(228, 232, 226);
    public static readonly Color Text = Color.FromArgb(31, 36, 32);
    public static readonly Color Mute = Color.FromArgb(122, 132, 124);
    public static readonly Color Lime = Color.FromArgb(122, 204, 0);
    public static readonly Color LimeDark = Color.FromArgb(94, 175, 0);
    public static readonly Color Danger = Color.FromArgb(185, 28, 28);
    public static readonly Color Ok = Color.FromArgb(22, 101, 52);
}

internal static class Desk
{
    public const string BaseUrl = GymDeskGate.BaseUrl;
    public const string HealthUrl = GymDeskGate.HealthUrl;
    public const string SetupStatusUrl = GymDeskGate.SetupStatusUrl;
    public const string LoginUrl = GymDeskGate.LoginUrl;
    public const string FirstRunUrl = GymDeskGate.FirstRunUrl;
    public const string ServiceName = "HyMotion";
    public const string DataDirMarkerFile = "hymotion-data-dir.txt";

    public static string UserInstallDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HyMotion", "app");

    public static string UserDataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HyMotion");

    public static string DefaultInstallDir => UserInstallDir;

    public static string MachineDataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "HyMotion");
}
