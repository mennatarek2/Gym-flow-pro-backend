# HyMotion Local - Restore.
# Destructive operation. Run elevated (Administrator), interactively - not from the Scheduled Task.
#
# Usage (from the folder that contains GMS.Api.exe):
#   .\install-scripts\backup\Restore-HyMotion.ps1 -Latest
#   .\install-scripts\backup\Restore-HyMotion.ps1 -BackupId HyMotionBackup_2026-09-10_020000
#   .\install-scripts\backup\Restore-HyMotion.ps1 -BackupId PreRestore_2026-09-12_180000
#
# Operator playbooks (no execution from the UI): Platform Console → Operations → Ops playbooks.
#   .\install-scripts\backup\Restore-HyMotion.ps1 -Latest -Force              (skip confirmation - automation/tests only)
#   .\install-scripts\backup\Restore-HyMotion.ps1 -Latest -SkipSafetyBackup    (support-only; no rollback point)
#
# -Latest restores the newest Healthy HyMotionBackup_* only (Failed and Partial are skipped).
# -BackupId is the folder / manifest id (HyMotionBackup_* or PreRestore_*). No quoting needed.
# Failed backups are refused even by -BackupId (integrity check). Partial may restore if hashes match.
#
# Exit codes: 0 = finished and /health returned 200, 1 = error, 3 = confirmation cancelled.
#
# Flow: validate backup -> confirm -> pre-restore safety backup -> stop service -> restore DB
# (single-user, REPLACE) -> restore uploads (swap with rollback kept) -> start service ->
# health-check -> validate core tables -> report.

param(
    [string]$BackupId,
    [switch]$Latest,
    [switch]$Force,
    [switch]$SkipSafetyBackup,
    [string]$ServiceName = "HyMotion",
    [string]$HealthUrl = "http://localhost:7140/health"
)

. "$PSScriptRoot\BackupCommon.ps1"

$currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltinRole]::Administrator)) {
    Write-Host "This script must be run as Administrator (stops/starts the Windows Service and restores the database)." -ForegroundColor Red
    exit 1
}

function Get-RollbackHint {
    $safety = @(Get-AllBackups | Where-Object { $_.backupId -like "PreRestore_*" } | Select-Object -First 1)
    $id = $null
    if ($safety.Count -gt 0) { $id = $safety[0].backupId }
    $scriptRel = ".\install-scripts\backup\Restore-HyMotion.ps1"
    if ($id) {
        return "Rollback (Administrator, from the GMS.Api.exe folder): $scriptRel -BackupId $id"
    }
    return "No PreRestore_* folder was found under $BackupsRoot."
}

# ── 1. Resolve target backup ──
$catalog = @(Get-AllBackups)
$nightly = @($catalog | Where-Object { $_.backupId -like "HyMotionBackup_*" })
if ($Latest) {
    $target = $nightly | Where-Object { $_.status -eq "Healthy" } | Select-Object -First 1
    if (-not $target) { Write-BackupLog "No Healthy backup exists to restore from." "ERROR"; exit 1 }
} elseif ($BackupId) {
    $target = $catalog | Where-Object { $_.backupId -eq $BackupId } | Select-Object -First 1
    if (-not $target) { Write-BackupLog "Backup '$BackupId' not found under $BackupsRoot." "ERROR"; exit 1 }
} else {
    Write-Host "Specify -Latest or -BackupId <id>. Available backups:" -ForegroundColor Yellow
    $catalog | Select-Object backupId, status, createdAtLocal | Format-Table -AutoSize
    exit 1
}

# ── 2. Validate integrity NOW, not just trust the manifest's old status ──
$check = Test-BackupIntegrity -BackupFolder $target.folderPath
if (-not $check.Ok) {
    Write-BackupLog "Refusing to restore '$($target.backupId)' - integrity check failed: $($check.Problems -join '; ')" "ERROR"
    exit 1
}
Write-BackupLog "Backup '$($target.backupId)' passed integrity re-check."

# ── 3. Show details, require confirmation ──
Write-Host ""
Write-Host "You are about to restore:" -ForegroundColor Cyan
Write-Host "  Backup:          $($target.backupId)"
Write-Host "  Created:         $($target.createdAtLocal)"
Write-Host "  Backup type:     $($target.backupType)"
Write-Host "  Database:        $($target.database.name)  ($([math]::Round($target.database.sizeBytes/1MB,1)) MB, schema $($target.database.schemaVersion))"
Write-Host "  Uploaded files:  $($target.uploads.fileCount) files ($([math]::Round($target.uploads.sizeBytes/1MB,2)) MB)"
Write-Host ""
Write-Host "Current data on this machine may be REPLACED. A pre-restore safety backup will be taken first" -ForegroundColor Yellow
Write-Host "(unless -SkipSafetyBackup was passed), so this can itself be undone if something goes wrong." -ForegroundColor Yellow
Write-Host ""
if (-not $Force) {
    $answer = Read-Host "Type RESTORE to continue, anything else to cancel"
    if ($answer -ne "RESTORE") { Write-Host "Cancelled." -ForegroundColor Yellow; exit 3 }
}

