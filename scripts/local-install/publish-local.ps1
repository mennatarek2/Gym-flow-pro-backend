# Publishes HyMotion Local Lifetime Edition as a self-contained win-x64 deployment.
# No .NET runtime, Node.js, or Visual Studio is required on the TARGET machine - only on the
# machine running this script (build time).
#
# Usage: .\scripts\local-install\publish-local.ps1 [-OutputDir .\publish-local]
#
# IMPORTANT: prepare-wwwroot.mjs is run explicitly, BEFORE `dotnet publish`, and must stay that
# way. GMS.Api.csproj's own PrepareWebDashboardBeforePublish target also runs it, but wwwroot's
# Content items are globbed at MSBuild EVALUATION time - before that target's Exec ever runs - so
# on a clean checkout (or after adding a new wwwroot subfolder, as shared/vendor was in Phase 2)
# anything the target's own Exec writes is silently dropped from the publish output. Running the
# node script here first means wwwroot is fully populated before `dotnet publish` starts, so
# MSBuild's evaluation-time glob sees everything. See the long comment on that csproj target.

param(
    [string]$OutputDir = "$PSScriptRoot\..\..\publish-local"
)

$ErrorActionPreference = "Stop"
$Root = Resolve-Path "$PSScriptRoot\..\.."
$FrontendWeb = Join-Path $Root "Frontend\apps\web"
$ApiProject = Join-Path $Root "GMS.Api\GMS.Api.csproj"

Write-Host "==> [1/2] Preparing wwwroot from frontend (fonts, icons, chart.js, jsbarcode, signalr, dashboard)..." -ForegroundColor Cyan
Push-Location $FrontendWeb
node scripts/prepare-wwwroot.mjs
Pop-Location

Write-Host "==> [2/3] Publishing self-contained win-x64 (Release)..." -ForegroundColor Cyan
dotnet publish $ApiProject -c Release -r win-x64 --self-contained true -o $OutputDir

Write-Host "==> [3/3] Removing files Local Edition never needs (Phase 3.1 release-artifact scan)..." -ForegroundColor Cyan
# Local only ever loads appsettings.json + appsettings.Local.json (+ the ProgramData override) -
# the other three environment files carry no secrets (verified: every sensitive field in them is
# an empty placeholder, same convention as elsewhere in this repo) but are still SaaS/dev-only
# clutter this installer doesn't need to ship. Debug symbols (.pdb) and the internal
# /dev/font-preview typography-comparison tool (Phase 2: not part of the served Local product
# surface) are likewise dropped from the Local package only - none of this touches
# GMS.Api.csproj's own publish output used for the SaaS/MonsterASP build.
$filesToRemove = @("appsettings.Development.json", "appsettings.Staging.json", "appsettings.Production.json")
foreach ($f in $filesToRemove) {
    $p = Join-Path $OutputDir $f
    if (Test-Path $p) { Remove-Item $p -Force }
}
Get-ChildItem -Path $OutputDir -Filter "*.pdb" | Remove-Item -Force
$devToolPath = Join-Path $OutputDir "wwwroot\dev"
if (Test-Path $devToolPath) { Remove-Item $devToolPath -Recurse -Force }

$installScriptsDest = Join-Path $OutputDir "install-scripts"
New-Item -ItemType Directory -Force -Path $installScriptsDest | Out-Null
Copy-Item (Join-Path $PSScriptRoot "install-service.ps1") $installScriptsDest -Force
Copy-Item (Join-Path $PSScriptRoot "uninstall-service.ps1") $installScriptsDest -Force
Copy-Item (Join-Path $PSScriptRoot "Test-SqlServerAvailability.ps1") $installScriptsDest -Force

$backupScriptsSrc = Join-Path $Root "scripts\local-install\backup"
$backupScriptsDest = Join-Path $OutputDir "install-scripts\backup"
if (Test-Path $backupScriptsSrc) {
    New-Item -ItemType Directory -Force -Path $backupScriptsDest | Out-Null
    Copy-Item -Path (Join-Path $backupScriptsSrc "*") -Destination $backupScriptsDest -Force
    Write-Host "==> Copied backup scripts to $backupScriptsDest" -ForegroundColor Cyan
} else {
    Write-Host "WARNING: backup scripts folder missing: $backupScriptsSrc" -ForegroundColor Yellow
}

Write-Host "==> [4/4] Publishing HyMotion Setup + desktop launcher + backup app..." -ForegroundColor Cyan
$desktopRoot = Join-Path $Root "tools\HyMotion.Desktop"
$setupProj = Join-Path $desktopRoot "HyMotion.Setup\HyMotion.Setup.csproj"
$launchProj = Join-Path $desktopRoot "HyMotion.Launcher\HyMotion.Launcher.csproj"
$backupProj = Join-Path $desktopRoot "HyMotion.Backup\HyMotion.Backup.csproj"
$desktopOut = Join-Path $OutputDir "_desktop-build"
if ((Test-Path $setupProj) -and (Test-Path $launchProj) -and (Test-Path $backupProj)) {
    New-Item -ItemType Directory -Force -Path $desktopOut | Out-Null
    dotnet publish $setupProj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o $desktopOut
    dotnet publish $launchProj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o $desktopOut
    dotnet publish $backupProj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o $desktopOut
    Copy-Item (Join-Path $desktopOut "HyMotionSetup.exe") $OutputDir -Force
    Copy-Item (Join-Path $desktopOut "HyMotionLauncher.exe") $OutputDir -Force
    Copy-Item (Join-Path $desktopOut "HyMotionBackup.exe") $OutputDir -Force
    Remove-Item $desktopOut -Recurse -Force
    Write-Host "==> Desktop apps: HyMotionSetup.exe + HyMotionLauncher.exe + HyMotionBackup.exe" -ForegroundColor Cyan
} else {
    Write-Host "WARNING: HyMotion.Desktop projects missing under tools\HyMotion.Desktop" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "Publish output: $OutputDir" -ForegroundColor Green
Write-Host "This folder needs no .NET runtime, Node.js, or Visual Studio on the target machine." -ForegroundColor Green
Write-Host ""
Write-Host "Before first run on the target machine:" -ForegroundColor Yellow
Write-Host "  1. Ensure SQL Server Express is installed (see scripts/local-install/Test-SqlServerAvailability.ps1)."
Write-Host "  2. Set ASPNETCORE_ENVIRONMENT=Local (Windows Service config does this - see install-service.ps1)."
Write-Host "  3. Supply JwtSettings__SecretKey / EncryptionKey / *_CodePepper via environment variables"
Write-Host "     OR let the app generate+persist them itself on first start (see LocalSecretProvider)."
Write-Host "  4. Run scripts/local-install/install-service.ps1 to register + start the Windows Service."
