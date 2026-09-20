param(
    [string]$Kit = "E:\HyMotionFieldKit",
    [string]$PublishDir = "D:\GMS\GMS\publish-local"
)

$ErrorActionPreference = "Stop"
$kit = $Kit
$publish = $PublishDir
$scripts = "D:\GMS\GMS\scripts\local-install"
$old = "E:\HyMotion"

if (-not (Test-Path "E:\")) { throw "USB E: is not ready." }
if (-not (Test-Path $publish)) { throw "Missing $publish" }

New-Item -ItemType Directory -Force -Path @(
  "$kit\01-sql-express",
  "$kit\02-hymotion-setup",
  "$kit\03-fallback-app",
  "$kit\04-install-scripts",
  "$kit\04-install-scripts\backup",
  "$kit\05-take-home-backups"
) | Out-Null

Write-Host "==> 01 SQL (from E:\HyMotion if present)..." -ForegroundColor Cyan
if (Test-Path "$old\SQL2022-SSEI-Expr.exe") {
  Copy-Item "$old\SQL2022-SSEI-Expr.exe" "$kit\01-sql-express\" -Force
}
if (Test-Path "$old\SQL2025-SSEI-Expr.exe") {
  Copy-Item "$old\SQL2025-SSEI-Expr.exe" "$kit\01-sql-express\" -Force
}

Write-Host "==> 02 old Inno setup if present..." -ForegroundColor Cyan
if (Test-Path "$old\HyMotionLocalSetup-1.0.0.exe") {
  Copy-Item "$old\HyMotionLocalSetup-1.0.0.exe" "$kit\02-hymotion-setup\" -Force
}

Write-Host "==> 03 app from $publish (this takes a minute)..." -ForegroundColor Cyan
Copy-Item "$publish\*" "$kit\03-fallback-app\" -Recurse -Force

Write-Host "==> 04 scripts..." -ForegroundColor Cyan
Copy-Item "$scripts\Test-SqlServerAvailability.ps1" "$kit\04-install-scripts\" -Force
Copy-Item "$scripts\install-service.ps1" "$kit\04-install-scripts\" -Force
Copy-Item "$scripts\uninstall-service.ps1" "$kit\04-install-scripts\" -Force
Copy-Item "$scripts\backup\*" "$kit\04-install-scripts\backup\" -Force

Write-Host "==> Setup exe at USB kit root..." -ForegroundColor Cyan
Copy-Item "$publish\HyMotionSetup.exe" "$kit\HyMotionSetup.exe" -Force
Copy-Item "$publish\HyMotionLauncher.exe" "$kit\HyMotionLauncher.exe" -Force
if (Test-Path "$publish\HyMotionBackup.exe") {
  Copy-Item "$publish\HyMotionBackup.exe" "$kit\HyMotionBackup.exe" -Force
}

Write-Host ""
Write-Host "USB kit ready: $kit" -ForegroundColor Green
Get-ChildItem $kit | Select-Object Mode, Name | Format-Table -AutoSize
Write-Host "GMS.Api.exe: $(Test-Path "$kit\03-fallback-app\GMS.Api.exe")"
Write-Host "HyMotionSetup.exe root: $(Test-Path "$kit\HyMotionSetup.exe")"
Write-Host "Restore script: $(Test-Path "$kit\04-install-scripts\backup\Restore-HyMotion.ps1")"
Get-ChildItem "$kit\01-sql-express" | Select-Object Name, Length
