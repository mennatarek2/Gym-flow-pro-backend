# HyMotion Local - Backup & Recovery shared helpers.
# Dot-sourced by Backup-HyMotion.ps1, Restore-HyMotion.ps1, and Register-BackupTask.ps1.
# Keep this file dependency-free (no external modules) - it must run unattended under a
# Scheduled Task with no internet access and no PowerShell Gallery.

$ErrorActionPreference = "Stop"

# ── Paths (mirrors GMS.Core.Configuration.LocalRuntimePaths - do not diverge from it) ──
$Script:HyMotionRoot = Join-Path $env:ProgramData "HyMotion"
$Script:ConfigDir    = Join-Path $HyMotionRoot "config"
$Script:UploadsDir   = Join-Path $HyMotionRoot "uploads"
$Script:LogsDir      = Join-Path $HyMotionRoot "logs"
$Script:BackupsRoot  = Join-Path $HyMotionRoot "Backups"
$Script:BackupConfigFile = Join-Path $ConfigDir "backup-config.json"
$Script:LockFile     = Join-Path $BackupsRoot ".backup.lock"

function Get-BackupLogPath {
    return Join-Path $LogsDir ("backup-{0}.log" -f (Get-Date -Format "yyyy-MM-dd"))
}

function Write-BackupLog {
    param(
        [Parameter(Mandatory = $true)][string]$Message,
        [ValidateSet("INFO", "WARN", "ERROR")][string]$Level = "INFO"
    )
    New-Item -ItemType Directory -Path $LogsDir -Force | Out-Null
    $line = "[{0}] [{1}] {2}" -f (Get-Date -Format "yyyy-MM-dd HH:mm:ss"), $Level, $Message
    Add-Content -Path (Get-BackupLogPath) -Value $line
    if ($Level -eq "ERROR") { Write-Host $line -ForegroundColor Red }
    elseif ($Level -eq "WARN") { Write-Host $line -ForegroundColor Yellow }
    else { Write-Host $line }
}

# ── Connection string (reads the SAME override file the app itself reads - never a second
#    source of truth for how to reach SQL Server) ──
function Get-HyMotionConnectionInfo {
    $configuredServer = "."
    $configuredDatabase = "HyMotionLocal"
    if (Test-Path $BackupConfigFile -PathType Leaf) {
        # backup-config.json only ever overrides backup SETTINGS (destination/retention), never
        # the DB connection - but tolerate a future field without failing if present.
    }
    $appConfigFile = Join-Path $ConfigDir "appsettings.json"
    if (Test-Path $appConfigFile) {
        try {
            $json = Get-Content $appConfigFile -Raw | ConvertFrom-Json
            $cs = $json.ConnectionStrings.DefaultConnection
            if ($cs) {
                if ($cs -match "Server=([^;]+)") { $configuredServer = $Matches[1] }
                if ($cs -match "Database=([^;]+)") { $configuredDatabase = $Matches[1] }
            }
        } catch {
            Write-BackupLog "Could not parse $appConfigFile for connection info, using defaults ($configuredServer/$configuredDatabase): $($_.Exception.Message)" "WARN"
        }
    }
    return [PSCustomObject]@{ Server = $configuredServer; Database = $configuredDatabase }
}

function New-SqlConnection {
    param([string]$Server, [string]$InitialCatalog = "master")
    Add-Type -AssemblyName System.Data -ErrorAction SilentlyContinue
    $csb = New-Object System.Data.SqlClient.SqlConnectionStringBuilder
    $csb["Server"] = $Server
    $csb["Integrated Security"] = $true
    $csb["Initial Catalog"] = $InitialCatalog
    $csb["TrustServerCertificate"] = $true
    $csb["Connect Timeout"] = 15
    $conn = New-Object System.Data.SqlClient.SqlConnection($csb.ConnectionString)
    $conn.Open()
    return $conn
}

function Invoke-SqlScalar {
    param([System.Data.SqlClient.SqlConnection]$Connection, [string]$Sql, [int]$TimeoutSeconds = 30)
    $cmd = $Connection.CreateCommand()
    $cmd.CommandText = $Sql
    $cmd.CommandTimeout = $TimeoutSeconds
    return $cmd.ExecuteScalar()
}

function Invoke-SqlNonQuery {
    param([System.Data.SqlClient.SqlConnection]$Connection, [string]$Sql, [int]$TimeoutSeconds = 600)
    $cmd = $Connection.CreateCommand()
    $cmd.CommandText = $Sql
    $cmd.CommandTimeout = $TimeoutSeconds
    return $cmd.ExecuteNonQuery()
}

