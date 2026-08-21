# Publish GymFlowPro for MonsterASP (single IIS site: API + staff dashboard)
# Usage: .\scripts\publish-monsterasp.ps1 [-OutputDir .\publish]

param(
    [string]$OutputDir = "$PSScriptRoot\..\publish"
)

$ErrorActionPreference = "Stop"
$Root = Split-Path $PSScriptRoot -Parent
$FrontendWeb = Join-Path $Root "Frontend\apps\web"
$ApiProject = Join-Path $Root "GMS.Api\GMS.Api.csproj"

Write-Host "==> Preparing wwwroot from frontend..." -ForegroundColor Cyan
Push-Location $FrontendWeb
node scripts/prepare-wwwroot.mjs
Pop-Location

Write-Host "==> dotnet publish (Release)..." -ForegroundColor Cyan
dotnet publish $ApiProject -c Release -o $OutputDir

Write-Host ""
Write-Host "Publish output: $OutputDir" -ForegroundColor Green
Write-Host "Upload the contents of that folder to your MonsterASP website root." -ForegroundColor Green
Write-Host ""
Write-Host "Set these environment variables in the MonsterASP control panel:" -ForegroundColor Yellow
Write-Host "  ASPNETCORE_ENVIRONMENT=Production"
Write-Host "  ConnectionStrings__DefaultConnection=<MonsterASP SQL connection string>"
Write-Host "  JwtSettings__SecretKey=<random 64+ char secret>"
Write-Host "  EmailSettings__SmtpHost / SmtpUser / SmtpPassword / FromAddress"
Write-Host "  MemberAppActivation__CodePepper=<random secret>"
Write-Host "  PlatformSeed__Email / PlatformSeed__Password (optional, first platform admin)"
Write-Host ""
Write-Host "Site: ASP.NET Core 8, in-process. Browse https://your-domain/dashboard/" -ForegroundColor Cyan
