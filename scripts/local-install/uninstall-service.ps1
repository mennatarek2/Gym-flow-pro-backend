# Removes the HyMotion Windows Service (application binaries and service registration only).
#
# IMPORTANT: this NEVER touches %ProgramData%\HyMotion (config, uploads, secrets, logs, database
# connection details) - gym data must survive an uninstall by default. Pass -RemoveAllData plus
# explicit confirmation to also delete it; there is no way to do that accidentally.
#
# Usage: .\uninstall-service.ps1
#        .\uninstall-service.ps1 -RemoveAllData    (prompts for a typed confirmation)

param(
    [string]$ServiceName = "HyMotion",
    [switch]$RemoveAllData
)

$ErrorActionPreference = "Stop"

$currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltinRole]::Administrator)) {
    Write-Host "This script must be run as Administrator." -ForegroundColor Red
    exit 1
}

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "==> Stopping and removing service '$ServiceName'..." -ForegroundColor Cyan
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    sc.exe delete $ServiceName | Out-Null
    Write-Host "Service removed." -ForegroundColor Green
} else {
    Write-Host "Service '$ServiceName' was not registered - nothing to remove." -ForegroundColor Yellow
}

# Removes the nightly backup Scheduled Task only - never the Backups folder itself (customer
# data, preserved below exactly like uploads/config/secrets/logs).
$backupTaskName = "HyMotion Nightly Backup"
if (Get-ScheduledTask -TaskName $backupTaskName -ErrorAction SilentlyContinue) {
    Write-Host "==> Removing scheduled task '$backupTaskName'..." -ForegroundColor Cyan
    Unregister-ScheduledTask -TaskName $backupTaskName -Confirm:$false
    Write-Host "Scheduled task removed. Existing backups under %ProgramData%\HyMotion\Backups were NOT touched." -ForegroundColor Green
}

$programDataHyMotion = Join-Path $env:ProgramData "HyMotion"
if ($RemoveAllData) {
    if (-not (Test-Path $programDataHyMotion)) {
        Write-Host "No data directory found at $programDataHyMotion." -ForegroundColor Yellow
        exit 0
    }
    Write-Host ""
    Write-Host "WARNING: this will permanently delete ALL gym data:" -ForegroundColor Red
    Write-Host "  - the local database connection config"
    Write-Host "  - uploaded files (member photos, documents, invoices, ...)"
    Write-Host "  - generated secrets (JWT signing key, encryption key)"
    Write-Host "  - logs"
    Write-Host "  - EVERY local backup under Backups\, including verified ones" -ForegroundColor Red
    Write-Host "at: $programDataHyMotion" -ForegroundColor Red
    if (Test-Path (Join-Path $programDataHyMotion "Backups")) {
        $backupCount = (Get-ChildItem (Join-Path $programDataHyMotion "Backups") -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -like "HyMotionBackup_*" }).Count
        Write-Host "  ($backupCount backup(s) currently present - if any were copied to an off-PC destination, those copies are unaffected)" -ForegroundColor Yellow
    }
    Write-Host "(this does NOT delete the SQL Server database itself - only files under ProgramData)" -ForegroundColor Yellow
    $confirm = Read-Host "Type DELETE to confirm"
    if ($confirm -eq "DELETE") {
        Remove-Item -Path $programDataHyMotion -Recurse -Force
        Write-Host "Deleted $programDataHyMotion." -ForegroundColor Green
    } else {
        Write-Host "Confirmation did not match - nothing was deleted." -ForegroundColor Yellow
    }
} else {
    Write-Host ""
    Write-Host "Gym data was preserved at: $programDataHyMotion" -ForegroundColor Green
    Write-Host "Re-run install-service.ps1 with a new/updated build to reinstall without losing it." -ForegroundColor Green
    Write-Host "(to also delete gym data, re-run this script with -RemoveAllData)"
}
