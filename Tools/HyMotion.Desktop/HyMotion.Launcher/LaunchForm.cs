namespace HyMotion.Desktop;

sealed class LaunchForm : Form
{
    readonly Label _title;
    readonly Label _status;
    readonly Button _retry;
    readonly Button _setup;
    readonly bool _headless;

    public LaunchForm(bool headless = false)
    {
        _headless = headless;
        Text = "HyMotion";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(440, 210);
        if (headless)
        {
            ShowInTaskbar = false;
            Opacity = 0;
            WindowState = FormWindowState.Minimized;
        }
        BackColor = Ui.Bg;
        Font = new Font("Segoe UI", 10f);
        RightToLeft = Ui.Arabic ? RightToLeft.Yes : RightToLeft.No;
        RightToLeftLayout = Ui.Arabic;

        _title = new Label
        {
            AutoSize = false,
            Bounds = new Rectangle(24, 22, 392, 32),
            Font = new Font("Segoe UI", 16f, FontStyle.Bold),
            ForeColor = Ui.Text,
            Text = Ui.T("Opening HyMotion…", "بيفتح HyMotion…"),
        };
        _status = new Label
        {
            AutoSize = false,
            Bounds = new Rectangle(24, 62, 392, 70),
            ForeColor = Ui.Mute,
            Text = Ui.T("Starting the gym desk on this PC.", "بيشغّل مكتب النادي على الجهاز."),
        };
        _retry = LimeButton(Ui.T("Try again", "حاول تاني"), 24, 148);
        _retry.Visible = false;
        _retry.Click += async (_, _) => await StartAsync();
        _setup = GhostButton(Ui.T("Open Setup", "افتح الإعداد"), 230, 148);
        _setup.Visible = false;
        _setup.Click += (_, _) => OpenSetup();

        Controls.Add(_title);
        Controls.Add(_status);
        Controls.Add(_retry);
        Controls.Add(_setup);
        Shown += async (_, _) => await StartAsync();
    }

    async Task StartAsync()
    {
        _retry.Visible = false;
        _setup.Visible = false;
        _status.ForeColor = Ui.Mute;
        _status.Text = Ui.T("Starting the gym desk on this PC.", "بيشغّل مكتب النادي على الجهاز.");
        try
        {
            if (await Native.WaitForHealthAsync(TimeSpan.FromSeconds(2)))
            {
                await OpenDeskAndClose();
                return;
            }

            try
            {
                if (Native.ServiceStatus() != null)
                    Native.TryStartService();
            }
            catch
            {
                // A normal Windows user cannot start a stopped service. User-mode API is the path.
            }

            if (await Native.WaitForHealthAsync(TimeSpan.FromSeconds(8)))
            {
                await OpenDeskAndClose();
                return;
            }

            if (Native.GymDeskAlreadyRunning())
            {
                if (await Native.WaitForHealthAsync(TimeSpan.FromSeconds(20)))
                {
                    await OpenDeskAndClose();
                    return;
                }
            }

            var launch = Native.TryLaunchUserApi();
            if (launch.Kind is UserApiStartKind.NotInstalled or UserApiStartKind.StartFailed)
            {
                Fail(MessageFor(GymDeskGate.ClassifyLaunchMiss(launch.Kind)), GymDeskGate.ClassifyLaunchMiss(launch.Kind));
                return;
            }

            _status.Text = Ui.T("Waiting for the desk to be ready…", "مستنيين المكتب يبقى جاهز…");
            var ok = await Native.WaitForHealthAsync(TimeSpan.FromSeconds(45));
            if (ok)
            {
                await OpenDeskAndClose();
                return;
            }

            var started = launch.Kind == UserApiStartKind.Started && launch.Process != null;
            var alive = started && !launch.Process!.HasExited;
            var kind = GymDeskGate.ClassifyAfterWait(healthOk: false, started, alive);
            Fail(MessageFor(kind), kind);
        }
        catch (Exception ex)
        {
            Fail(ex.Message, GymDeskFailKind.TimedOut);
        }
    }

    void Fail(string message, GymDeskFailKind kind)
    {
        if (_headless)
        {
            Close();
            return;
        }

        _title.Text = Ui.T("Could not open HyMotion", "ما قدرناش نفتح HyMotion");
        _status.ForeColor = Ui.Danger;
        _status.Text = message;
        _retry.Visible = true;
        _setup.Visible = GymDeskGate.ShowOpenSetup(kind);
    }

    static string MessageFor(GymDeskFailKind kind) => kind switch
    {
        GymDeskFailKind.NotInstalled => Ui.T(
            "HyMotion is not installed for this Windows user. Double-click HyMotion Setup first.",
            "HyMotion مش متثبت للمستخدم ده. دبل كليك HyMotion Setup الأول."),
        GymDeskFailKind.ProcessExited => Ui.T(
            "HyMotion could not start. SQL Server may still be starting. Try again — you do not need to set up the gym again.",
            "HyMotion ما قدرش يبدأ. SQL Server ممكن لسه بيفتح. حاول تاني — مش محتاج تجهّز النادي تاني."),
        _ => Ui.T(
            "HyMotion did not open in time. Try again — you do not need to set up the gym again.",
            "HyMotion ما فتحش في الوقت. حاول تاني — مش محتاج تجهّز النادي تاني."),
    };

    async Task OpenDeskAndClose()
    {
        if (_headless)
        {
            Close();
            return;
        }
        var firstRun = await Native.NeedsFirstRunAsync();
        Native.OpenBrowser(firstRun ? Desk.FirstRunUrl : Desk.LoginUrl);
        Close();
    }

    void OpenSetup()
    {
        var dir = AppContext.BaseDirectory;
        var setup = Path.Combine(dir, "HyMotionSetup.exe");
        if (File.Exists(setup))
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = setup,
                UseShellExecute = true,
            });
            return;
        }
        MessageBox.Show(
            Ui.T("HyMotionSetup.exe was not found next to the launcher.", "ملف الإعداد مش جنب البرنامج."),
            "HyMotion", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    static Button LimeButton(string text, int x, int y) => new()
    {
        Text = text,
        Bounds = new Rectangle(x, y, 190, 40),
        BackColor = Ui.Lime,
        ForeColor = Color.FromArgb(13, 13, 13),
        FlatStyle = FlatStyle.Flat,
        Font = new Font("Segoe UI", 10f, FontStyle.Bold),
    };

    static Button GhostButton(string text, int x, int y)
    {
        var b = LimeButton(text, x, y);
        b.BackColor = Ui.Card;
        b.ForeColor = Ui.Text;
        b.FlatAppearance.BorderColor = Ui.Line;
        return b;
    }
}
