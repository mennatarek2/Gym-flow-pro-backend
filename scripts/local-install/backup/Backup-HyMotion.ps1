# HyMotion Local - Backup.
# Run unattended by the "HyMotion Nightly Backup" Scheduled Task (see Register-BackupTask.ps1),
# or manually: .\Backup-HyMotion.ps1 [-Type Manual|Scheduled|PreRestore] [-Reason "..."]
#
# Produces one folder per backup under %ProgramData%\HyMotion\Backups\<BackupId>\:
#   database.bak   - native SQL Server full backup (BACKUP DATABASE, not a custom dump)
#   uploads.zip     - the uploads directory, paths preserved
#   manifest.json   - metadata + integrity hashes + final status
#
# Exit codes: 0 = Healthy/Verified, 1 = Failed, 2 = Partial, 3 = Skipped (lock held / disabled),
# 4 = Insufficient disk space.

param(
    [ValidateSet("Scheduled", "Manual", "PreRestore")]
    [string]$Type = "Manual",
    [string]$Reason = ""
)

. "$PSScriptRoot\BackupCommon.ps1"

$backupId = New-BackupId -Prefix ($(if ($Type -eq "PreRestore") { "PreRestore" } else { "HyMotionBackup" }))
$backupFolder = Join-Path $BackupsRoot $backupId
$manifest = [ordered]@{
    backupId       = $backupId
    createdAtUtc   = (Get-Date).ToUniversalTime().ToString("o")
    createdAtLocal = (Get-Date).ToString("o")
    backupType     = $Type
    reason         = $Reason
    toolVersion    = "1.0"
    appVersion     = $null
    database       = $null
    uploads        = $null
    status         = "Running"
    offPc          = [ordered]@{ configured = $false; destination = $null; copiedAtUtc = $null; verified = $false }
    errors         = @()
}

function Fail-Backup {
    param([string]$Message)
    Write-BackupLog $Message "ERROR"
    $manifest.status = "Failed"
    $manifest.errors += $Message
    New-Item -ItemType Directory -Path $backupFolder -Force | Out-Null
    Save-BackupManifest -BackupFolder $backupFolder -Manifest $manifest
}

$cfg = Get-BackupConfig
if (-not $cfg.enabled -and $Type -eq "Scheduled") {
    Write-BackupLog "Backup is disabled in backup-config.json - skipping scheduled run." "WARN"
    exit 3
}

try {
    Enter-BackupLock -Owner "backup:$Type"
} catch {
    Write-BackupLog $_.Exception.Message "WARN"
    exit 3
}

