# Elevated cutover: stop HyMotion, copy publish-local-verify -> publish-local, start service.
# Run as Administrator:
#   powershell -ExecutionPolicy Bypass -File D:\GMS\GMS\scripts\local-install\cutover-accesscards-verify.ps1
$ErrorActionPreference = "Stop"
$Root = "D:\GMS\GMS"
$Live = Join-Path $Root "publish-local"
$Verify = Join-Path $Root "publish-local-verify"
$Log = Join-Path $Root "accesscard-service-cutover.log"

function Log([string]$m) {
  $line = "$(Get-Date -Format o) $m"
  Add-Content -Path $Log -Value $line
  Write-Host $line
}

try {
  if (-not (Test-Path (Join-Path $Verify "GMS.Api.exe"))) {
    throw "Missing verify publish at $Verify\GMS.Api.exe. First run: publish-local.ps1 -OutputDir publish-local-verify"
  }

  Log "Stopping HyMotion..."
  Stop-Service -Name HyMotion -Force -ErrorAction SilentlyContinue
  $deadline = (Get-Date).AddSeconds(60)
  do {
    Start-Sleep -Seconds 1
    $svc = Get-Service HyMotion -ErrorAction SilentlyContinue
  } while ($svc -and $svc.Status -ne "Stopped" -and (Get-Date) -lt $deadline)

  Get-Process -Name "GMS.Api" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
  Start-Sleep -Seconds 2

  if ((Get-Service HyMotion).Status -ne "Stopped") {
    throw "Could not stop HyMotion. Re-run this script in an elevated (Administrator) PowerShell."
  }
  Log "Service stopped."

  Log "Copying verify publish to live publish-local..."
  robocopy $Verify $Live /MIR /NFL /NDL /NJH /NJS /nc /ns /np | Out-Null
  $rc = $LASTEXITCODE
  if ($rc -ge 8) { throw "robocopy failed with code $rc" }
  Log "Copy done (robocopy=$rc)."

  Log "Starting HyMotion..."
  Start-Service -Name HyMotion
  $deadline = (Get-Date).AddSeconds(60)
  do {
    Start-Sleep -Seconds 1
    $svc = Get-Service HyMotion -ErrorAction SilentlyContinue
  } while ($svc -and $svc.Status -ne "Running" -and (Get-Date) -lt $deadline)

  if ((Get-Service HyMotion).Status -ne "Running") {
    throw "HyMotion did not start. Status=$((Get-Service HyMotion).Status)"
  }
  Log "HyMotion Running. Open http://localhost:7140/dashboard/access-cards/"
  exit 0
} catch {
  Log "FAILED: $($_.Exception.Message)"
  exit 1
}
