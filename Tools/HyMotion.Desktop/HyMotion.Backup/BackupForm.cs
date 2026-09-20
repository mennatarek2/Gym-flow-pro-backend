namespace HyMotion.Desktop;

sealed class BackupForm : Form
{
    readonly BackupLaunch _launch;
    readonly Label _title;
    readonly Label _status;
    readonly Panel _home;
    readonly Panel _usb;
    readonly Panel _restore;
    readonly ComboBox _usbDrives = new();
    readonly ComboBox _usbCopies = new();
    readonly ComboBox _restoreCopies = new();
    readonly TextBox _restoreType = new();
    readonly CheckBox _restoreAck = new();
    readonly TextBox _log = new();
    readonly Button _copyUsb;
    readonly Button _doRestore;
    bool _busy;

    public BackupForm(BackupLaunch launch)
    {
        _launch = launch;
        Text = Ui.T("HyMotion Backup", "نسخ HyMotion");
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        ClientSize = new Size(520, 430);
        BackColor = Ui.Bg;
        Font = new Font("Segoe UI", 10f);
        RightToLeft = Ui.Arabic ? RightToLeft.Yes : RightToLeft.No;
        RightToLeftLayout = Ui.Arabic;

        _title = new Label
        {
            AutoSize = false,
            Bounds = new Rectangle(24, 18, 472, 32),
            Font = new Font("Segoe UI", 16f, FontStyle.Bold),
            ForeColor = Ui.Text,
        };
        _status = new Label
        {
            AutoSize = false,
            Bounds = new Rectangle(24, 52, 472, 40),
            ForeColor = Ui.Mute,
        };

        _home = BuildHome();
        _usb = BuildUsb();
        _restore = BuildRestore();
        _copyUsb = Lime(Ui.T("Save to this USB", "احفظ على الـ USB دي"), 24, 372);
        _copyUsb.Click += async (_, _) => await CopyUsbAsync();
        _doRestore = Danger(Ui.T("Restore this copy", "استعيد النسخة دي"), 24, 372);
        _doRestore.Click += async (_, _) => await RestoreAsync();

        Controls.Add(_title);
        Controls.Add(_status);
        Controls.Add(_home);
        Controls.Add(_usb);
        Controls.Add(_restore);
        Controls.Add(_copyUsb);
        Controls.Add(_doRestore);
        Shown += async (_, _) =>
        {
            ShowMode(_launch.Mode == BackupMode.Home ? BackupMode.Home : _launch.Mode);
            if (_launch.Mode == BackupMode.Usb
                && _copyUsb.Enabled
                && _usbDrives.Items.Count == 1
                && _usbCopies.SelectedIndex >= 0)
            {
                await CopyUsbAsync();
            }
        };
    }

    Panel BuildHome()
    {
        var p = new Panel { Bounds = new Rectangle(24, 100, 472, 250), BackColor = Ui.Bg };
        var usb = Lime(Ui.T("Save a copy to USB", "احفظ نسخة على USB"), 0, 8);
        usb.Width = 472;
        usb.Click += (_, _) => ShowMode(BackupMode.Usb);
        var restore = Ghost(Ui.T("Restore this gym", "استعيد النادي"), 0, 60);
        restore.Width = 472;
        restore.Click += (_, _) =>
        {
            if (!Native.IsAdministrator())
            {
                Native.RelaunchElevated("--restore" + IdArg(_launch.BackupId));
                Close();
                return;
            }
            ShowMode(BackupMode.Restore);
        };
        p.Controls.Add(usb);
        p.Controls.Add(restore);
        return p;
    }

    Panel BuildUsb()
    {
        var p = new Panel { Bounds = new Rectangle(24, 100, 472, 250), BackColor = Ui.Bg };
        p.Controls.Add(Lbl(Ui.T("USB stick", "الفلاشة"), 0, 0));
        _usbDrives.Bounds = new Rectangle(0, 24, 472, 28);
        _usbDrives.DropDownStyle = ComboBoxStyle.DropDownList;
        p.Controls.Add(_usbDrives);
        p.Controls.Add(Lbl(Ui.T("Copy to save", "النسخة"), 0, 64));
        _usbCopies.Bounds = new Rectangle(0, 88, 472, 28);
        _usbCopies.DropDownStyle = ComboBoxStyle.DropDownList;
        p.Controls.Add(_usbCopies);
        var look = Ghost(Ui.T("Look again", "دور تاني"), 0, 130);
        look.Click += (_, _) => FillUsb();
        var back = Ghost(Ui.T("Back", "رجوع"), 250, 130);
        back.Click += (_, _) => ShowMode(BackupMode.Home);
        p.Controls.Add(look);
        p.Controls.Add(back);
        return p;
    }