# ── 4. Pre-restore safety backup (run BEFORE taking the restore lock - it manages its own lock) ──
if (-not $SkipSafetyBackup) {
    Write-BackupLog "Taking pre-restore safety backup of current state..."
    & "$PSScriptRoot\Backup-HyMotion.ps1" -Type PreRestore -Reason "Automatic safety backup before restoring $($target.backupId)"
    $safetyExit = $LASTEXITCODE
    if ($safetyExit -ne 0 -and $safetyExit -ne 2) {
        Write-BackupLog "Pre-restore safety backup FAILED (exit $safetyExit). Refusing to proceed with a destructive restore without one." "ERROR"
        Write-Host "Re-run with -SkipSafetyBackup only if you explicitly accept the risk of no rollback point." -ForegroundColor Red
        exit 1
    }
    Write-BackupLog "Pre-restore safety backup completed (exit $safetyExit)."
} else {
    Write-BackupLog "SkipSafetyBackup requested - proceeding WITHOUT a fresh safety backup. Current state is not separately protected." "WARN"
}

try {
    Enter-BackupLock -Owner "restore:$($target.backupId)"
} catch {
    Write-BackupLog $_.Exception.Message "ERROR"
    exit 1
}

$restoreLog = [ordered]@{
    restoredBackupId = $target.backupId
    startedAtUtc     = (Get-Date).ToUniversalTime().ToString("o")
    steps            = @()
    result           = "Running"
}
function Add-Step { param([string]$Name, [bool]$Ok, [string]$Detail = "")
    $restoreLog.steps += [ordered]@{ step = $Name; ok = $Ok; detail = $Detail; atUtc = (Get-Date).ToUniversalTime().ToString("o") }
    Write-BackupLog "[$Name] $(if ($Ok) {'OK'} else {'FAILED'}) $Detail" $(if ($Ok) { "INFO" } else { "ERROR" })
}

