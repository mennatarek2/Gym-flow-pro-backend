namespace HyMotion.Desktop;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        ApplicationConfiguration.Initialize();
        Native.TryRegisterBackupProtocol();

        var launch = BackupStore.Parse(args);
        if (launch.Mode == BackupMode.Verify)
            return BackupStore.RunVerify(launch);
        if (launch.Mode == BackupMode.CopyLive)
            return BackupStore.RunCopyLive(launch);

        if (launch.Mode == BackupMode.Restore && !Native.IsAdministrator())
        {
            try
            {
                Native.RelaunchElevated(launch.ToProcessArgs() + " --elevated");
                return 0;
            }
            catch
            {
                MessageBox.Show(
                    Ui.T(
                        "Windows must allow HyMotion Backup as Administrator to restore.",
                        "ويندوز لازم يسمح لـ HyMotion Backup كمسؤول عشان الاستعادة."),
                    "HyMotion",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return 1;
            }
        }

        Application.Run(new BackupForm(launch));
        return 0;
    }
}
