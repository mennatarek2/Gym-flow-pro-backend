namespace HyMotion.Desktop;

sealed class SetupForm : Form
{
    int _step;
    readonly Label _h1;
    readonly Label _sub;
    readonly Label _log;
    readonly Button _back;
    readonly Button _next;
    readonly Button _lang;
    readonly Panel[] _steps;
    readonly Label[] _pills;

    Label _pcStatus = null!;
    Label _sqlStatus = null!;
    TextBox _installDir = null!;
    Label _sourceLabel = null!;
    CheckBox _shortcut = null!;

    string? _sourceDir;
    readonly bool _autoUpdate;

    public SetupForm(bool autoUpdate = false)
    {
        _autoUpdate = autoUpdate;
        Text = Ui.T("HyMotion Setup", "إعداد HyMotion");
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        ClientSize = new Size(640, 520);
        BackColor = Ui.Bg;
        Font = new Font("Segoe UI", 10f);
        ApplyRtl();

        var rail = new Panel { Bounds = new Rectangle(0, 0, 168, 520), BackColor = Color.FromArgb(13, 13, 13) };
        _pills = new Label[4];
        var names = new[]
        {
            Ui.T("1  This PC", "1  الجهاز"),
            Ui.T("2  Database", "2  القاعدة"),
            Ui.T("3  Install", "3  التثبيت"),
            Ui.T("4  Finish", "4  خلّص"),
        };
        for (var i = 0; i < 4; i++)
        {
            _pills[i] = new Label
            {
                AutoSize = false,
                Bounds = new Rectangle(12, 28 + i * 44, 144, 36),
                ForeColor = Color.FromArgb(176, 176, 176),
                TextAlign = ContentAlignment.MiddleLeft,
                Text = names[i],
            };
            rail.Controls.Add(_pills[i]);
        }
        var brand = new Label
        {
            AutoSize = false,
            Bounds = new Rectangle(12, 460, 144, 40),
            ForeColor = Ui.Lime,
            Font = new Font("Segoe UI", 11f, FontStyle.Bold),
            Text = "HyMotion",
        };
        rail.Controls.Add(brand);

        _h1 = new Label
        {
            AutoSize = false,
            Bounds = new Rectangle(188, 18, 420, 34),
            Font = new Font("Segoe UI", 16f, FontStyle.Bold),
            ForeColor = Ui.Text,
        };
        _sub = new Label
        {
            AutoSize = false,
            Bounds = new Rectangle(188, 54, 428, 48),
            ForeColor = Ui.Mute,
        };
        _lang = new Button
        {
            Bounds = new Rectangle(556, 18, 60, 28),
            FlatStyle = FlatStyle.Flat,
            Text = Ui.Arabic ? "EN" : "AR",
            ForeColor = Ui.Text,
            BackColor = Ui.Card,
        };
        _lang.FlatAppearance.BorderColor = Ui.Line;
        _lang.Click += (_, _) =>
        {
            Ui.Arabic = !Ui.Arabic;
            Close();
            Application.Restart();
        };

        _steps = new[] { BuildPc(), BuildSql(), BuildInstall(), BuildDone() };
        foreach (var p in _steps)
        {
            p.Bounds = new Rectangle(188, 108, 428, 300);
            p.Visible = false;
            Controls.Add(p);
        }

        _log = new Label
        {
            AutoSize = false,
            Bounds = new Rectangle(188, 412, 428, 40),
            ForeColor = Ui.Mute,
        };
        _back = Ghost(Ui.T("Back", "رجوع"), 188, 460);
        _next = Lime(Ui.T("Continue", "كمّل"), 426, 460);
        _back.Click += (_, _) => ShowStep(_step - 1);
        _next.Click += async (_, _) => await OnNext();

        Controls.Add(rail);
        Controls.Add(_h1);
        Controls.Add(_sub);
        Controls.Add(_lang);
        Controls.Add(_log);
        Controls.Add(_back);
        Controls.Add(_next);
        ShowStep(0);
        Shown += async (_, _) =>
        {
            RefreshPc();
            if (!_autoUpdate) return;
            ShowStep(2);
            await InstallAsync();
        };
    }

    void ApplyRtl()
    {
        RightToLeft = Ui.Arabic ? RightToLeft.Yes : RightToLeft.No;
        RightToLeftLayout = Ui.Arabic;
    }

    Panel BuildPc()
    {
        var p = new Panel { BackColor = Ui.Bg };
        _pcStatus = Body(p, 0);
        return p;
    }