try {
    $connInfo = Get-HyMotionConnectionInfo

    # ── 5. Stop the service so nothing writes during restore ──
    $svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($svc -and $svc.Status -ne "Stopped") {
        Stop-Service -Name $ServiceName -Force
        (Get-Service -Name $ServiceName).WaitForStatus("Stopped", (New-TimeSpan -Seconds 60))
        Add-Step "StopService" $true
    } elseif ($svc) {
        Add-Step "StopService" $true "already stopped"
    } else {
        Add-Step "StopService" $true "service '$ServiceName' not installed"
        Write-BackupLog "Windows service '$ServiceName' was not found. Restore will still run; start GMS.Api yourself after." "WARN"
    }

    # ── 6. Restore database (single-user, REPLACE - safe standard pattern) ──
    $dbBakPath = Join-Path $target.folderPath "database.bak"
    $dbName = $connInfo.Database
    $dbLit = Escape-SqlLiteral $dbName
    $dbIdent = $dbName.Replace("]", "]]")
    $bakLit = Escape-SqlLiteral $dbBakPath
    try {
        $conn = New-SqlConnection -Server $connInfo.Server -InitialCatalog "master"
        Invoke-SqlNonQuery $conn "IF DB_ID(N'$dbLit') IS NOT NULL ALTER DATABASE [$dbIdent] SET SINGLE_USER WITH ROLLBACK IMMEDIATE" -TimeoutSeconds 60 | Out-Null
        Invoke-SqlNonQuery $conn "RESTORE DATABASE [$dbIdent] FROM DISK = N'$bakLit' WITH REPLACE, STATS = 10" -TimeoutSeconds 1800 | Out-Null
        Invoke-SqlNonQuery $conn "ALTER DATABASE [$dbIdent] SET MULTI_USER" -TimeoutSeconds 60 | Out-Null
        $conn.Close()
        Add-Step "RestoreDatabase" $true "Restored from $($target.backupId)"
    } catch {
        Add-Step "RestoreDatabase" $false $_.Exception.Message
        try {
            $recoverConn = New-SqlConnection -Server $connInfo.Server -InitialCatalog "master"
            Invoke-SqlNonQuery $recoverConn "IF DB_ID(N'$dbLit') IS NOT NULL ALTER DATABASE [$dbIdent] SET MULTI_USER" -TimeoutSeconds 60 | Out-Null
            $recoverConn.Close()
        } catch { }
        $restoreLog.result = "Failed"
        Save-RestoreLog -RestoreLog $restoreLog
        Write-Host "RESTORE FAILED at the database step. The database may be left in an inconsistent state." -ForegroundColor Red
        Write-Host (Get-RollbackHint) -ForegroundColor Yellow
        exit 1
    }

    # ── 7. Restore uploads (extract to temp, then swap - keep the old copy as rollback rather
    #      than deleting it outright) ──
    $tempExtract = "$UploadsDir.restore-tmp"
    $rollback = "$UploadsDir.rollback-$(Get-Date -Format 'yyyyMMddHHmmss')"
    try {
        if (Test-Path $tempExtract) { Remove-Item $tempExtract -Recurse -Force }
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [System.IO.Compression.ZipFile]::ExtractToDirectory((Join-Path $target.folderPath "uploads.zip"), $tempExtract)

        if (Test-Path $UploadsDir) { Rename-Item -Path $UploadsDir -NewName (Split-Path $rollback -Leaf) -Force }
        Rename-Item -Path $tempExtract -NewName (Split-Path $UploadsDir -Leaf) -Force
        icacls $UploadsDir /grant "NT AUTHORITY\NETWORK SERVICE:(OI)(CI)M" | Out-Null
        Add-Step "RestoreUploads" $true "Previous uploads preserved at $rollback"
    } catch {
        Add-Step "RestoreUploads" $false $_.Exception.Message
        if ((Test-Path $rollback) -and -not (Test-Path $UploadsDir)) {
            Rename-Item -Path $rollback -NewName (Split-Path $UploadsDir -Leaf) -Force -ErrorAction SilentlyContinue
        }
        $restoreLog.result = "Failed"
        Save-RestoreLog -RestoreLog $restoreLog
        Write-Host "RESTORE FAILED at the uploads step. Database WAS restored; uploads were not. Original uploads folder preserved where possible." -ForegroundColor Red
        Write-Host (Get-RollbackHint) -ForegroundColor Yellow
        exit 1
    }

    # ── 8. Start service, wait for health ──
    $healthy = $false
    $healthDetail = ""
    if ($svc) {
        try {
            Start-Service -Name $ServiceName
        } catch {
            $healthDetail = "Start-Service failed: $($_.Exception.Message)"
            Write-BackupLog $healthDetail "ERROR"
        }
    } else {
        $healthDetail = "service '$ServiceName' not installed; checking $HealthUrl only"
    }
    if (-not $healthDetail.StartsWith("Start-Service failed")) {
        for ($i = 0; $i -lt 30; $i++) {
            Start-Sleep -Seconds 2
            try {
                $resp = Invoke-WebRequest -Uri $HealthUrl -UseBasicParsing -TimeoutSec 5
                if ($resp.StatusCode -eq 200) { $healthy = $true; break }
            } catch { }
        }
        if ($healthy) { $healthDetail = "Health endpoint responded 200" }
        elseif (-not $healthDetail) { $healthDetail = "Health endpoint did not respond within 60s" }
        else { $healthDetail = $healthDetail + "; health endpoint did not respond within 60s" }
    }
    Add-Step "StartServiceAndHealthCheck" $healthy $healthDetail

    # ── 9. Validate core tables are queryable post-restore (post-migration, since the service's
    #      own startup just ran EF migrations forward per the app's existing strategy) ──
    $tableChecks = @{}
    $tablesOk = $false
    try {
        $conn = New-SqlConnection -Server $connInfo.Server -InitialCatalog $dbName
        foreach ($t in @("tenants", "app_users", "gym_members", "sales", "gym_attendance")) {
            try {
                $exists = Invoke-SqlScalar $conn "SELECT CASE WHEN OBJECT_ID('dbo.$t','U') IS NOT NULL THEN 1 ELSE 0 END"
                if ($exists -eq 1) {
                    $count = Invoke-SqlScalar $conn "SELECT COUNT(*) FROM [dbo].[$t]"
                    $tableChecks[$t] = "OK ($count rows)"
                } else {
                    $tableChecks[$t] = "table not found (may not exist in this schema version)"
                }
            } catch {
                $tableChecks[$t] = "FAILED: $($_.Exception.Message)"
            }
        }
        $conn.Close()
        $tablesOk = ($tableChecks.Values | Where-Object { $_ -like "FAILED*" }).Count -eq 0
        $tableDetail = ($tableChecks.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" }) -join "; "
        Add-Step "ValidateCoreTables" $tablesOk $tableDetail
    } catch {
        Add-Step "ValidateCoreTables" $false $_.Exception.Message
    }

    $restoreLog.result = $(if ($healthy -and $tablesOk) { "Succeeded" } elseif ($healthy) { "SucceededWithWarnings" } else { "Failed" })
    $restoreLog.finishedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
    Save-RestoreLog -RestoreLog $restoreLog

    Write-Host ""
    Write-Host "Restore finished: $($restoreLog.result)" -ForegroundColor $(if ($healthy -and $tablesOk) { "Green" } elseif ($healthy) { "Yellow" } else { "Red" })
    foreach ($s in $restoreLog.steps) { Write-Host "  [$($s.step)] $(if ($s.ok) {'OK'} else {'FAILED'}) $($s.detail)" }
    exit $(if ($healthy -and $tablesOk) { 0 } else { 1 })
} finally {
    Exit-BackupLock
}
