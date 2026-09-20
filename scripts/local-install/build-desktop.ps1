# Builds HyMotionSetup.exe + HyMotionLauncher.exe + HyMotionBackup.exe into publish-local (does not rebuild GMS.Api).
# Usage: powershell -ExecutionPolicy Bypass -File .\scripts\local-install\build-desktop.ps1

$ErrorActionPreference = "Stop"
$Root = Resolve-Path "$PSScriptRoot\..\.."
$OutputDir = Join-Path $Root "publish-local"
$desktopRoot = Join-Path $Root "tools\HyMotion.Desktop"
$desktopOut = Join-Path $OutputDir "_desktop-build"

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
New-Item -ItemType Directory -Force -Path $desktopOut | Out-Null

$pubArgs = @(
    "-c", "Release", "-r", "win-x64", "--self-contained", "true",
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:EnableCompressionInSingleFile=true",
    "-o", $desktopOut
)

Write-Host "==> Publishing HyMotion Setup..." -ForegroundColor Cyan
dotnet publish (Join-Path $desktopRoot "HyMotion.Setup\HyMotion.Setup.csproj") @pubArgs
Write-Host "==> Publishing HyMotion Launcher..." -ForegroundColor Cyan
dotnet publish (Join-Path $desktopRoot "HyMotion.Launcher\HyMotion.Launcher.csproj") @pubArgs
Write-Host "==> Publishing HyMotion Backup..." -ForegroundColor Cyan
dotnet publish (Join-Path $desktopRoot "HyMotion.Backup\HyMotion.Backup.csproj") @pubArgs

Copy-Item (Join-Path $desktopOut "HyMotionSetup.exe") $OutputDir -Force
Copy-Item (Join-Path $desktopOut "HyMotionLauncher.exe") $OutputDir -Force
Copy-Item (Join-Path $desktopOut "HyMotionBackup.exe") $OutputDir -Force
Remove-Item $desktopOut -Recurse -Force

Write-Host ""
Write-Host "Copied to $OutputDir" -ForegroundColor Green
Write-Host "  HyMotionSetup.exe     team installer (this Windows user, no Administrator)"
Write-Host "  HyMotionLauncher.exe  gym desktop icon"
Write-Host "  HyMotionBackup.exe    owner Save to USB + Restore"
Write-Host "Double-click HyMotionSetup.exe to add the desktop icon on this PC."