    Panel BuildSql()
    {
        var p = new Panel { BackColor = Ui.Bg };
        _sqlStatus = Body(p, 0);
        var check = Lime(Ui.T("Check SQL", "افحص SQL"), 0, 210);
        var write = Ghost(Ui.T("Create gym database", "أنشئ قاعدة النادي"), 200, 210);
        check.Click += (_, _) => CheckSql(writeConfig: false);
        write.Click += (_, _) => CheckSql(writeConfig: true);
        p.Controls.Add(check);
        p.Controls.Add(write);
        return p;
    }

    Panel BuildInstall()
    {
        var p = new Panel { BackColor = Ui.Bg };
        _sourceLabel = Body(p, 0);
        _sourceLabel.Height = 70;
        var destLbl = new Label
        {
            AutoSize = false,
            Bounds = new Rectangle(0, 78, 428, 22),
            ForeColor = Ui.Mute,
            Text = Ui.T("Install folder", "مجلد التثبيت"),
        };
        _installDir = new TextBox
        {
            Bounds = new Rectangle(0, 102, 428, 32),
            Text = Desk.DefaultInstallDir,
        };
        _shortcut = new CheckBox
        {
            Bounds = new Rectangle(0, 148, 428, 28),
            Checked = true,
            ForeColor = Ui.Text,
            Text = Ui.T("Create a desktop icon", "اعمل أيقونة على سطح المكتب"),
        };
        p.Controls.Add(destLbl);
        p.Controls.Add(_installDir);
        p.Controls.Add(_shortcut);
        return p;
    }

    Panel BuildDone()
    {
        var p = new Panel { BackColor = Ui.Bg };
        var done = Body(p, 0);
        done.Name = "doneBody";
        done.Height = 160;
        var open = Lime(Ui.T("Open HyMotion", "افتح HyMotion"), 0, 210);
        open.Click += async (_, _) => await OpenDesk();
        p.Controls.Add(open);
        return p;
    }

    static Label Body(Panel p, int y)
    {
        var l = new Label
        {
            AutoSize = false,
            Bounds = new Rectangle(0, y, 428, 190),
            ForeColor = Ui.Text,
        };
        p.Controls.Add(l);
        return l;
    }

    void ShowStep(int i)
    {
        _step = Math.Clamp(i, 0, 3);
        for (var n = 0; n < _steps.Length; n++)
        {
            _steps[n].Visible = n == _step;
            _pills[n].ForeColor = n == _step ? Ui.Lime : Color.FromArgb(176, 176, 176);
            _pills[n].Font = new Font("Segoe UI", 10f, n == _step ? FontStyle.Bold : FontStyle.Regular);
        }
        _back.Visible = _step > 0 && _step < 3;
        switch (_step)
        {
            case 0:
                _h1.Text = Ui.T("Install HyMotion on this PC", "ثبّت HyMotion على الجهاز");
                _sub.Text = Ui.T(
                    "Same as any desktop app: Next, desktop icon, then open the gym.",
                    "زي أي برنامج كمبيوتر: كمّل، أيقونة على سطح المكتب، وبعدين افتح النادي.");
                _next.Text = Ui.T("Continue", "كمّل");
                RefreshPc();
                break;
            case 1:
                _h1.Text = Ui.T("Gym database (SQL)", "قاعدة النادي (SQL)");
                _sub.Text = Ui.T(
                    "HyMotion does not include SQL Server. Install Express with the Microsoft wizard if it is missing, then create the gym database here.",
                    "HyMotion مش شايل SQL. ثبّت Express من معالج مايكروسوفت لو ناقص، وبعدين أنشئ قاعدة النادي من هنا.");
                _next.Text = Ui.T("Continue", "كمّل");
                break;
            case 2:
                _h1.Text = Ui.T("Install and desktop icon", "التثبيت وأيقونة سطح المكتب");
                _sub.Text = Ui.T(
                    "Files are copied into this Windows user's folder, and a desktop icon is created. Administrator is not required.",
                    "الملفات بتتتنسخ في مجلد المستخدم ده، وأيقونة سطح المكتب بتتعمل. مش محتاج صلاحية مسؤول.");
                _next.Text = Ui.T("Install", "ثبّت");
                RefreshSource();
                break;
            default:
                _h1.Text = Ui.T("HyMotion is ready", "HyMotion جاهز");
                _sub.Text = Ui.T("Double-click the desktop icon any time. First run asks for the license and owner account.", "اضغط أيقونة سطح المكتب في أي وقت. أول تشغيل يطلب الترخيص وحساب المالك.");
                _next.Text = Ui.T("Close", "قفل");
                break;
        }
        _log.Text = "";
    }