function Escape-SqlLiteral {
    param([string]$Value)
    if ($null -eq $Value) { return "" }
    return $Value.Replace("'", "''")
}

function Test-SqlCreateDatabasePermissionDenied {
    param([string]$Message)
    return [bool]($Message -match 'CREATE DATABASE permission denied' -or $Message -match 'VERIFY DATABASE is terminating')
}

# RESTORE VERIFYONLY is the right integrity check, but SQL Server requires CREATE DATABASE for it.
# Local Edition often runs as a Windows login that can BACKUP the gym DB but is not dbcreator.
# Fall back to FILELISTONLY (reads the media header, does not create a database). If that is also
# blocked, a backup written WITH CHECKSUM whose .bak is on disk is still treated as verified.
function Test-SqlBackupMedia {
    param(
        [System.Data.SqlClient.SqlConnection]$Connection,
        [string]$BakPath
    )
    $disk = Escape-SqlLiteral $BakPath
    try {
        Invoke-SqlNonQuery -Connection $Connection -Sql "RESTORE VERIFYONLY FROM DISK = N'$disk'" -TimeoutSeconds 600 | Out-Null
        return [PSCustomObject]@{ Ok = $true; Method = "VerifyOnly"; Message = $null }
    } catch {
        $verifyMsg = $_.Exception.Message
        if (-not (Test-SqlCreateDatabasePermissionDenied $verifyMsg)) {
            return [PSCustomObject]@{ Ok = $false; Method = "VerifyOnly"; Message = $verifyMsg }
        }
        Write-BackupLog "RESTORE VERIFYONLY needs CREATE DATABASE; this SQL login does not have it. Checking the backup media header instead." "WARN"
        try {
            $cmd = $Connection.CreateCommand()
            $cmd.CommandText = "RESTORE FILELISTONLY FROM DISK = N'$disk'"
            $cmd.CommandTimeout = 120
            $reader = $cmd.ExecuteReader()
            try {
                $hasRow = $reader.Read()
            } finally {
                $reader.Close()
            }
            if ($hasRow) {
                return [PSCustomObject]@{ Ok = $true; Method = "FileListOnly"; Message = $null }
            }
            return [PSCustomObject]@{ Ok = $false; Method = "FileListOnly"; Message = "Backup media header was empty." }
        } catch {
            $listMsg = $_.Exception.Message
            if ((Test-Path -LiteralPath $BakPath) -and ((Get-Item -LiteralPath $BakPath).Length -gt 0) -and (Test-SqlCreateDatabasePermissionDenied $listMsg)) {
                Write-BackupLog "Could not run FILELISTONLY either ($listMsg). Backup file is present and was written WITH CHECKSUM." "WARN"
                return [PSCustomObject]@{ Ok = $true; Method = "BackupFile"; Message = $null }
            }
            return [PSCustomObject]@{ Ok = $false; Method = "FileListOnly"; Message = $listMsg }
        }
    }
}

# Express edition cannot BACKUP ... WITH COMPRESSION ("This edition of SQL Server does not
# support database backup compression"). Detect instead of hardcoding - a script that only
# works on the developer's Enterprise Evaluation instance and fails on every real customer's
# Express install is worse than not compressing at all.
function Test-SqlBackupCompressionSupported {
    param([System.Data.SqlClient.SqlConnection]$Connection)
    try {
        $edition = [string](Invoke-SqlScalar $Connection "SELECT CAST(SERVERPROPERTY('EngineEdition') AS INT)")
        # EngineEdition: 4 = Express (and Express variants). 2=Standard,3=Enterprise/Developer,5=Azure SQL DB,8=Managed Instance.
        return $edition -ne "4"
    } catch {
        return $false
    }
}

function Get-SchemaVersion {
    param([System.Data.SqlClient.SqlConnection]$Connection)
    try {
        return [string](Invoke-SqlScalar $Connection "SELECT TOP 1 MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId DESC")
    } catch {
        return $null
    }
}

function Get-AppVersionString {
    # Reads the actual running binary's own version - avoids maintaining a second, driftable
    # "app version" string only the backup system knows about.
    $candidates = @(
        (Join-Path $HyMotionRoot "..\..\Program Files\HyMotion\app\GMS.Api.exe"),
        "$env:ProgramFiles\HyMotion\app\GMS.Api.exe"
    )
    foreach ($c in $candidates) {
        if (Test-Path $c) {
            try { return [System.Diagnostics.FileVersionInfo]::GetVersionInfo($c).FileVersion } catch { }
        }
    }
    return "unknown"
}

