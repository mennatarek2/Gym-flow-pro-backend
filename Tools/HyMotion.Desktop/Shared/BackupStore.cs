using System.Management;
using System.Text;
using System.Text.Json;

namespace HyMotion.Desktop;

internal enum BackupMode
{
    Home,
    Usb,
    Restore,
    Verify,
    CopyLive,
}

internal sealed class BackupLaunch
{
    public BackupMode Mode { get; init; } = BackupMode.Home;
    public string? BackupId { get; init; }
    public bool Elevated { get; init; }
    public string? VerifyDir { get; init; }
    public string? VerifyOut { get; init; }

    public string ToProcessArgs()
    {
        var parts = new List<string>();
        if (Mode == BackupMode.Usb) parts.Add("--usb");
        if (Mode == BackupMode.Restore) parts.Add("--restore");
        if (!string.IsNullOrWhiteSpace(BackupId))
            parts.Add("--id \"" + BackupId.Replace("\"", "") + "\"");
        if (Elevated) parts.Add("--elevated");
        return string.Join(" ", parts);
    }
}

internal sealed record BackupCopy(
    string Id,
    string FolderPath,
    string Status,
    DateTimeOffset? CreatedAt,
    string OriginLabel,
    bool OnUsb)
{
    public bool CanRestore =>
        !string.Equals(Status, "Failed", StringComparison.OrdinalIgnoreCase)
        && BackupStore.HasRequiredFiles(FolderPath);
}

internal static class BackupStore
{
    public const string ProtocolName = "hymotion-backup";
    public const string UsbFolderName = "HyMotionBackups";