    void RefreshPc()
    {
        _sourceDir = Path.GetDirectoryName(AppFiles.FindApiExe(
            AppContext.BaseDirectory,
            Directory.GetParent(AppContext.BaseDirectory)?.FullName,
            Native.ServiceImageDir(),
            Desk.DefaultInstallDir));
        var bits = Native.Is64Bit
            ? Ui.T("Windows is 64-bit.", "ويندوز 64 بت.")
            : Ui.T("This PC is not 64-bit. HyMotion Local needs 64-bit Windows.", "الجهاز مش 64 بت. HyMotion المحلي محتاج ويندوز 64.");
        var who = Ui.T(
            "This Windows user can install HyMotion. Administrator is not required.",
            "المستخدم ده يقدر يثبّت HyMotion. مش محتاج صلاحية مسؤول.");
        var src = _sourceDir != null
            ? Ui.T("HyMotion files found:\n", "ملفات HyMotion موجودة:\n") + _sourceDir
            : Ui.T("HyMotion files were not found next to Setup. Put Setup next to GMS.Api.exe (the USB app folder).", "ملفات HyMotion مش جنب الإعداد. حط Setup جنب GMS.Api.exe (مجلد التطبيق على الـ USB).");
        var svc = Native.ServiceStatus();
        var svcLine = svc == null
            ? Ui.T("HyMotion is not installed yet — that is OK.", "HyMotion لسه مش متثبت — ده طبيعي.")
            : Ui.T("HyMotion is already on this PC (" + svc + "). Setup can add the desktop icon for this Windows user.", "HyMotion موجود على الجهاز (" + svc + "). الإعداد يقدر يضيف الأيقونة للمستخدم ده.");
        _pcStatus.Text = bits + "\n" + who + "\n\n" + src + "\n\n" + svcLine;
    }

    void RefreshSource()
    {
        _sourceLabel.Text = _sourceDir == null
            ? Ui.T("No GMS.Api.exe found. Go back and put Setup next to the app folder.", "GMS.Api.exe مش موجود. ارجع حط الإعداد جنب مجلد التطبيق.")
            : Ui.T("Copy from:\n", "هننسخ من:\n") + _sourceDir;
        if (string.IsNullOrWhiteSpace(_installDir.Text))
            _installDir.Text = Desk.DefaultInstallDir;
    }

    async Task OnNext()
    {
        if (_step == 0)
        {
            if (!Native.Is64Bit || _sourceDir == null)
            {
                _log.ForeColor = Ui.Danger;
                _log.Text = Ui.T("Fix the items on this page first.", "صلّح النقاط في الصفحة دي الأول.");
                RefreshPc();
                return;
            }
            ShowStep(1);
            return;
        }
        if (_step == 1)
        {
            ShowStep(2);
            return;
        }
        if (_step == 2)
        {
            await InstallAsync();
            return;
        }
        Close();
    }

    void CheckSql(bool writeConfig)
    {
        var script = AppFiles.FindScript("Test-SqlServerAvailability.ps1",
            AppContext.BaseDirectory, _sourceDir, Native.ServiceImageDir(), _installDir.Text);
        if (script == null)
        {
            _sqlStatus.ForeColor = Ui.Danger;
            _sqlStatus.Text = Ui.T(
                "SQL check script was not found. Put Test-SqlServerAvailability.ps1 in install-scripts next to the app.",
                "سكربت فحص SQL مش موجود. حطه في install-scripts جنب التطبيق.");
            return;
        }
        _sqlStatus.ForeColor = Ui.Mute;
        _sqlStatus.Text = Ui.T("Checking SQL…", "بيفحص SQL…");
        Refresh();
        var account = Native.CurrentWindowsAccount();
        var args = writeConfig
            ? "-WriteConfig -ServiceAccount \"" + account + "\" -ConfigRoot \"" + Desk.UserDataDir + "\""
            : "";
        var (code, output) = Native.RunPowerShell(script, args);
        if (code == 0)
        {
            _sqlStatus.ForeColor = Ui.Ok;
            _sqlStatus.Text = writeConfig
                ? Ui.T("Gym database is ready for this Windows user. Continue.", "قاعدة النادي جاهزة للمستخدم ده. كمّل.")
                : Ui.T("SQL Server was found on this PC. Click Create gym database, then Continue.", "SQL Server موجود على الجهاز. اضغط أنشئ قاعدة النادي، وبعدين كمّل.");
        }
        else
        {
            _sqlStatus.ForeColor = Ui.Danger;
            _sqlStatus.Text = Ui.T(
                "SQL Server Express is missing or not ready.\nInstall it with the Microsoft wizard (USB folder 01-sql-express), then Check SQL again.\n\nhttps://www.microsoft.com/en-us/sql-server/sql-server-downloads",
                "SQL Server Express مش جاهز.\nثبّته من معالج مايكروسوفت (مجلد 01-sql-express على الـ USB)، وبعدين افحص SQL تاني.");
        }
        _log.ForeColor = Ui.Mute;
        _log.Text = Truncate(output, 180);
    }

