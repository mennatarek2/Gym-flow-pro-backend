using System.Diagnostics;
using System.Net.NetworkInformation;
using System.ServiceProcess;
using Microsoft.Win32;

namespace HyMotion.Desktop;

internal static class Native
{
    public static bool Is64Bit => Environment.Is64BitOperatingSystem;

    public static bool IsAdministrator()
    {
        using var id = System.Security.Principal.WindowsIdentity.GetCurrent();
        var p = new System.Security.Principal.WindowsPrincipal(id);
        return p.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }

    public static string CurrentWindowsAccount()
    {
        using var id = System.Security.Principal.WindowsIdentity.GetCurrent();
        return id.Name;
    }

    public static string ReadDataRoot(string appDir)
    {
        try
        {
            var marker = Path.Combine(appDir, Desk.DataDirMarkerFile);
            if (File.Exists(marker))
            {
                var line = File.ReadAllText(marker).Trim();
                if (line.Length > 0) return line;
            }
        }
        catch
        {
            // use per-user data
        }
        return Desk.UserDataDir;
    }

    public static void WriteDataRootMarker(string appDir, string dataRoot)
    {
        Directory.CreateDirectory(appDir);
        Directory.CreateDirectory(dataRoot);
        Directory.CreateDirectory(Path.Combine(dataRoot, "config"));
        File.WriteAllText(Path.Combine(appDir, Desk.DataDirMarkerFile), dataRoot);
    }

    /// <summary>
    /// Prefer %ProgramData%\HyMotion when it already exists (Windows service / SQL backups).
    /// A second data dir under LocalAppData hides backups and makes the gym look like it has no HA.
    /// </summary>
    public static string ResolveDataDir()
    {
        if (Directory.Exists(Desk.MachineDataDir))
            return Desk.MachineDataDir;
        try
        {
            Directory.CreateDirectory(Desk.MachineDataDir);
            Directory.CreateDirectory(Path.Combine(Desk.MachineDataDir, "config"));
            return Desk.MachineDataDir;
        }
        catch
        {
            return Desk.UserDataDir;
        }
    }

    public static bool GymDeskAlreadyRunning()
    {
        var status = ServiceStatus();
        var serviceUp = status is ServiceControllerStatus.Running or ServiceControllerStatus.StartPending;
        return GymDeskGate.DeskAlreadyUp(serviceUp, IsDeskPortOpen());
    }

    public static bool IsDeskPortOpen()
    {
        try
        {
            foreach (var ep in IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners())
            {
                if (GymDeskGate.IsDeskPort(ep.Port)) return true;
            }
        }
        catch
        {
            // fall through to health wait
        }
        return false;
    }

    public static bool TryStartUserApi()
    {
        var launch = TryLaunchUserApi();
        return launch.Kind is UserApiStartKind.DeskAlreadyUp or UserApiStartKind.Started;
    }