try {
    Write-BackupLog "=== Starting $Type backup: $backupId ==="
    $manifest.appVersion = Get-AppVersionString

    $connInfo = Get-HyMotionConnectionInfo
    $manifest.database = [ordered]@{ name = $connInfo.Database }

    # ── Connectivity check ──
    try {
        $masterConn = New-SqlConnection -Server $connInfo.Server -InitialCatalog "master"
    } catch {
        Fail-Backup "Cannot reach SQL Server '$($connInfo.Server)': $($_.Exception.Message)"
        exit 1
    }

    # ── Disk space check BEFORE doing anything expensive ──
    $dbSizeBytes = 0
    try {
        $pages = Invoke-SqlScalar $masterConn "SELECT SUM(size) FROM sys.master_files WHERE database_id = DB_ID('$($connInfo.Database)') AND type = 0"
        $dbSizeBytes = [long]$pages * 8192
    } catch { }
    $uploadsSizeBytes = Get-DirectorySize -Path $UploadsDir
    $estimatedTotal = $dbSizeBytes + $uploadsSizeBytes
    New-Item -ItemType Directory -Path $BackupsRoot -Force | Out-Null
    $spaceCheck = Test-SufficientDiskSpace -Path $BackupsRoot -RequiredBytes $estimatedTotal
    if (-not $spaceCheck.Sufficient) {
        $masterConn.Close()
        Fail-Backup ("BACKUP_INSUFFICIENT_DISK_SPACE: need ~{0:N0} MB (with safety margin), only {1:N0} MB free at {2}" -f ($spaceCheck.NeededBytes / 1MB), ($spaceCheck.FreeBytes / 1MB), $BackupsRoot)
        exit 4
    }
    Write-BackupLog ("Disk space OK: {0:N0} MB free, estimated need {1:N0} MB" -f ($spaceCheck.FreeBytes / 1MB), ($spaceCheck.NeededBytes / 1MB))

    New-Item -ItemType Directory -Path $backupFolder -Force | Out-Null
    $manifest.status = "Running"
    Save-BackupManifest -BackupFolder $backupFolder -Manifest $manifest

    # ── 1. Database backup (native SQL Server BACKUP DATABASE) ──
    $dbBakPath = Join-Path $backupFolder "database.bak"
    $compressionSupported = Test-SqlBackupCompressionSupported -Connection $masterConn
    $withClauses = @("CHECKSUM", "INIT", "STATS = 10")
    if ($compressionSupported) { $withClauses = @("COMPRESSION") + $withClauses }
    $sql = "BACKUP DATABASE [$($connInfo.Database)] TO DISK = N'$dbBakPath' WITH $($withClauses -join ', ')"
    Write-BackupLog "Running: BACKUP DATABASE (compression=$compressionSupported)"
    try {
        Invoke-SqlNonQuery -Connection $masterConn -Sql $sql -TimeoutSeconds 1800 | Out-Null
    } catch {
        $masterConn.Close()
        Fail-Backup "BACKUP DATABASE failed: $($_.Exception.Message)"
        exit 1
    }
    if (-not (Test-Path $dbBakPath)) {
        $masterConn.Close()
        Fail-Backup "BACKUP DATABASE reported success but $dbBakPath does not exist."
        exit 1
    }
    $dbFileInfo = Get-Item $dbBakPath
    Write-BackupLog "Database backup written: $($dbFileInfo.Length) bytes"

    # ── 2. Verify the database backup (never restore-over-production to check) ──
    $dbVerified = $false
    $verify = Test-SqlBackupMedia -Connection $masterConn -BakPath $dbBakPath
    if ($verify.Ok) {
        $dbVerified = $true
        Write-BackupLog "Database backup verified ($($verify.Method))."
    } else {
        Write-BackupLog "Database verification failed: $($verify.Message)" "ERROR"
        $manifest.errors += "Database verification failed: $($verify.Message)"
    }

    # __EFMigrationsHistory lives in the app database, not master - $masterConn's Initial Catalog
    # is master (needed for BACKUP DATABASE), so this needs its own connection.
    # Found the same way as the ZipFile bug above: by actually running this, not just reading it.
    $schemaVersion = $null
    try {
        $appDbConn = New-SqlConnection -Server $connInfo.Server -InitialCatalog $connInfo.Database
        $schemaVersion = Get-SchemaVersion -Connection $appDbConn
        $appDbConn.Close()
    } catch {
        Write-BackupLog "Could not read schema version: $($_.Exception.Message)" "WARN"
    }
    $masterConn.Close()

    $manifest.database = [ordered]@{
        name              = $connInfo.Database
        fileName          = "database.bak"
        sizeBytes         = $dbFileInfo.Length
        sha256            = Get-FileSha256 -Path $dbBakPath
        schemaVersion     = $schemaVersion
        compressionUsed   = $compressionSupported
        verified          = $dbVerified
        verifiedAtUtc     = $(if ($dbVerified) { (Get-Date).ToUniversalTime().ToString("o") } else { $null })
    }

    # ── 3. Uploads backup (streamed, per-file error handling - a locked/unreadable file must not
    #      silently vanish from the archive without being recorded) ──
    # ZipFile (the static open/create helper) lives in System.IO.Compression.FileSystem, not
    # System.IO.Compression (which only has ZipArchive/ZipArchiveMode) - loading just the latter
    # compiles fine but throws "Unable to find type [System.IO.Compression.ZipFile]" at runtime
    # under Windows PowerShell 5.1. Found by actually running this against the real database.
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $uploadsZipPath = Join-Path $backupFolder "uploads.zip"
    $sourceFiles = @()
    if (Test-Path $UploadsDir) {
        $sourceFiles = Get-ChildItem -Path $UploadsDir -Recurse -File -Force -ErrorAction SilentlyContinue
    }
    $skipped = @()
    $copiedCount = 0
    $zip = [System.IO.Compression.ZipFile]::Open($uploadsZipPath, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($f in $sourceFiles) {
            $relativePath = $f.FullName.Substring($UploadsDir.Length).TrimStart('\', '/').Replace('\', '/')
            $fileStream = $null
            $entryStream = $null
            try {
                # CreateEntryFromFile is an extension method (System.IO.Compression.ZipFileExtensions)
                # that Windows PowerShell 5.1 does not reliably resolve on a ZipArchive instance even
                # with the assembly loaded ("does not contain a method named 'CreateEntryFromFile'").
                # CreateEntry + manual stream copy uses only ZipArchive's own instance methods, which
                # always resolve. Found by actually running this against real upload files, not
                # assumed from the .NET docs.
                $entry = $zip.CreateEntry($relativePath, [System.IO.Compression.CompressionLevel]::Optimal)
                $entryStream = $entry.Open()
                $fileStream = [System.IO.File]::OpenRead($f.FullName)
                $fileStream.CopyTo($entryStream)
                $copiedCount++
            } catch {
                Write-BackupLog "Could not archive upload file '$($f.FullName)': $($_.Exception.Message)" "WARN"
                $skipped += $relativePath
            } finally {
                if ($entryStream) { $entryStream.Dispose() }
                if ($fileStream) { $fileStream.Dispose() }
            }
        }
    } finally {
        $zip.Dispose()
    }

    $uploadsStatus = "Complete"
    if ($skipped.Count -gt 0) {
        $uploadsStatus = $(if ($copiedCount -eq 0) { "Failed" } else { "Partial" })
    }
    $uploadsZipInfo = Get-Item $uploadsZipPath
    Write-BackupLog "Uploads archived: $copiedCount files, $($skipped.Count) skipped, $($uploadsZipInfo.Length) bytes"

    # ── 4. Verify the uploads archive (open it back up, count entries) ──
    $uploadsVerified = $false
    try {
        $verifyZip = [System.IO.Compression.ZipFile]::OpenRead($uploadsZipPath)
        $entryCount = $verifyZip.Entries.Count
        $verifyZip.Dispose()
        $uploadsVerified = ($entryCount -eq $copiedCount)
        if (-not $uploadsVerified) {
            $manifest.errors += "Uploads archive entry count ($entryCount) does not match files copied ($copiedCount)."
        }
    } catch {
        $manifest.errors += "Uploads archive could not be reopened for verification: $($_.Exception.Message)"
    }

    $manifest.uploads = [ordered]@{
        fileName      = "uploads.zip"
        sourceRoot    = $UploadsDir
        totalFiles    = $sourceFiles.Count
        fileCount     = $copiedCount
        skippedFiles  = $skipped
        sizeBytes     = $uploadsZipInfo.Length
        sha256        = Get-FileSha256 -Path $uploadsZipPath
        status        = $uploadsStatus
        verified      = $uploadsVerified
    }

    # ── 5. Final status ──
    $finalStatus = "Healthy"
    if (-not $dbVerified) { $finalStatus = "Failed" }
    elseif ($uploadsStatus -eq "Failed") { $finalStatus = "Failed" }
    elseif ($uploadsStatus -eq "Partial" -or -not $uploadsVerified) { $finalStatus = "Partial" }
    $manifest.status = $finalStatus
    Save-BackupManifest -BackupFolder $backupFolder -Manifest $manifest
    Write-BackupLog "Backup $backupId finished with status: $finalStatus"

    # ── 6. Off-PC copy (only for backups already at least Partial-or-better; never claim healthy
    #      off-PC copy without verifying the copy itself) ──
    if ($cfg.offPcDestination -and $finalStatus -ne "Failed") {
        try {
            if (-not (Test-Path $cfg.offPcDestination)) {
                throw "Configured off-PC destination '$($cfg.offPcDestination)' is not reachable."
            }
            $destFolder = Join-Path $cfg.offPcDestination $backupId
            Write-BackupLog "Copying backup to off-PC destination: $destFolder"
            Copy-Item -Path $backupFolder -Destination $destFolder -Recurse -Force
            $copyOk = $true
            foreach ($f in @("database.bak", "uploads.zip", "manifest.json")) {
                $srcHash = $(if ($f -ne "manifest.json") { Get-FileSha256 (Join-Path $backupFolder $f) } else { $null })
                $dstPath = Join-Path $destFolder $f
                if (-not (Test-Path $dstPath)) { $copyOk = $false; break }
                if ($srcHash -and (Get-FileSha256 $dstPath) -ne $srcHash) { $copyOk = $false; break }
            }
            $manifest.offPc = [ordered]@{
                configured  = $true
                destination = $cfg.offPcDestination
                copiedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
                verified    = $copyOk
            }
            Write-BackupLog "Off-PC copy verified: $copyOk"
        } catch {
            Write-BackupLog "Off-PC copy failed: $($_.Exception.Message)" "ERROR"
            $manifest.offPc = [ordered]@{ configured = $true; destination = $cfg.offPcDestination; copiedAtUtc = $null; verified = $false }
            $manifest.errors += "Off-PC copy failed: $($_.Exception.Message)"
        }
        Save-BackupManifest -BackupFolder $backupFolder -Manifest $manifest
    }

    # ── 7. Retention (only ever deletes verified-safe candidates, never the last healthy one) ──
    if ($Type -ne "PreRestore") {
        Invoke-BackupRetention -RetentionCount $cfg.retentionCount
    }

    switch ($finalStatus) {
        "Healthy" { exit 0 }
        "Partial" { exit 2 }
        default   { exit 1 }
    }
} catch {
    Fail-Backup "Unexpected backup failure: $($_.Exception.Message)"
    exit 1
} finally {
    Exit-BackupLock
}
