# HyMotion Local - registers the nightly backup Scheduled Task. Run elevated, called by the
# installer's [Run] section after install-service.ps1. Idempotent: safe to re-run on upgrade -
# updates the existing task in place instead of creating a duplicate.
#
# Usage: .\Register-BackupTask.ps1 [-InstallDir <path to app>] [-TimeOfDay "02:00"] [-AllowCurrentUser]

param(
    [string]$InstallDir = "$PSScriptRoot\..\..\..\publish-local",
    [string]$TimeOfDay = "02:00",
    [string]$TaskName = "HyMotion Nightly Backup",
    [switch]$AllowCurrentUser
)

. "$PSScriptRoot\BackupCommon.ps1"

$currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
$isAdmin = $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltinRole]::Administrator)
if (-not $isAdmin -and -not $AllowCurrentUser) {
    Write-Host "This script must be run as Administrator (Scheduled Task registration requires it), or with -AllowCurrentUser." -ForegroundColor Red
    exit 1
}

$backupScript = Join-Path $PSScriptRoot "Backup-HyMotion.ps1"
if (-not (Test-Path $backupScript)) {
    Write-Host "Backup-HyMotion.ps1 not found next to this script ($PSScriptRoot) - install is incomplete." -ForegroundColor Red
    exit 1
}

Write-Host "==> Preparing backup directories and permissions..." -ForegroundColor Cyan
New-Item -ItemType Directory -Path $BackupsRoot -Force | Out-Null

# The database was found in FULL recovery model with no transaction-log backups configured -
# that only grows the log file forever for zero point-in-time-recovery benefit, since nothing
# here implements log shipping. SIMPLE + nightly full backups is the appropriate model for this
# product; safe to run every time this script runs (a no-op once already SIMPLE).
try {
    $connInfo = Get-HyMotionConnectionInfo
    $conn = New-SqlConnection -Server $connInfo.Server -InitialCatalog "master"
    $currentModel = Invoke-SqlScalar $conn "SELECT recovery_model_desc FROM sys.databases WHERE name = N'$($connInfo.Database)'"
    if ($currentModel -and $currentModel -ne "SIMPLE") {
        Invoke-SqlNonQuery $conn "ALTER DATABASE [$($connInfo.Database)] SET RECOVERY SIMPLE" -TimeoutSeconds 30 | Out-Null
        Write-Host "==> Switched $($connInfo.Database) from $currentModel to SIMPLE recovery model (nightly full backups are the recovery strategy here, not log shipping)." -ForegroundColor Green
    }
    $conn.Close()
} catch {
    Write-Host "Could not confirm/set SIMPLE recovery model - not fatal, but the transaction log may grow unbounded until this is fixed: $($_.Exception.Message)" -ForegroundColor Yellow
}

# The service account (NETWORK SERVICE) already has Modify on the whole %ProgramData%\HyMotion
# tree from install-service.ps1 - Backups/ inherits that. What's NEW here: SQL Server's own
# process identity (NT SERVICE\MSSQLSERVER, not whoever issues the T-SQL command) is the one that
# physically writes database.bak, so it separately needs write access to this folder or every
# BACKUP DATABASE will fail with an "Operating system error 5(Access is denied)" that has nothing
# to do with the calling account's own permissions.
# Default instance uses NT SERVICE\MSSQLSERVER; Express named instance uses NT SERVICE\MSSQL$SQLEXPRESS.
Get-Service -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -eq "MSSQLSERVER" -or $_.Name -like "MSSQL`$*" } |
    ForEach-Object {
        $sqlServiceAccount = "NT SERVICE\$($_.Name)"
        try {
            icacls $BackupsRoot /grant "${sqlServiceAccount}:(OI)(CI)M" | Out-Null
            Write-Host "==> Granted $sqlServiceAccount write access to $BackupsRoot" -ForegroundColor Green
        } catch {
            Write-Host "Could not grant $sqlServiceAccount access to $BackupsRoot - nightly backups will fail until this is fixed manually: $($_.Exception.Message)" -ForegroundColor Yellow
        }
    }
