// HyMotion Local Lifetime Edition - browser launcher.
//
// Double-clicked from the Desktop/Start Menu shortcut. Does NOT run the application itself (that
// is the "HyMotion" Windows Service, installed separately - see scripts/local-install) - this is
// only a thin, reliable way to get the gym owner from "double-click an icon" to "dashboard open in
// their browser" without ever showing a console/PowerShell/dotnet window, per the Phase 3
// "Desktop Experience" and "Browser Launcher" requirements. Deliberately NOT a GUI framework:
// System.Windows.Forms is referenced only for MessageBox on the failure path below.

using System.Diagnostics;
using System.ServiceProcess;

const string ServiceName = "HyMotion";
const string LocalUrl = "http://localhost:7140";
const string HealthUrl = LocalUrl + "/health";
var readinessTimeout = TimeSpan.FromSeconds(30);
var pollInterval = TimeSpan.FromMilliseconds(500);

TryStartServiceIfStopped();

if (await WaitUntilHealthyAsync(readinessTimeout, pollInterval))
{
    OpenBrowser(LocalUrl);
    return;
}

System.Windows.Forms.MessageBox.Show(
    "HyMotion did not respond within 30 seconds.\n\n" +
    "The HyMotion service may still be starting, or may have failed to start.\n" +
    "Check %ProgramData%\\HyMotion\\logs for details, or open Services.msc and look for \"HyMotion\".",
    "HyMotion",
    System.Windows.Forms.MessageBoxButtons.OK,
    System.Windows.Forms.MessageBoxIcon.Warning);

static void TryStartServiceIfStopped()
{
    try
    {
        using var sc = new ServiceController(ServiceName);
        if (sc.Status == ServiceControllerStatus.Stopped)
        {
            sc.Start();
        }
    }
    catch
    {
        // Service not installed, or the current user can't control it (starting a service
        // requires appropriate rights) - fall through and just poll /health; if the service is
        // already running under another account this is a no-op anyway.
    }
}

static async Task<bool> WaitUntilHealthyAsync(TimeSpan timeout, TimeSpan interval)
{
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
    var deadline = DateTime.UtcNow + timeout;

    while (DateTime.UtcNow < deadline)
    {
        try
        {
            var response = await http.GetAsync(HealthUrl);
            if (response.IsSuccessStatusCode)
                return true;
        }
        catch
        {
            // Not up yet (connection refused / DNS / timeout) - keep polling until the deadline.
        }

        await Task.Delay(interval);
    }

    return false;
}

static void OpenBrowser(string url)
{
    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
}