# ── Backup lock: prevents two backups (or a backup + a restore) running concurrently.
#    File-based with PID + start time, stale-lock detection (process no longer exists). ──
function Enter-BackupLock {
    param([string]$Owner = "backup")
    New-Item -ItemType Directory -Path $BackupsRoot -Force | Out-Null
    if (Test-Path $LockFile) {
        try {
            $existing = Get-Content $LockFile -Raw | ConvertFrom-Json
            $stillRunning = $false
            try {
                $proc = Get-Process -Id $existing.pid -ErrorAction Stop
                if ($proc -and $proc.StartTime -eq [datetime]$existing.startTime) { $stillRunning = $true }
            } catch { $stillRunning = $false }
            if ($stillRunning) {
                throw "Another backup/restore operation is already running (pid=$($existing.pid), owner=$($existing.owner), started $($existing.startTime)). Aborting to avoid concurrent access."
            } else {
                Write-BackupLog "Found a stale lock file (owner=$($existing.owner), pid=$($existing.pid) no longer running) - clearing it." "WARN"
            }
        } catch [System.Management.Automation.RuntimeException] {
            throw
        } catch {
            Write-BackupLog "Lock file was unreadable/corrupt - clearing it." "WARN"
        }
    }
    $proc = Get-Process -Id $PID
    $lockInfo = @{ pid = $PID; owner = $Owner; startTime = $proc.StartTime.ToString("o") }
    ($lockInfo | ConvertTo-Json) | Set-Content -Path $LockFile
}

function Exit-BackupLock {
    if (Test-Path $LockFile) { Remove-Item $LockFile -Force -ErrorAction SilentlyContinue }
}