try {
    icacls $BackupsRoot /grant "NT AUTHORITY\NETWORK SERVICE:(OI)(CI)M" | Out-Null
} catch {
    Write-Host "Could not grant NETWORK SERVICE access to $BackupsRoot (ok for a current-user task): $($_.Exception.Message)" -ForegroundColor DarkYellow
}

if (-not (Test-Path $BackupConfigFile)) {
    Save-BackupConfig -Config ([PSCustomObject]@{ enabled = $true; retentionCount = 14; offPcDestination = $null })
    Write-Host "==> Wrote default backup-config.json (enabled, retain 14, no off-PC destination configured)" -ForegroundColor Green
}

Write-Host "==> Registering Scheduled Task '$TaskName'..." -ForegroundColor Cyan

$action = New-ScheduledTaskAction -Execute "$env:WINDIR\System32\WindowsPowerShell\v1.0\powershell.exe" `
    -Argument "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$backupScript`" -Type Scheduled"

$trigger = New-ScheduledTaskTrigger -Daily -At $TimeOfDay

if ($isAdmin) {
    $principal = New-ScheduledTaskPrincipal -UserId "NT AUTHORITY\NETWORK SERVICE" -LogonType ServiceAccount -RunLevel Limited
} else {
    $principal = New-ScheduledTaskPrincipal -UserId $env:USERNAME -LogonType Interactive -RunLevel Limited
}

$settings = New-ScheduledTaskSettingsSet `
    -StartWhenAvailable `
    -DontStopOnIdleEnd `
    -ExecutionTimeLimit (New-TimeSpan -Hours 3) `
    -MultipleInstances IgnoreNew `
    -RestartCount 2 -RestartInterval (New-TimeSpan -Minutes 10)

$existing = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "==> Task already exists - updating in place (no duplicate created)." -ForegroundColor Yellow
    Set-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings | Out-Null
} else {
    Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings `
        -Description "Nightly HyMotion Local database + uploads backup. Managed by the HyMotion installer - do not delete." | Out-Null
}

# ── Installer self-verification (section 31): don't assume success, prove it ──
$verify = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
$ok = $true
if (-not $verify) { Write-Host "VERIFY FAILED: task does not exist after registration." -ForegroundColor Red; $ok = $false }
else {
    $info = Get-ScheduledTaskInfo -TaskName $TaskName
    $actualAction = ($verify.Actions | Select-Object -First 1).Arguments
    if ($actualAction -notlike "*Backup-HyMotion.ps1*") { Write-Host "VERIFY FAILED: task action does not reference Backup-HyMotion.ps1." -ForegroundColor Red; $ok = $false }
    if ($isAdmin) {
        if ($verify.Principal.UserId -notlike "*NETWORK SERVICE*") { Write-Host "VERIFY FAILED: task principal is not NETWORK SERVICE." -ForegroundColor Red; $ok = $false }
    }
    Write-Host "==> Task verified: next run $($info.NextRunTime), state $($verify.State), principal $($verify.Principal.UserId)" -ForegroundColor Green
}

if (-not (Test-Path $BackupsRoot)) { Write-Host "VERIFY FAILED: backup directory does not exist." -ForegroundColor Red; $ok = $false }
else {
    try {
        $probe = Join-Path $BackupsRoot ".write-test"
        "test" | Set-Content -Path $probe -ErrorAction Stop
        Remove-Item $probe -Force
    } catch {
        Write-Host "VERIFY FAILED: backup directory is not writable by the current process: $($_.Exception.Message)" -ForegroundColor Red
        $ok = $false
    }
}

if ($ok) {
    Write-Host ""
    Write-Host "Backup system installed. Nightly backups run at $TimeOfDay as NT AUTHORITY\NETWORK SERVICE." -ForegroundColor Green
    exit 0
} else {
    Write-Host ""
    Write-Host "Backup system installed with warnings above - review before relying on nightly backups." -ForegroundColor Yellow
    exit 1
}