    async Task InstallAsync()
    {
            var dest = _installDir.Text.Trim();
            var liveDir = Native.ServiceImageDir();
            if (!string.IsNullOrWhiteSpace(liveDir))
                dest = liveDir;

            if (string.IsNullOrWhiteSpace(dest) || _sourceDir == null)
            {
                _log.ForeColor = Ui.Danger;
                _log.Text = Ui.T("Choose an install folder.", "اختار مجلد التثبيت.");
                return;
            }
            _next.Enabled = false;
            _back.Enabled = false;
            try
            {
                _log.ForeColor = Ui.Mute;
                _log.Text = Ui.T("Installing…", "بيثبّت…");
                Refresh();

                if (Native.GymDeskAlreadyRunning() && !Native.IsAdministrator())
                {
                    _log.Text = Ui.T(
                        "HyMotion is already running. Windows will ask for permission so Setup can replace it.",
                        "HyMotion شغال. ويندوز هيسأل عن صلاحية عشان الإعداد يقدر يبدّله.");
                    Refresh();
                    Native.RelaunchThisElevated("--update");
                    Close();
                    return;
                }

                if (Native.GymDeskAlreadyRunning() && !Native.TryStopGymDesk())
                {
                    throw new InvalidOperationException(Ui.T(
                        "Could not stop HyMotion. Right-click HyMotionSetup.exe and choose Run as administrator.",
                        "مش قادرين نوقف HyMotion. كليك يمين على HyMotionSetup.exe واختَر تشغيل كمسؤول."));
                }

                var same = PathsEqual(_sourceDir, dest);
                if (!same)
                {
                    _log.Text = Ui.T("Copying files…", "بينسخ الملفات…");
                    Refresh();
                    AppFiles.CopyApp(_sourceDir, dest);
                }

            EnsureScripts(dest);
            Native.WriteDataRootMarker(dest, Native.ResolveDataDir());

            if (Native.IsAdministrator())
            {
                var existingDir = Native.ServiceImageDir();
                var alreadyHere = existingDir != null && PathsEqual(existingDir, dest)
                    && Native.ServiceStatus() is System.ServiceProcess.ServiceControllerStatus.Running
                        or System.ServiceProcess.ServiceControllerStatus.StartPending;
                if (!alreadyHere)
                {
                    var install = AppFiles.FindScript("install-service.ps1", dest, _sourceDir, AppContext.BaseDirectory);
                    if (install != null)
                    {
                        _log.Text = Ui.T("Registering HyMotion service…", "بتتسجّل خدمة HyMotion…");
                        Refresh();
                        var svc = Native.RunPowerShell(install, "-InstallDir \"" + dest + "\"", 240000);
                        if (svc.Code != 0)
                            throw new InvalidOperationException(Truncate(svc.Output, 400));
                    }
                }

                var nightlyAdmin = AppFiles.FindScript("Register-BackupTask.ps1", dest, _sourceDir);
                if (nightlyAdmin != null)
                {
                    _log.Text = Ui.T("Turning on nightly save…", "بيشغّل الحفظ الليلي…");
                    Refresh();
                    Native.RunPowerShell(nightlyAdmin, "-InstallDir \"" + dest + "\"");
                }
            }
            else
            {
                var nightlyUser = AppFiles.FindScript("Register-BackupTask.ps1", dest, _sourceDir);
                if (nightlyUser != null)
                {
                    _log.Text = Ui.T("Turning on nightly save…", "بيشغّل الحفظ الليلي…");
                    Refresh();
                    Native.RunPowerShell(nightlyUser, "-InstallDir \"" + dest + "\" -AllowCurrentUser");
                }
            }

            var launcher = Path.Combine(dest, "HyMotionLauncher.exe");
            if (!File.Exists(launcher))
                launcher = Path.Combine(dest, "GMS.Api.exe");
            var backupExe = Path.Combine(dest, "HyMotionBackup.exe");
            if (_shortcut.Checked)
                Native.CreateShortcuts(launcher, File.Exists(backupExe) ? backupExe : null);
            Native.RegisterLogonKeepAlive(launcher);

            try { Native.TryStartService(); }
            catch { Native.TryStartUserApi(); }

            var done = _steps[3].Controls.Find("doneBody", false).OfType<Label>().FirstOrDefault();
            if (done != null)
            {
                done.ForeColor = Ui.Ok;
                done.Text = Ui.T(
                    "Installed.\nDesktop icon: HyMotion\nOpen it to set up the gym (license + owner) or to sign in.",
                    "اتثبت.\nأيقونة سطح المكتب: HyMotion\nافتحها لتجهيز النادي (ترخيص + مالك) أو لتسجيل الدخول.");
            }
            ShowStep(3);
            _log.ForeColor = Ui.Ok;
            _log.Text = Ui.T("Done.", "تم.");
        }
        catch (Exception ex)
        {
            _log.ForeColor = Ui.Danger;
            _log.Text = ex.Message;
        }
        finally
        {
            _next.Enabled = true;
            _back.Enabled = true;
        }
        await Task.CompletedTask;
    }