    public static UserApiLaunch TryLaunchUserApi()
    {
        if (!GymDeskGate.ShouldStartUserApi(GymDeskAlreadyRunning()))
            return new UserApiLaunch(UserApiStartKind.DeskAlreadyUp, null);

        var exe = AppFiles.FindApiExe(
            AppContext.BaseDirectory,
            Path.GetDirectoryName(Environment.ProcessPath),
            Desk.UserInstallDir,
            ServiceImageDir());
        if (exe == null)
        {
            return ServiceStatus() != null
                ? new UserApiLaunch(UserApiStartKind.StartFailed, null)
                : new UserApiLaunch(UserApiStartKind.NotInstalled, null);
        }

        var dir = Path.GetDirectoryName(exe)!;
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = dir,
        };
        psi.Environment["ASPNETCORE_ENVIRONMENT"] = "Local";
        psi.Environment["HYMOTION_DATA_DIR"] = ResolveDataDir();
        var process = Process.Start(psi);
        return process == null
            ? new UserApiLaunch(UserApiStartKind.StartFailed, null)
            : new UserApiLaunch(UserApiStartKind.Started, process);
    }

    public static void RegisterLogonKeepAlive(string launcherExe)
    {
        if (string.IsNullOrWhiteSpace(launcherExe) || !File.Exists(launcherExe)) return;
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "schtasks.exe"),
                Arguments = "/Create /TN \"HyMotion Keep Alive\" /TR \"\\\"" + launcherExe + "\\\" --headless\" /SC ONLOGON /F",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(20000);
        }
        catch
        {
            // logon task is best-effort; the desktop icon still starts HyMotion
        }
    }

    public static string? ServiceImageDir()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + Desk.ServiceName);
            var raw = key?.GetValue("ImagePath") as string;
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var path = raw.Trim().Trim('"');
            if (path.Contains(" -", StringComparison.Ordinal))
                path = path.Split(new[] { " -" }, 2, StringSplitOptions.None)[0].Trim().Trim('"');
            var dir = Path.GetDirectoryName(path);
            return Directory.Exists(dir) ? dir : null;
        }
        catch
        {
            return null;
        }
    }

    public static ServiceControllerStatus? ServiceStatus()
    {
        try
        {
            using var sc = new ServiceController(Desk.ServiceName);
            return sc.Status;
        }
        catch
        {
            return null;
        }
    }

    public static void TryStartService()
    {
        using var sc = new ServiceController(Desk.ServiceName);
        if (sc.Status == ServiceControllerStatus.Running) return;
        if (sc.Status == ServiceControllerStatus.StartPending)
        {
            sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(40));
            return;
        }
        sc.Start();
        sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(40));
    }

    /// <summary>
    /// Stops the HyMotion Windows service and any leftover GMS.Api so Setup can overwrite files.
    /// Returns false if the desk is still running (usually because this process is not elevated).
    /// </summary>
    public static bool TryStopGymDesk()
    {
        try
        {
            using var sc = new ServiceController(Desk.ServiceName);
            if (sc.Status != ServiceControllerStatus.Stopped && sc.Status != ServiceControllerStatus.StopPending)
            {
                sc.Stop();
            }
            if (sc.Status != ServiceControllerStatus.Stopped)
                sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(45));
        }
        catch
        {
            // no service, or this Windows user cannot stop it
        }

        try
        {
            foreach (var p in Process.GetProcessesByName("GMS.Api"))
            {
                try
                {
                    p.Kill(entireProcessTree: true);
                    p.WaitForExit(15000);
                }
                catch
                {
                    // skip processes we cannot signal
                }
            }
        }
        catch
        {
            // ignore
        }

        return !GymDeskAlreadyRunning();
    }

    public static void RelaunchThisElevated(string arguments = "")
    {
        var exe = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "HyMotionSetup.exe");
        Process.Start(new ProcessStartInfo
        {
            FileName = exe,
            Arguments = arguments ?? "",
            UseShellExecute = true,
            Verb = "runas",
        });
    }

    public static (int Code, string Output) RunPowerShell(string scriptPath, string arguments, int timeoutMs = 180000)
    {
        if (!File.Exists(scriptPath))
            return (2, "Script not found: " + scriptPath);

        var psi = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"),
            Arguments = "-NoProfile -ExecutionPolicy Bypass -File \"" + scriptPath + "\" " + arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(scriptPath) ?? Environment.CurrentDirectory,
        };
        using var p = Process.Start(psi);
        if (p == null) return (1, "Could not start PowerShell.");
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        if (!p.WaitForExit(timeoutMs))
        {
            try { p.Kill(entireProcessTree: true); } catch { /* ignore */ }
            return (1, "Timed out.");
        }
        return (p.ExitCode, (stdout + Environment.NewLine + stderr).Trim());
    }

    public static void CreateShortcuts(string launcherExe, string? backupExe = null)
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var programs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "HyMotion");
        Directory.CreateDirectory(programs);
        if (!string.IsNullOrWhiteSpace(desktop) && Directory.Exists(desktop))
            WriteShortcut(Path.Combine(desktop, "HyMotion.lnk"), launcherExe);
        WriteShortcut(Path.Combine(programs, "HyMotion.lnk"), launcherExe);
        if (!string.IsNullOrWhiteSpace(backupExe) && File.Exists(backupExe))
            WriteShortcut(Path.Combine(programs, "HyMotion Backup.lnk"), backupExe, "HyMotion Backup");
        TryRegisterBackupProtocol();
    }

    public static string? BackupExePath()
    {
        var dirs = new List<string?>
        {
            Path.GetDirectoryName(Environment.ProcessPath),
            AppContext.BaseDirectory,
            ServiceImageDir(),
        };
        foreach (var dir in dirs)
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            var p = Path.Combine(dir, "HyMotionBackup.exe");
            if (File.Exists(p)) return p;
        }
        return null;
    }

    public static void TryRegisterBackupProtocol()
    {
        try
        {
            var exe = BackupExePath();
            if (string.IsNullOrWhiteSpace(exe)) return;
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + BackupStore.ProtocolName);
            if (key == null) return;
            key.SetValue("", "URL:HyMotion Backup");
            key.SetValue("URL Protocol", "");
            using var cmd = key.CreateSubKey(@"shell\open\command");
            cmd?.SetValue("", "\"" + exe + "\" \"%1\"");
        }
        catch
        {
            // protocol is optional; Start Menu shortcut still works
        }
    }

    public static void RelaunchElevated(string arguments)
    {
        var exe = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "HyMotionBackup.exe");
        Process.Start(new ProcessStartInfo
        {
            FileName = exe,
            Arguments = arguments,
            UseShellExecute = true,
            Verb = "runas",
        });
    }

    static void WriteShortcut(string lnkPath, string target, string description = "HyMotion")
    {
        var type = Type.GetTypeFromProgID("WScript.Shell");
        if (type == null) throw new InvalidOperationException("WScript.Shell is not available.");
        dynamic shell = Activator.CreateInstance(type)!;
        dynamic sc = shell.CreateShortcut(lnkPath);
        sc.TargetPath = target;
        sc.WorkingDirectory = Path.GetDirectoryName(target);
        sc.WindowStyle = 1;
        sc.Description = description;
        sc.IconLocation = target;
        sc.Save();
    }

    public static void OpenBrowser(string url)
    {
        Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
    }

    public static async Task<bool> WaitForHealthAsync(TimeSpan timeout)
    {
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(3) };
        var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until)
        {
            try
            {
                using var res = await http.GetAsync(GymDeskGate.HealthUrl);
                if (GymDeskGate.IsHealthUp((int)res.StatusCode)) return true;
            }
            catch
            {
                // still starting
            }
            await Task.Delay(800);
        }
        return false;
    }

    public static async Task<bool> NeedsFirstRunAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            using var res = await http.GetAsync(Desk.SetupStatusUrl);
            if (!res.IsSuccessStatusCode) return false;
            var json = await res.Content.ReadAsStringAsync();
            return json.IndexOf("\"isCompleted\":false", StringComparison.OrdinalIgnoreCase) >= 0
                || json.IndexOf("\"isCompleted\": false", StringComparison.OrdinalIgnoreCase) >= 0;
        }
        catch
        {
            return false;
        }
    }
}

internal readonly record struct UserApiLaunch(UserApiStartKind Kind, Process? Process);
