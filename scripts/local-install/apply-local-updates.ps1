# Applies latest Frontend + Backend to HyMotion Local Edition (publish-local + Windows service).
# Run elevated (Administrator). Usage:
#   powershell -ExecutionPolicy Bypass -File .\scripts\local-install\apply-local-updates.ps1

$ErrorActionPreference = "Stop"
$Root = Resolve-Path "$PSScriptRoot\..\.."
$PublishScript = Join-Path $PSScriptRoot "publish-local.ps1"
$OutputDir = Join-Path $Root "publish-local"

Write-Host "==> Stopping HyMotion Local service..." -ForegroundColor Cyan
Stop-Service -Name "HyMotion" -Force -ErrorAction SilentlyContinue
$deadline = (Get-Date).AddSeconds(45)
do {
    Start-Sleep -Seconds 1
    $svc = Get-Service -Name "HyMotion" -ErrorAction SilentlyContinue
} while ($svc -and $svc.Status -ne "Stopped" -and (Get-Date) -lt $deadline)

if ((Get-Service HyMotion).Status -ne "Stopped") {
    throw "Could not stop HyMotion service. Close any open handles and retry as Administrator."
}
Write-Host "    Service stopped." -ForegroundColor Green

# Also kill stray GMS.Api that lock build outputs (SaaS debug on 5000/5001).
Get-Process -Name "GMS.Api" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 1

Write-Host "==> Publishing Local Edition (frontend wwwroot + backend Release)..." -ForegroundColor Cyan
& $PublishScript -OutputDir $OutputDir

Write-Host "==> Starting HyMotion Local service..." -ForegroundColor Cyan
Start-Service -Name "HyMotion"
$deadline = (Get-Date).AddSeconds(45)
do {
    Start-Sleep -Seconds 1
    $svc = Get-Service -Name "HyMotion" -ErrorAction SilentlyContinue
} while ($svc -and $svc.Status -ne "Running" -and (Get-Date) -lt $deadline)

$final = Get-Service HyMotion
if ($final.Status -ne "Running") {
    throw "HyMotion service did not start. Status=$($final.Status)"
}

Write-Host ""
Write-Host "Local Edition updated and running." -ForegroundColor Green
Write-Host "Open: http://localhost:7140/dashboard/" -ForegroundColor Green
Write-Host "Hard-refresh the browser (Ctrl+F5)." -ForegroundColor Yellow