    void EnsureScripts(string dest)
    {
        var dir = Path.Combine(dest, "install-scripts");
        Directory.CreateDirectory(dir);
        Directory.CreateDirectory(Path.Combine(dir, "backup"));
        foreach (var name in new[] { "install-service.ps1", "uninstall-service.ps1", "Test-SqlServerAvailability.ps1" })
        {
            var found = AppFiles.FindScript(name, _sourceDir, AppContext.BaseDirectory, dest);
            if (found == null) continue;
            var target = Path.Combine(dir, name);
            if (!PathsEqual(found, target)) File.Copy(found, target, true);
        }
        foreach (var name in new[] { "BackupCommon.ps1", "Backup-HyMotion.ps1", "Restore-HyMotion.ps1", "Register-BackupTask.ps1" })
        {
            var found = AppFiles.FindScript(name, _sourceDir, AppContext.BaseDirectory, dest);
            if (found == null) continue;
            var target = Path.Combine(dir, "backup", name);
            if (!PathsEqual(found, target)) File.Copy(found, target, true);
        }
        var launcherSrc = Path.Combine(_sourceDir!, "HyMotionLauncher.exe");
        var launcherDest = Path.Combine(dest, "HyMotionLauncher.exe");
        if (File.Exists(launcherSrc) && !PathsEqual(launcherSrc, launcherDest))
            File.Copy(launcherSrc, launcherDest, true);
        var setupSrc = Path.Combine(_sourceDir!, "HyMotionSetup.exe");
        var setupDest = Path.Combine(dest, "HyMotionSetup.exe");
        if (File.Exists(setupSrc) && !PathsEqual(setupSrc, setupDest))
            File.Copy(setupSrc, setupDest, true);
        var backupSrc = Path.Combine(_sourceDir!, "HyMotionBackup.exe");
        var backupDest = Path.Combine(dest, "HyMotionBackup.exe");
        if (File.Exists(backupSrc) && !PathsEqual(backupSrc, backupDest))
            File.Copy(backupSrc, backupDest, true);
    }

    async Task OpenDesk()
    {
        try { Native.TryStartService(); } catch { /* user-mode API next */ }
        if (!Native.GymDeskAlreadyRunning())
            Native.TryStartUserApi();
        _log.Text = Ui.T("Opening…", "بيفتح…");
        var ok = await Native.WaitForHealthAsync(TimeSpan.FromSeconds(45));
        if (!ok)
        {
            _log.ForeColor = Ui.Danger;
            _log.Text = Ui.T("Desk did not start yet. Use the desktop icon in a moment.", "المكتب لسه ما فتحش. استخدم أيقونة سطح المكتب بعد لحظات.");
            return;
        }
        var first = await Native.NeedsFirstRunAsync();
        Native.OpenBrowser(first ? Desk.FirstRunUrl : Desk.LoginUrl);
    }

    static bool PathsEqual(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    static string Truncate(string s, int n) => string.IsNullOrEmpty(s) ? "" : (s.Length <= n ? s : s[..n] + "…");

    static Button Lime(string text, int x, int y)
    {
        var b = new Button
        {
            Text = text,
            Bounds = new Rectangle(x, y, 190, 40),
            BackColor = Ui.Lime,
            ForeColor = Color.FromArgb(13, 13, 13),
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
        };
        b.FlatAppearance.BorderSize = 0;
        return b;
    }

    static Button Ghost(string text, int x, int y)
    {
        var b = Lime(text, x, y);
        b.BackColor = Ui.Card;
        b.ForeColor = Ui.Text;
        b.FlatAppearance.BorderColor = Ui.Line;
        return b;
    }
}