    Panel BuildRestore()
    {
        var p = new Panel { Bounds = new Rectangle(24, 100, 472, 260), BackColor = Ui.Bg };
        p.Controls.Add(Lbl(Ui.T("Copy to restore", "النسخة للاستعادة"), 0, 0));
        _restoreCopies.Bounds = new Rectangle(0, 24, 472, 28);
        _restoreCopies.DropDownStyle = ComboBoxStyle.DropDownList;
        p.Controls.Add(_restoreCopies);
        _restoreAck.AutoSize = false;
        _restoreAck.Bounds = new Rectangle(0, 64, 472, 40);
        _restoreAck.Text = Ui.T(
            "I understand this replaces today’s gym data.",
            "فاهم إن ده هيبدّل بيانات النادي النهارده.");
        p.Controls.Add(_restoreAck);
        p.Controls.Add(Lbl(Ui.T("Type RESTORE to continue", "اكتب RESTORE عشان نكمّل"), 0, 108));
        _restoreType.Bounds = new Rectangle(0, 132, 220, 28);
        p.Controls.Add(_restoreType);
        _log.Bounds = new Rectangle(0, 168, 472, 78);
        _log.Multiline = true;
        _log.ReadOnly = true;
        _log.ScrollBars = ScrollBars.Vertical;
        _log.BackColor = Ui.Card;
        p.Controls.Add(_log);
        return p;
    }

    void ShowMode(BackupMode mode)
    {
        _home.Visible = mode == BackupMode.Home;
        _usb.Visible = mode == BackupMode.Usb;
        _restore.Visible = mode == BackupMode.Restore;
        _copyUsb.Visible = mode == BackupMode.Usb;
        _doRestore.Visible = mode == BackupMode.Restore;

        if (mode == BackupMode.Home)
        {
            _title.Text = Ui.T("Backup of your gym", "نسخة النادي");
            _status.Text = Ui.T(
                "Save a copy onto a USB, or restore this PC from a saved copy.",
                "احفظ نسخة على USB، أو استعيد الجهاز من نسخة محفوظة.");
            _status.ForeColor = Ui.Mute;
        }
        else if (mode == BackupMode.Usb)
        {
            _title.Text = Ui.T("Save to USB", "احفظ على USB");
            FillUsb();
        }
        else
        {
            _title.Text = Ui.T("Restore this gym", "استعيد النادي");
            _status.Text = Ui.T(
                "The desk will be offline for a few minutes. Nobody should sell or check in.",
                "المكتب هيقف دقايق. محدش يبيع أو يسجّل حضور.");
            _status.ForeColor = Ui.Danger;
            FillRestore();
        }
    }

    void FillUsb()
    {
        _usbDrives.Items.Clear();
        foreach (var d in BackupStore.CandidateUsbDrives())
            _usbDrives.Items.Add(new DriveItem(d, LabelDrive(d)));
        if (_usbDrives.Items.Count > 0)
        {
            var best = 0;
            long bestFree = -1;
            for (var i = 0; i < _usbDrives.Items.Count; i++)
            {
                try
                {
                    var free = ((_usbDrives.Items[i] as DriveItem)?.Drive.AvailableFreeSpace) ?? 0;
                    if (free > bestFree) { bestFree = free; best = i; }
                }
                catch { /* ignore */ }
            }
            _usbDrives.SelectedIndex = best;
        }

        _usbCopies.Items.Clear();
        var local = BackupStore.ListLocal().Where(b => b.CanRestore).ToList();
        foreach (var c in local)
            _usbCopies.Items.Add(new CopyItem(c, Label(c)));
        SelectCopy(_usbCopies, _launch.BackupId);
        if (_usbCopies.SelectedIndex < 0 && _usbCopies.Items.Count > 0)
        {
            var newest = BackupStore.NewestUsable(local);
            SelectCopy(_usbCopies, newest?.Id);
            if (_usbCopies.SelectedIndex < 0) _usbCopies.SelectedIndex = 0;
        }

        if (_usbDrives.Items.Count == 0)
        {
            _status.ForeColor = Ui.Danger;
            _status.Text = Ui.T(
                "No USB yet. Plug it in, wait a few seconds, then Look again. It should appear in File Explorer as a drive letter.",
                "مفيش USB ظاهر. وصّله، استنى ثواني، وبعدين اضغط دور تاني. المفروض يظهر حرف قرص في مستكشف الملفات.");
            _copyUsb.Enabled = false;
        }
        else if (_usbCopies.Items.Count == 0)
        {
            _status.ForeColor = Ui.Danger;
            _status.Text = Ui.T("No usable copy on this PC yet. Save a copy in HyMotion first.", "مفيش نسخة تتنفع على الجهاز. احفظ نسخة من HyMotion الأول.");
            _copyUsb.Enabled = false;
        }
        else
        {
            _status.ForeColor = Ui.Mute;
            _status.Text = Ui.T("Pick the USB and the copy, then save.", "اختار الفلاشة والنسخة، وبعدين احفظ.");
            _copyUsb.Enabled = true;
        }
    }

