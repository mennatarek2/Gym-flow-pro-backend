# Registers HyMotion Local Lifetime Edition as a Windows Service.
# Must be run elevated (Administrator). Uses the built-in Service Control Manager (sc.exe) - no
# custom service-wrapper code, per the Phase 3 brief ("do not implement a custom homemade service
# wrapper unless necessary"); GMS.Api itself calls UseWindowsService() so it natively understands
# SCM start/stop signals once registered this way.
#
# Usage: .\install-service.ps1                                    (uses publish-local.ps1's default output folder)
#        .\install-service.ps1 -InstallDir "C:\Program Files\HyMotion\app"
#
# Run .\publish-local.ps1 FIRST if you have not already - this script only registers a service
# pointing at an already-published build, it does not build one.

param(
    # Defaults to the exact same folder publish-local.ps1 writes to by default, so running both
    # scripts back to back with no arguments works without having to know/match a path by hand.
    # (When building the actual installer, publish-local.ps1 is instead pointed at a different
    # -OutputDir - "publish-output" - matching what HyMotionLocal.iss expects; that workflow
    # doesn't use this script directly, so its default here is unaffected.)
    [string]$InstallDir = "$PSScriptRoot\..\..\publish-local",
    [string]$ServiceName = "HyMotion",
    [string]$DisplayName = "HyMotion Local"
)

$ErrorActionPreference = "Stop"
# Trim: a value typed at PowerShell's "Supply values for the following parameters" prompt can
# easily pick up a trailing space, which Join-Path does not trim - producing a subtly-broken path
# like "...publish-local \GMS.Api.exe" that's easy to miss when reading the error.
$InstallDir = $InstallDir.Trim()

$currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltinRole]::Administrator)) {
    Write-Host "This script must be run as Administrator (service registration requires it)." -ForegroundColor Red
    exit 1
}

$exePath = Join-Path $InstallDir "GMS.Api.exe"
if (-not (Test-Path $exePath)) {
    Write-Host "GMS.Api.exe was not found at $exePath" -ForegroundColor Red
    Write-Host ""
    Write-Host "Publish first, then re-run this script (with no arguments, if using the default folder):" -ForegroundColor Yellow
    Write-Host "  .\publish-local.ps1"
    Write-Host "  .\install-service.ps1"
    exit 1
}

# Least-privileged practical account: NETWORK SERVICE can bind a loopback port and (once ACL'd
# below) write to %ProgramData%\HyMotion, without the broad rights LocalSystem would have. It is
# NOT run as Administrator - see the Phase 3 report's Security section for what was verified vs.
# what a real installer must still confirm on a clean target machine.
# Must match Test-SqlServerAvailability.ps1's -ServiceAccount default exactly, so the SQL login it
# creates during the SQL check is the same identity this service actually runs as. Both forms
# ("NT AUTHORITY\NetworkService" and "NT AUTHORITY\NETWORK SERVICE") resolve to the same
# well-known SID (S-1-5-20), but kept identical here to avoid any doubt.
$serviceAccount = "NT AUTHORITY\NETWORK SERVICE"

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "Service '$ServiceName' already exists - stopping and removing it first." -ForegroundColor Yellow
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 2
}

Write-Host "==> Creating service '$ServiceName'..." -ForegroundColor Cyan
$binPath = "`"$exePath`""
sc.exe create $ServiceName binPath= $binPath start= auto obj= $serviceAccount DisplayName= $DisplayName | Out-Null
sc.exe description $ServiceName "HyMotion Local Lifetime Edition - local gym management (offline)." | Out-Null

# ASPNETCORE_ENVIRONMENT=Local for this service only, via the per-service Environment registry
# value (sc.exe has no direct flag for this) - never affects any other process on the machine.
$regKey = "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName"
New-ItemProperty -Path $regKey -Name "Environment" -PropertyType MultiString -Value @("ASPNETCORE_ENVIRONMENT=Local") -Force | Out-Null

# Recovery: restart on failure rather than leaving the gym offline until someone notices (Phase 3
# "Service Recovery" requirement) - uses SCM's own recovery mechanism, not a custom restart loop.
sc.exe failure $ServiceName reset= 86400 actions= restart/60000/restart/60000/restart/300000 | Out-Null

Write-Host "==> Granting $serviceAccount access to %ProgramData%\HyMotion..." -ForegroundColor Cyan
$programDataHyMotion = Join-Path $env:ProgramData "HyMotion"
New-Item -ItemType Directory -Path $programDataHyMotion -Force | Out-Null
icacls $programDataHyMotion /grant "NT AUTHORITY\NETWORK SERVICE:(OI)(CI)M" | Out-Null

Write-Host "==> Starting service..." -ForegroundColor Cyan
Start-Service -Name $ServiceName
Start-Sleep -Seconds 2
Get-Service -Name $ServiceName | Format-Table -AutoSize

Write-Host ""
Write-Host "Service '$ServiceName' installed and started." -ForegroundColor Green
Write-Host "Logs: %ProgramData%\HyMotion\logs" -ForegroundColor Green
Write-Host "To uninstall: .\uninstall-service.ps1" -ForegroundColor Green
