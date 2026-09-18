namespace GMS.Core.Configuration;

/// <summary>
/// Single source of truth for where a Local Edition install keeps its mutable runtime data.
/// Default is %ProgramData%\HyMotion (Windows-service installs). Per-user Setup writes
/// hymotion-data-dir.txt next to GMS.Api.exe and/or sets HYMOTION_DATA_DIR so a normal
/// Windows user can keep config, license, and logs under %LOCALAPPDATA%\HyMotion.
/// Never used by SaaS.
/// </summary>
public static class LocalRuntimePaths
{
    /// <summary>Escape hatch for tests and for the desktop Launcher when it starts GMS.Api
    /// as the signed-in Windows user. Unset in Windows-service installs — those keep using
    /// %ProgramData%\HyMotion.</summary>
    private const string RootOverrideEnvVar = "HYMOTION_DATA_DIR";
    private const string DataDirMarkerFile = "hymotion-data-dir.txt";

    public static string Root
    {
        get
        {
            if (Environment.GetEnvironmentVariable(RootOverrideEnvVar) is { Length: > 0 } overridden)
                return overridden;

            try
            {
                var marker = Path.Combine(AppContext.BaseDirectory, DataDirMarkerFile);
                if (File.Exists(marker))
                {
                    var line = File.ReadAllText(marker).Trim();
                    if (line.Length > 0) return line;
                }
            }
            catch
            {
                // fall through to ProgramData
            }

            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "HyMotion");
        }
    }

    public static string ConfigDir => Path.Combine(Root, "config");
    public static string UploadsDir => Path.Combine(Root, "uploads");
    public static string SecretsDir => Path.Combine(Root, "secrets");
    public static string LogsDir => Path.Combine(Root, "logs");

    /// <summary>One folder per backup (database.bak + uploads.zip + manifest.json), written and
    /// read by scripts/local-install/backup/*.ps1. The API only ever reads manifests here for the
    /// Backup Health surface — it never writes backups itself (that stays in the scripts, so the
    /// same identity/permission model — NETWORK SERVICE, no sysadmin — is exercised by both the
    /// Scheduled Task and any manual "Backup Now" trigger).</summary>
    public static string BackupsDir => Path.Combine(Root, "Backups");

    /// <summary>Optional machine-specific config overrides (e.g. a discovered SQL Server connection
    /// string) an installer/setup script may write here. Never committed to source control.</summary>
    public static string ConfigOverrideFile => Path.Combine(ConfigDir, "appsettings.json");

    /// <summary>Backup-system settings (enabled/retention/off-PC destination) - written by
    /// Register-BackupTask.ps1, editable later via the Backup &amp; Recovery admin page.</summary>
    public static string BackupConfigFile => Path.Combine(ConfigDir, "backup-config.json");

    /// <summary>DPAPI-protected (LocalMachine scope, same convention as LocalSecretProvider) file
    /// holding this installation's identity (InstallationId + machine fingerprint) and the most
    /// recently verified signed license payload. See LocalLicenseStore.</summary>
    public static string LicenseFile => Path.Combine(SecretsDir, "local-license.dat");

    /// <summary>DPAPI-protected (LocalMachine scope) keep-signed-in refresh credential for this
    /// gym PC. Separate from <see cref="LicenseFile"/> so logout never touches gym identity.
    /// Never stores a password. See LocalDeviceSessionStore.</summary>
    public static string DeviceSessionFile => Path.Combine(SecretsDir, "device-session.dat");

    /// <summary>DPAPI-protected Local Owner recovery request state. Never stores a password or
    /// Platform private key. See LocalOwnerRecoveryStore.</summary>
    public static string OwnerRecoveryFile => Path.Combine(SecretsDir, "owner-recovery.dat");

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(ConfigDir);
        Directory.CreateDirectory(UploadsDir);
        Directory.CreateDirectory(SecretsDir);
        Directory.CreateDirectory(LogsDir);
    }
}