# ── Disk space ──
function Test-SufficientDiskSpace {
    param([string]$Path, [long]$RequiredBytes, [double]$SafetyMarginRatio = 1.25)
    $drive = (Get-Item $Path).PSDrive
    if (-not $drive) { $drive = Get-PSDrive -Name ([System.IO.Path]::GetPathRoot($Path).TrimEnd('\', ':') ) }
    $free = (Get-PSDrive -Name $drive.Name).Free
    $needed = [long]($RequiredBytes * $SafetyMarginRatio)
    return [PSCustomObject]@{
        FreeBytes    = $free
        NeededBytes  = $needed
        Sufficient   = $free -gt $needed
    }
}

function Get-DirectorySize {
    param([string]$Path)
    if (-not (Test-Path $Path)) { return 0 }
    $files = Get-ChildItem -Path $Path -Recurse -File -Force -ErrorAction SilentlyContinue
    if (-not $files) { return 0 }
    return ($files | Measure-Object -Property Length -Sum).Sum
}

function Get-FileSha256 {
    param([string]$Path)
    $hash = Get-FileHash -Path $Path -Algorithm SHA256
    return $hash.Hash
}

# ── Backup config (off-PC destination, retention count, enabled flag) ──
function Get-BackupConfig {
    $defaults = [PSCustomObject]@{
        enabled          = $true
        retentionCount   = 14
        offPcDestination = $null
    }
    if (Test-Path $BackupConfigFile) {
        try {
            $cfg = Get-Content $BackupConfigFile -Raw | ConvertFrom-Json
            foreach ($p in $defaults.PSObject.Properties.Name) {
                if ($null -eq $cfg.$p) { $cfg | Add-Member -NotePropertyName $p -NotePropertyValue $defaults.$p -Force }
            }
            return $cfg
        } catch {
            Write-BackupLog "backup-config.json unreadable, using defaults: $($_.Exception.Message)" "WARN"
        }
    }
    return $defaults
}

function Save-BackupConfig {
    param($Config)
    New-Item -ItemType Directory -Path $ConfigDir -Force | Out-Null
    ($Config | ConvertTo-Json) | Set-Content -Path $BackupConfigFile
}

# ── Manifest ──
function Read-BackupManifest {
    param([string]$BackupFolder)
    $path = Join-Path $BackupFolder "manifest.json"
    if (-not (Test-Path $path)) { return $null }
    try { return Get-Content $path -Raw | ConvertFrom-Json } catch { return $null }
}

function Save-BackupManifest {
    param([string]$BackupFolder, $Manifest)
    New-Item -ItemType Directory -Path $BackupFolder -Force | Out-Null
    $path = Join-Path $BackupFolder "manifest.json"
    ($Manifest | ConvertTo-Json -Depth 10) | Set-Content -Path $path
}

function Save-RestoreLog {
    # Diagnostic record of the most recent restore attempt (success or failure). Not itself a
    # backup - deliberately named so Get-AllBackups' HyMotionBackup_*/PreRestore_* filters skip it.
    param($RestoreLog)
    Save-BackupManifest -BackupFolder (Join-Path $BackupsRoot "_last-restore-attempt") -Manifest $RestoreLog
}

function Get-AllBackups {
    # Returns every backup folder's manifest, newest first, tolerating unreadable/partial ones.
    if (-not (Test-Path $BackupsRoot)) { return @() }
    $folders = Get-ChildItem -Path $BackupsRoot -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -like "HyMotionBackup_*" -or $_.Name -like "PreRestore_*" }
    $results = @()
    foreach ($f in $folders) {
        $m = Read-BackupManifest -BackupFolder $f.FullName
        if ($m) {
            $m | Add-Member -NotePropertyName "folderPath" -NotePropertyValue $f.FullName -Force
            $results += $m
        }
    }
    return $results | Sort-Object createdAtUtc -Descending
}

function Test-BackupIntegrity {
    # Independent of the backup-time verification: re-checks the manifest's own recorded hashes
    # against what's on disk right now, so a backup can't be "trusted" just because it once said
    # Healthy - it must still verify at the moment something is about to restore from it.
    param([string]$BackupFolder)
    $result = [PSCustomObject]@{ Ok = $false; Manifest = $null; Problems = @() }
    $manifest = Read-BackupManifest -BackupFolder $BackupFolder
    if (-not $manifest) { $result.Problems += "manifest.json missing or unreadable"; return $result }
    $result.Manifest = $manifest

    $dbPath = Join-Path $BackupFolder $manifest.database.fileName
    if (-not (Test-Path $dbPath)) { $result.Problems += "database.bak missing" }
    elseif ((Get-FileSha256 $dbPath) -ne $manifest.database.sha256) { $result.Problems += "database.bak hash mismatch (file changed or corrupt since backup)" }

    $zipPath = Join-Path $BackupFolder $manifest.uploads.fileName
    if (-not (Test-Path $zipPath)) { $result.Problems += "uploads.zip missing" }
    elseif ((Get-FileSha256 $zipPath) -ne $manifest.uploads.sha256) { $result.Problems += "uploads.zip hash mismatch (file changed or corrupt since backup)" }

    if ($manifest.status -eq "Failed") { $result.Problems += "backup's own recorded status is Failed" }

    $result.Ok = ($result.Problems.Count -eq 0)
    return $result
}

function New-BackupId {
    param([string]$Prefix = "HyMotionBackup")
    return "{0}_{1}" -f $Prefix, (Get-Date -Format "yyyy-MM-dd_HHmmss")
}

# ── Retention ──
# Only considers regular nightly/manual backups (HyMotionBackup_*). PreRestore_* safety backups
# are never auto-deleted here - they're tied to a specific restore event and small in number;
# an operator cleans those up by hand if desired.
function Invoke-BackupRetention {
    param([int]$RetentionCount = 14, [int]$KeepFailedCount = 5)

    if (-not (Test-Path $BackupsRoot)) { return }
    $all = Get-AllBackups | Where-Object { $_.backupId -like "HyMotionBackup_*" }
    $healthy = @($all | Where-Object { $_.status -eq "Healthy" } | Sort-Object createdAtUtc -Descending)
    $other = @($all | Where-Object { $_.status -ne "Healthy" } | Sort-Object createdAtUtc -Descending)

    $toDelete = @()
    if ($healthy.Count -gt $RetentionCount) {
        # Never drop below 1 healthy backup even if RetentionCount is misconfigured to 0.
        $keepCount = [Math]::Max(1, $RetentionCount)
        $toDelete += $healthy | Select-Object -Skip $keepCount
    }
    if ($other.Count -gt $KeepFailedCount) {
        $toDelete += $other | Select-Object -Skip $KeepFailedCount
    }

    foreach ($b in $toDelete) {
        # Never delete a backup still being written (defensive - Get-AllBackups only returns ones
        # with a saved manifest, but a manifest can exist mid-write with status "Running").
        if ($b.status -eq "Running") { continue }
        if (Test-Path $LockFile) {
            try {
                $lock = Get-Content $LockFile -Raw | ConvertFrom-Json
                if ($lock.owner -like "*$($b.backupId)*") { continue }
            } catch { }
        }
        try {
            Remove-Item -Path $b.folderPath -Recurse -Force
            Write-BackupLog "Deleted: $($b.backupId) | Reason: retention policy exceeded (status was $($b.status))"
        } catch {
            Write-BackupLog "Retention could not delete $($b.backupId): $($_.Exception.Message)" "WARN"
        }
    }
}