    void FillRestore()
    {
        _restoreCopies.Items.Clear();
        foreach (var c in BackupStore.ListRestoreChoices())
            _restoreCopies.Items.Add(new CopyItem(c, Label(c)));
        SelectCopy(_restoreCopies, _launch.BackupId);
        if (_restoreCopies.SelectedIndex < 0 && _restoreCopies.Items.Count > 0)
            _restoreCopies.SelectedIndex = 0;
        _doRestore.Enabled = _restoreCopies.Items.Count > 0;
        if (_restoreCopies.Items.Count == 0)
        {
            _status.ForeColor = Ui.Danger;
            _status.Text = Ui.T(
                "No usable copy on this PC or USB. Plug in the USB that has HyMotionBackups, or save a copy first.",
                "مفيش نسخة تتنفع على الجهاز أو USB. وصل الفلاشة اللي فيها HyMotionBackups، أو احفظ نسخة الأول.");
        }
    }

    async Task CopyUsbAsync()
    {
        if (_busy) return;
        var drive = (_usbDrives.SelectedItem as DriveItem)?.Drive;
        var copy = (_usbCopies.SelectedItem as CopyItem)?.Copy;
        if (drive == null || copy == null) return;
        _busy = true;
        _copyUsb.Enabled = false;
        _status.ForeColor = Ui.Mute;
        _status.Text = Ui.T("Copying onto the USB…", "بننسخ على الـ USB…");
        try
        {
            var dest = await Task.Run(() => BackupStore.CopyToUsb(copy, drive));
            _status.ForeColor = Ui.Ok;
            _status.Text = Ui.T("Saved on the USB. Keep the stick off this PC.", "اتحفظت على الـ USB. سيب الفلاشة برا الجهاز.")
                + "\n" + dest;
        }
        catch (Exception ex)
        {
            _status.ForeColor = Ui.Danger;
            _status.Text = OwnerError(ex);
        }
        finally
        {
            _busy = false;
            _copyUsb.Enabled = true;
        }
    }

    async Task RestoreAsync()
    {
        if (_busy) return;
        var copy = (_restoreCopies.SelectedItem as CopyItem)?.Copy;
        if (copy == null) return;
        if (!_restoreAck.Checked)
        {
            _status.ForeColor = Ui.Danger;
            _status.Text = Ui.T("Tick the warning first.", "علّم على التحذير الأول.");
            return;
        }
        if (!string.Equals(_restoreType.Text.Trim(), "RESTORE", StringComparison.Ordinal))
        {
            _status.ForeColor = Ui.Danger;
            _status.Text = Ui.T("Type RESTORE in capital letters.", "اكتب RESTORE بحروف كبيرة.");
            return;
        }

        var script = AppFiles.FindScript("Restore-HyMotion.ps1", Native.ServiceImageDir(), AppContext.BaseDirectory);
        if (script == null)
        {
            _status.ForeColor = Ui.Danger;
            _status.Text = Ui.T("Restore files are missing on this PC. Call HyMotion.", "ملفات الاستعادة مش على الجهاز. كلّم HyMotion.");
            return;
        }

        _busy = true;
        _doRestore.Enabled = false;
        _restoreAck.Enabled = false;
        _restoreType.Enabled = false;
        _restoreCopies.Enabled = false;
        AppendLog(Ui.T("Starting restore…", "بنبدأ الاستعادة…"));
        try
        {
            if (copy.OnUsb)
            {
                AppendLog(Ui.T("Copying from USB onto this PC…", "بننسخ من الـ USB على الجهاز…"));
                copy = copy with { FolderPath = await Task.Run(() => BackupStore.ImportToThisPc(copy)), OnUsb = false, OriginLabel = Ui.T("This PC", "الجهاز") };
            }

            var args = "-BackupId " + copy.Id + " -Force";
            AppendLog(Ui.T("Restoring. Stay on this window.", "بنستعيد. استنى على النافذة."));
            var result = await Task.Run(() => Native.RunPowerShell(script, args, 1_800_000));
            AppendLog(TrimLog(result.Output));
            if (result.Code == 0)
            {
                _status.ForeColor = Ui.Ok;
                _status.Text = Ui.T("Restore finished. Opening HyMotion…", "الاستعادة خلصت. بنفتح HyMotion…");
                try { Native.TryStartService(); } catch { /* launcher health wait */ }
                await Native.WaitForHealthAsync(TimeSpan.FromSeconds(45));
                Native.OpenBrowser(Desk.LoginUrl);
                Close();
                return;
            }
            if (result.Code == 3)
            {
                _status.ForeColor = Ui.Mute;
                _status.Text = Ui.T("Cancelled.", "اتلغت.");
            }
            else
            {
                _status.ForeColor = Ui.Danger;
                _status.Text = Ui.T("Restore did not finish. Call HyMotion and keep this window.", "الاستعادة ما كملتش. كلّم HyMotion وسيب النافذة.");
            }
        }
        catch (Exception ex)
        {
            AppendLog(ex.Message);
            _status.ForeColor = Ui.Danger;
            _status.Text = OwnerError(ex);
        }
        finally
        {
            _busy = false;
            _doRestore.Enabled = true;
            _restoreAck.Enabled = true;
            _restoreType.Enabled = true;
            _restoreCopies.Enabled = true;
        }
    }