    public static string BackupsRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "HyMotion", "Backups");

    public static BackupLaunch Parse(IReadOnlyList<string> args)
    {
        var mode = BackupMode.Home;
        string? id = null;
        var elevated = false;
        string? verifyDir = null;
        string? verifyOut = null;

        for (var i = 0; i < args.Count; i++)
        {
            var a = args[i].Trim().Trim('"');
            if (a.StartsWith(ProtocolName + ":", StringComparison.OrdinalIgnoreCase))
            {
                var rest = a[(ProtocolName.Length + 1)..].TrimStart('/');
                var q = rest.IndexOf('?');
                var path = (q >= 0 ? rest[..q] : rest).Trim('/');
                var query = q >= 0 ? rest[(q + 1)..] : "";
                if (path.Equals("usb", StringComparison.OrdinalIgnoreCase)
                    || path.Equals("copy", StringComparison.OrdinalIgnoreCase))
                    mode = BackupMode.Usb;
                else if (path.Equals("restore", StringComparison.OrdinalIgnoreCase))
                    mode = BackupMode.Restore;
                id = QueryValue(query, "id") ?? id;
                continue;
            }

            if (a.Equals("--usb", StringComparison.OrdinalIgnoreCase) || a.Equals("-usb", StringComparison.OrdinalIgnoreCase))
                mode = BackupMode.Usb;
            else if (a.Equals("--restore", StringComparison.OrdinalIgnoreCase))
                mode = BackupMode.Restore;
            else if (a.Equals("--verify", StringComparison.OrdinalIgnoreCase))
                mode = BackupMode.Verify;
            else if (a.Equals("--copy-live", StringComparison.OrdinalIgnoreCase))
                mode = BackupMode.CopyLive;
            else if (a.Equals("--elevated", StringComparison.OrdinalIgnoreCase))
                elevated = true;
            else if ((a.Equals("--id", StringComparison.OrdinalIgnoreCase) || a.Equals("-id", StringComparison.OrdinalIgnoreCase))
                     && i + 1 < args.Count)
                id = args[++i].Trim().Trim('"');
            else if (a.StartsWith("--id=", StringComparison.OrdinalIgnoreCase))
                id = a[5..].Trim().Trim('"');
            else if (a.Equals("--verify-dir", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Count)
                verifyDir = args[++i];
            else if (a.Equals("--verify-out", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Count)
                verifyOut = args[++i];
        }

        return new BackupLaunch
        {
            Mode = mode,
            BackupId = string.IsNullOrWhiteSpace(id) ? null : id,
            Elevated = elevated,
            VerifyDir = verifyDir,
            VerifyOut = verifyOut,
        };
    }

    public static IReadOnlyList<DriveInfo> CandidateUsbDrives()
    {
        var usbLetters = UsbVolumeLetters();
        var list = new List<DriveInfo>();
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (!d.IsReady) continue;
                var letter = char.ToUpperInvariant(d.Name[0]);
                if (d.DriveType == DriveType.Removable || usbLetters.Contains(letter))
                    list.Add(d);
            }
            catch
            {
                // skip unreadable volumes
            }
        }
        return list.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    // USB HDDs / UASP enclosures often show as DriveType.Fixed. BusType USB is the real signal.
    static HashSet<char> UsbVolumeLetters()
    {
        var letters = new HashSet<char>();
        try
        {
            using var disks = new ManagementObjectSearcher(
                @"\\.\root\Microsoft\Windows\Storage",
                "SELECT Number, BusType FROM MSFT_Disk");
            foreach (ManagementObject disk in disks.Get())
            {
                using (disk)
                {
                    var bus = Convert.ToInt32(disk["BusType"]);
                    // 7=USB, 12=SD, 13=MMC (see MSFT_Disk BusType)
                    if (bus is not (7 or 12 or 13)) continue;
                    var number = Convert.ToInt32(disk["Number"]);
                    using var parts = new ManagementObjectSearcher(
                        @"\\.\root\Microsoft\Windows\Storage",
                        "SELECT DriveLetter FROM MSFT_Partition WHERE DiskNumber = " + number);
                    foreach (ManagementObject part in parts.Get())
                    {
                        using (part)
                        {
                            var raw = part["DriveLetter"];
                            if (raw is char c && char.IsLetter(c))
                                letters.Add(char.ToUpperInvariant(c));
                            else if (raw is string s && s.Length > 0 && char.IsLetter(s[0]))
                                letters.Add(char.ToUpperInvariant(s[0]));
                            else if (raw != null && raw is not DBNull)
                            {
                                var text = Convert.ToString(raw);
                                if (!string.IsNullOrWhiteSpace(text) && char.IsLetter(text[0]))
                                    letters.Add(char.ToUpperInvariant(text[0]));
                            }
                        }
                    }
                }
            }
        }
        catch
        {
            // older Windows / no Storage namespace — Removable DriveType still applies
        }
        return letters;
    }

    public static string UsbRoot(DriveInfo drive) =>
        Path.Combine(drive.RootDirectory.FullName, UsbFolderName);

    public static IReadOnlyList<BackupCopy> ListLocal() => ListFromRoot(BackupsRoot, Ui.T("This PC", "الجهاز"), onUsb: false);

    public static IReadOnlyList<BackupCopy> ListOnDrive(DriveInfo drive) =>
        ListFromRoot(UsbRoot(drive), drive.Name.TrimEnd('\\'), onUsb: true);

    public static IReadOnlyList<BackupCopy> ListRestoreChoices()
    {
        var list = new List<BackupCopy>();
        list.AddRange(ListLocal());
        foreach (var d in CandidateUsbDrives())
            list.AddRange(ListOnDrive(d));
        return list
            .Where(b => b.CanRestore)
            .GroupBy(b => b.Id, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderByDescending(b => b.CreatedAt ?? DateTimeOffset.MinValue)
            .ToList();
    }

    public static BackupCopy? NewestUsable(IEnumerable<BackupCopy> copies) =>
        copies
            .Where(b => b.CanRestore && string.Equals(b.Status, "Healthy", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(b => b.CreatedAt ?? DateTimeOffset.MinValue)
            .FirstOrDefault()
        ?? copies.Where(b => b.CanRestore).OrderByDescending(b => b.CreatedAt ?? DateTimeOffset.MinValue).FirstOrDefault();

    public static bool HasRequiredFiles(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return false;
        return File.Exists(Path.Combine(folder, "database.bak"))
            && File.Exists(Path.Combine(folder, "uploads.zip"))
            && File.Exists(Path.Combine(folder, "manifest.json"));
    }

    public static void CopyDirectory(string source, string dest)
    {
        if (!Directory.Exists(source))
            throw new DirectoryNotFoundException(source);
        Directory.CreateDirectory(dest);
        foreach (var dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(source, dir);
            Directory.CreateDirectory(Path.Combine(dest, rel));
        }
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(source, file);
            var target = Path.Combine(dest, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    public static string CopyToUsb(BackupCopy copy, DriveInfo drive)
    {
        if (!copy.CanRestore)
            throw new InvalidOperationException(Ui.T("This copy cannot be used.", "النسخة دي مش تتنفع."));
        var dest = Path.Combine(UsbRoot(drive), copy.Id);
        CopyDirectory(copy.FolderPath, dest);
        if (!HasRequiredFiles(dest))
            throw new InvalidOperationException(Ui.T("The USB copy is missing files. Try another stick.", "نسخة الـ USB ناقصة ملفات. جرّب فلاشة تانية."));
        return dest;
    }

    public static string ImportToThisPc(BackupCopy copy)
    {
        if (!copy.OnUsb) return copy.FolderPath;
        var dest = Path.Combine(BackupsRoot, copy.Id);
        Directory.CreateDirectory(BackupsRoot);
        CopyDirectory(copy.FolderPath, dest);
        if (!HasRequiredFiles(dest))
            throw new InvalidOperationException(Ui.T("Could not copy this backup onto the PC.", "ما قدرناش ننسخ النسخة على الجهاز."));
        return dest;
    }

    public static int RunCopyLive(BackupLaunch launch)
    {
        var log = new StringBuilder();
        try
        {
            var usb = CandidateUsbDrives().FirstOrDefault();
            if (usb == null)
            {
                log.AppendLine("no usb");
                WriteVerifyOut(launch.VerifyOut, log.ToString());
                return 2;
            }
            var copies = ListLocal();
            var copy = !string.IsNullOrWhiteSpace(launch.BackupId)
                ? copies.FirstOrDefault(b => string.Equals(b.Id, launch.BackupId, StringComparison.OrdinalIgnoreCase))
                : NewestUsable(copies);
            if (copy == null || !copy.CanRestore)
                throw new InvalidOperationException("no usable backup on this PC");
            var dest = CopyToUsb(copy, usb);
            log.AppendLine("copy-live ok");
            log.AppendLine("id=" + copy.Id);
            log.AppendLine("dest=" + dest);
            WriteVerifyOut(launch.VerifyOut, log.ToString());
            return 0;
        }
        catch (Exception ex)
        {
            log.AppendLine("copy-live failed: " + ex.Message);
            WriteVerifyOut(launch.VerifyOut, log.ToString());
            return 1;
        }
    }

    public static int RunVerify(BackupLaunch launch)
    {
        var log = new StringBuilder();
        try
        {
            var restore = Parse(new[] { "hymotion-backup:restore?id=HyMotionBackup_2026-09-14_044724" });
            if (restore.Mode != BackupMode.Restore || restore.BackupId != "HyMotionBackup_2026-09-14_044724")
                throw new InvalidOperationException("parse restore URL failed");

            var usb = Parse(new[] { "hymotion-backup:usb" });
            if (usb.Mode != BackupMode.Usb)
                throw new InvalidOperationException("parse usb URL failed");

            var flags = Parse(new[] { "--restore", "--id", "HyMotionBackup_test", "--elevated" });
            if (flags.Mode != BackupMode.Restore || flags.BackupId != "HyMotionBackup_test" || !flags.Elevated)
                throw new InvalidOperationException("parse flags failed");

            var src = Path.Combine(Path.GetTempPath(), "hymotion-verify-src", "HyMotionBackup_test");
            var dstRoot = string.IsNullOrWhiteSpace(launch.VerifyDir)
                ? Path.Combine(Path.GetTempPath(), "hymotion-verify-usb")
                : launch.VerifyDir;
            if (Directory.Exists(src)) Directory.Delete(src, true);
            Directory.CreateDirectory(src);
            File.WriteAllText(Path.Combine(src, "manifest.json"), "{\"backupId\":\"HyMotionBackup_test\",\"status\":\"Healthy\"}");
            File.WriteAllText(Path.Combine(src, "database.bak"), "db");
            File.WriteAllText(Path.Combine(src, "uploads.zip"), "zip");
            var dest = Path.Combine(dstRoot, UsbFolderName, "HyMotionBackup_test");
            if (Directory.Exists(dest)) Directory.Delete(dest, true);
            CopyDirectory(src, dest);
            if (!HasRequiredFiles(dest))
                throw new InvalidOperationException("copied backup missing required files");

            log.AppendLine("verify ok");
            log.AppendLine("copied=" + dest);
            foreach (var d in CandidateUsbDrives())
                log.AppendLine("usb=" + d.Name.TrimEnd('\\'));
            WriteVerifyOut(launch.VerifyOut, log.ToString());
            return 0;
        }
        catch (Exception ex)
        {
            log.AppendLine("verify failed: " + ex.Message);
            WriteVerifyOut(launch.VerifyOut, log.ToString());
            return 1;
        }
    }

    static IReadOnlyList<BackupCopy> ListFromRoot(string root, string origin, bool onUsb)
    {
        var list = new List<BackupCopy>();
        if (!Directory.Exists(root)) return list;
        foreach (var dir in Directory.GetDirectories(root))
        {
            var name = Path.GetFileName(dir);
            if (!name.StartsWith("HyMotionBackup_", StringComparison.OrdinalIgnoreCase)
                && !name.StartsWith("PreRestore_", StringComparison.OrdinalIgnoreCase))
                continue;
            var (status, created) = ReadManifest(dir);
            list.Add(new BackupCopy(name, dir, status, created, origin, onUsb));
        }
        return list.OrderByDescending(b => b.CreatedAt ?? DateTimeOffset.MinValue).ToList();
    }

    static (string Status, DateTimeOffset? Created) ReadManifest(string folder)
    {
        var path = Path.Combine(folder, "manifest.json");
        if (!File.Exists(path)) return ("Unknown", null);
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            var status = ReadString(root, "status") ?? "Unknown";
            DateTimeOffset? created = null;
            var raw = ReadString(root, "createdAtUtc") ?? ReadString(root, "createdAtLocal");
            if (raw != null && DateTimeOffset.TryParse(raw, out var dt)) created = dt;
            return (status, created);
        }
        catch
        {
            return ("Unknown", null);
        }
    }

    static string? ReadString(JsonElement root, string name)
    {
        if (root.ValueKind != JsonValueKind.Object) return null;
        foreach (var p in root.EnumerateObject())
        {
            if (p.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.String)
                return p.Value.GetString();
        }
        return null;
    }

    static string? QueryValue(string query, string key)
    {
        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=', 2);
            if (!Uri.UnescapeDataString(kv[0]).Equals(key, StringComparison.OrdinalIgnoreCase)) continue;
            return kv.Length > 1 ? Uri.UnescapeDataString(kv[1].Replace('+', ' ')) : "";
        }
        return null;
    }

    static void WriteVerifyOut(string? path, string text)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, text);
    }
}
