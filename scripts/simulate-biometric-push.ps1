# HyMotion biometric push simulation (no hardware)
# 1) In UI: map employee Device User ID = 1001
# 2) Fill $DeviceId + $ApiKey (from device create/rotate modal)
param(
  [Parameter(Mandatory=$true)][string]$DeviceId,
  [Parameter(Mandatory=$true)][string]$ApiKey,
  [string]$DeviceUserId = "1001",
  [string]$BaseUrl = "http://127.0.0.1:7140"
)
$headers = @{
  "X-HyMotion-Device-Id" = $DeviceId
  "X-HyMotion-Device-Key" = $ApiKey
  "Content-Type" = "application/json"
}
function Send-Punch([string]$tag) {
  $body = @{
    events = @(
      @{
        vendorEventId = "$tag-" + [guid]::NewGuid().ToString("N").Substring(0,8)
        deviceUserId = $DeviceUserId
        deviceTimestamp = [DateTime]::UtcNow.ToString("o")
        punchDirection = "Unknown"
        safePayloadJson = '{"sim":true}'
      }
    )
  } | ConvertTo-Json -Depth 5
  Invoke-RestMethod -Uri "$BaseUrl/api/local/biometric/events" -Method POST -Headers $headers -Body $body
}
Write-Host "Sim check-in..."
Send-Punch "SIM-IN" | ConvertTo-Json -Depth 5
Start-Sleep -Seconds 2
Write-Host "Sim check-out..."
Send-Punch "SIM-OUT" | ConvertTo-Json -Depth 5
Write-Host "Done. Open /dashboard/hr/biometric-events/ and /dashboard/hr/attendance/"