    void AppendLog(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        if (_log.Text.Length > 0) _log.AppendText(Environment.NewLine);
        _log.AppendText(text.Trim());
    }

    static string TrimLog(string output)
    {
        var lines = (output ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length <= 16) return string.Join(Environment.NewLine, lines);
        return string.Join(Environment.NewLine, lines.TakeLast(16));
    }

    static string OwnerError(Exception ex)
    {
        var m = ex.Message ?? "";
        if (m.Contains("not found", StringComparison.OrdinalIgnoreCase))
            return Ui.T("That copy was not found.", "النسخة دي مش موجودة.");
        return m.Length > 220 ? m[..220] : m;
    }

    static string LabelDrive(DriveInfo d)
    {
        var letter = d.Name.TrimEnd('\\');
        var name = "";
        try
        {
            if (!string.IsNullOrWhiteSpace(d.VolumeLabel))
                name = " " + d.VolumeLabel;
        }
        catch { /* ignore */ }
        var free = "";
        try { free = " · " + Math.Max(0, d.AvailableFreeSpace / (1024L * 1024 * 1024)) + " GB"; }
        catch { /* ignore */ }
        return letter + name + " · USB" + free;
    }

    static string Label(BackupCopy c)
    {
        var when = c.CreatedAt?.ToLocalTime().ToString("dd MMM yyyy HH:mm") ?? c.Id;
        var mark = string.Equals(c.Status, "Healthy", StringComparison.OrdinalIgnoreCase)
            ? Ui.T("Looks good", "تمام")
            : c.Status;
        return when + " · " + mark + " · " + c.OriginLabel;
    }

    static void SelectCopy(ComboBox box, string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return;
        for (var i = 0; i < box.Items.Count; i++)
        {
            if (box.Items[i] is CopyItem item && string.Equals(item.Copy.Id, id, StringComparison.OrdinalIgnoreCase))
            {
                box.SelectedIndex = i;
                return;
            }
        }
    }

    static string IdArg(string? id) =>
        string.IsNullOrWhiteSpace(id) ? "" : " --id \"" + id.Replace("\"", "") + "\"";

    static Label Lbl(string text, int x, int y) => new()
    {
        AutoSize = false,
        Bounds = new Rectangle(x, y, 472, 22),
        Text = text,
        ForeColor = Ui.Mute,
    };

    static Button Lime(string text, int x, int y) => new()
    {
        Text = text,
        Bounds = new Rectangle(x, y, 220, 40),
        BackColor = Ui.Lime,
        ForeColor = Color.FromArgb(13, 13, 13),
        FlatStyle = FlatStyle.Flat,
        Font = new Font("Segoe UI", 10f, FontStyle.Bold),
    };

    static Button Ghost(string text, int x, int y)
    {
        var b = Lime(text, x, y);
        b.BackColor = Ui.Card;
        b.ForeColor = Ui.Text;
        b.FlatAppearance.BorderColor = Ui.Line;
        return b;
    }

    static Button Danger(string text, int x, int y)
    {
        var b = Lime(text, x, y);
        b.BackColor = Ui.Danger;
        b.ForeColor = Color.White;
        b.Width = 472;
        return b;
    }

    sealed record DriveItem(DriveInfo Drive, string Label)
    {
        public override string ToString() => Label;
    }

    sealed record CopyItem(BackupCopy Copy, string Label)
    {
        public override string ToString() => Label;
    }
}
