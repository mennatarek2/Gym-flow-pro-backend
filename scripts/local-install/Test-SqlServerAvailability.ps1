# Detects usable local SQL Server instances for HyMotion Local Lifetime Edition and (optionally)
# writes the discovered connection string into the machine-level config override that
# GMS.Core.Configuration.LocalRuntimePaths.ConfigOverrideFile points at
# (%ProgramData%\HyMotion\config\appsettings.json) - picked up automatically on next start,
# without ever needing to hardcode an instance name into a committed appsettings file.
#
# Local Lifetime Edition uses real SQL Server Express (or any local SQL Server edition - this
# script only distinguishes "is a usable local SQL Server instance available", not which edition;
# see appsettings.Local.json's DatabaseConfig.Description). It does NOT install or bundle SQL
# Server - redistributing it has its own Microsoft licensing terms outside this repo's scope, so
# this script only detects + validates + guides, per the Phase 3 brief. If nothing is found, it
# prints the official download link and exits non-zero so an installer step can react to that.
#
# PRODUCTION FIX (post-3.1): this script previously only proved that the INTERACTIVE INSTALLING
# USER could reach SQL Server via Windows Authentication, then wrote a Trusted_Connection string
# and stopped. The HyMotion Windows Service runs as a completely different Windows security
# principal (NT AUTHORITY\NETWORK SERVICE, see install-service.ps1) which had no SQL Server login
# at all - so the service authenticated fine during the installer's own check and then failed to
# connect the moment it actually started, because nobody had ever granted that identity any SQL
# access. Confirmed directly against a real SQL Server: `SELECT * FROM sys.server_principals`
# showed no entry for NT AUTHORITY\NETWORK SERVICE.
#
# Fix (-WriteConfig only, i.e. a real install, not a bare diagnostic run): while still running as
# the interactive admin (who already has full rights), this script now also pre-creates the
# target database and grants the SERVICE account db_owner - and ONLY db_owner - inside that one
# database. It does NOT grant sysadmin, dbcreator, or any other server-wide role. Pre-creating the
# database here means the running service (with only db_owner, never CREATE DATABASE rights) can
# still successfully run EF migrations and Hangfire's schema init, since both only need to
# create/alter objects INSIDE an already-existing database - never to create the database itself.
# All of this is idempotent (IF NOT EXISTS guards throughout), so re-running on every
# install/upgrade is safe and never duplicates or errors.
#
# Usage:
#   .\Test-SqlServerAvailability.ps1                    # detect + report only
#   .\Test-SqlServerAvailability.ps1 -WriteConfig       # also write the connection string AND grant service access
#   .\Test-SqlServerAvailability.ps1 -DatabaseName HyMotionLocal -ServiceAccount "NT AUTHORITY\NETWORK SERVICE"

# Detects usable local SQL Server instances for HyMotion Local Lifetime Edition and (optionally)
# writes the discovered connection string into the Local Edition config override
# (HYMOTION_DATA_DIR, -ConfigRoot, or %ProgramData%\HyMotion\config\appsettings.json).
#
# Usage:
#   .\Test-SqlServerAvailability.ps1
#   .\Test-SqlServerAvailability.ps1 -WriteConfig
#   .\Test-SqlServerAvailability.ps1 -WriteConfig -ServiceAccount "PC\GymUser" -ConfigRoot "$env:LOCALAPPDATA\HyMotion"

param(
    [switch]$WriteConfig,
    [string]$DatabaseName = "HyMotionLocal",
    [string]$ServiceAccount = "NT AUTHORITY\NETWORK SERVICE",
    [string]$ConfigRoot = ""
)

$ErrorActionPreference = "Stop"

function Get-LocalSqlInstances {
    # Installed SQL Server instances register themselves here regardless of edition
    # (Express/Developer/Standard/Enterprise) - this is the standard, documented discovery path.
    $regPath = "HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL"
    if (-not (Test-Path $regPath)) { return @() }

    $names = (Get-ItemProperty -Path $regPath).PSObject.Properties |
        Where-Object { $_.Name -notlike "PS*" } |
        Select-Object -ExpandProperty Name

    $servers = @()
    foreach ($name in $names) {
        # Default instance registers as "MSSQLSERVER" and connects as just the machine name / ".".
        # Named instances (e.g. a fresh "SQL Server Express" install is typically "SQLEXPRESS")
        # connect as "<machine>\<name>".
        if ($name -eq "MSSQLSERVER") {
            $servers += "."
        } else {
            $servers += ".\$name"
        }
    }
    return $servers
}

function Test-SqlConnection {
    param([string]$Server)
    try {
        $csb = New-Object System.Data.SqlClient.SqlConnectionStringBuilder
        $csb["Server"] = $Server
        $csb["Integrated Security"] = $true
        $csb["TrustServerCertificate"] = $true
        $csb["Connect Timeout"] = 5
        $conn = New-Object System.Data.SqlClient.SqlConnection($csb.ConnectionString)
        $conn.Open()
        $cmd = $conn.CreateCommand()
        $cmd.CommandText = "SELECT SERVERPROPERTY('Edition'), SERVERPROPERTY('ProductVersion')"
        $reader = $cmd.ExecuteReader()
        $reader.Read() | Out-Null
        $edition = $reader.GetString(0)
        $version = $reader.GetString(1)
        $reader.Close()
        $conn.Close()
        return @{ Server = $Server; Edition = $edition; Version = $version; Ok = $true }
    } catch {
        return @{ Server = $Server; Ok = $false; Error = $_.Exception.Message }
    }
}

function Grant-ServiceAccountAccess {
    param([string]$Server, [string]$Database, [string]$Account)

    $csb = New-Object System.Data.SqlClient.SqlConnectionStringBuilder
    $csb["Server"] = $Server
    $csb["Integrated Security"] = $true
    $csb["TrustServerCertificate"] = $true
    $csb["Connect Timeout"] = 10
    $conn = New-Object System.Data.SqlClient.SqlConnection($csb.ConnectionString)
    $conn.Open()

    function Invoke-Sql([System.Data.SqlClient.SqlConnection]$Connection, [string]$Sql) {
        $cmd = $Connection.CreateCommand()
        $cmd.CommandText = $Sql
        $cmd.ExecuteNonQuery() | Out-Null
    }

    function Invoke-SqlScalar([System.Data.SqlClient.SqlConnection]$Connection, [string]$Sql) {
        $cmd = $Connection.CreateCommand()
        $cmd.CommandText = $Sql
        return $cmd.ExecuteScalar()
    }

    $dbNameEscaped = $Database.Replace("]", "]]")
    $dbLiteral = $Database.Replace("'", "''")
    $existingId = Invoke-SqlScalar $conn "SELECT DB_ID(N'$dbLiteral')"
    if ($null -eq $existingId) {
        try {
            Invoke-Sql $conn "EXEC('CREATE DATABASE [$dbNameEscaped]');"
        } catch {
            throw "This Windows user cannot create database [$Database]. The person who installed SQL Express should click Create gym database, or install SQL Express first (Windows may ask for permission once). $($_.Exception.Message)"
        }
    }

    # 2. Create the server-level login for the service's Windows identity if it doesn't exist yet.
    #    Creating a login grants zero access by itself - access is scoped entirely by step 4 below.
    $acctEscaped = $Account.Replace("]", "]]")
    $acctLiteral = $Account.Replace("'", "''")
    Invoke-Sql $conn "IF SUSER_ID(N'$acctLiteral') IS NULL EXEC('CREATE LOGIN [$acctEscaped] FROM WINDOWS');"

    # 3/4. Switch to the target database and grant db_owner there ONLY - no sysadmin, no
    #    server-wide role. This is the entire runtime permission the service will ever have.
    $conn.ChangeDatabase($Database)
    Invoke-Sql $conn "IF DATABASE_PRINCIPAL_ID(N'$acctLiteral') IS NULL EXEC('CREATE USER [$acctEscaped] FOR LOGIN [$acctEscaped]');"
    Invoke-Sql $conn "IF IS_ROLEMEMBER('db_owner', N'$acctLiteral') = 0 EXEC('ALTER ROLE db_owner ADD MEMBER [$acctEscaped]');"

    $conn.Close()
}

Write-Host "==> Scanning for local SQL Server instances..." -ForegroundColor Cyan
$candidates = Get-LocalSqlInstances

if ($candidates.Count -eq 0) {
    Write-Host ""
    Write-Host "No local SQL Server instance was found (checked HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL)." -ForegroundColor Red
    Write-Host ""
    Write-Host "HyMotion Local Lifetime Edition requires SQL Server Express (free) installed on this machine." -ForegroundColor Yellow
    Write-Host "Download: https://www.microsoft.com/en-us/sql-server/sql-server-downloads (choose 'Express')" -ForegroundColor Yellow
    Write-Host "During setup, either the default instance or a named 'SQLEXPRESS' instance both work - re-run this script afterwards." -ForegroundColor Yellow
    exit 1
}

$working = @()
foreach ($server in $candidates) {
    Write-Host "   Testing $server ..." -NoNewline
    $result = Test-SqlConnection -Server $server
    if ($result.Ok) {
        Write-Host (" OK ({0}, {1})" -f $result.Edition, $result.Version) -ForegroundColor Green
        $working += $result
    } else {
        Write-Host (" unreachable ({0})" -f $result.Error) -ForegroundColor DarkYellow
    }
}

if ($working.Count -eq 0) {
    Write-Host ""
    Write-Host "SQL Server instance(s) were found in the registry but none accepted a Windows-auth connection." -ForegroundColor Red
    Write-Host "Verify the SQL Server / SQL Server (INSTANCENAME) Windows service is running and this user has login rights." -ForegroundColor Yellow
    exit 1
}

$chosen = $working[0]
Write-Host ""
Write-Host ("==> Using SQL Server instance: {0}" -f $chosen.Server) -ForegroundColor Green

try {
    $csbFix = New-Object System.Data.SqlClient.SqlConnectionStringBuilder
    $csbFix["Server"] = $chosen.Server
    $csbFix["Integrated Security"] = $true
    $csbFix["TrustServerCertificate"] = $true
    $csbFix["Initial Catalog"] = "master"
    $csbFix["Connect Timeout"] = 8
    $fixConn = New-Object System.Data.SqlClient.SqlConnection($csbFix.ConnectionString)
    $fixConn.Open()
    $fixCmd = $fixConn.CreateCommand()
    $fixCmd.CommandText = "SELECT user_access_desc FROM sys.databases WHERE name = N'$($DatabaseName.Replace("'","''"))'"
    $access = $fixCmd.ExecuteScalar()
    if ($access -eq "SINGLE_USER" -or $access -eq "RESTRICTED_USER") {
        Write-Host ("==> Database [{0}] is {1} (often leftover from SSMS Delete). Setting MULTI_USER..." -f $DatabaseName, $access) -ForegroundColor Yellow
        $fixCmd.CommandText = "ALTER DATABASE [$($DatabaseName.Replace(']', ']]'))] SET MULTI_USER WITH ROLLBACK IMMEDIATE"
        $fixCmd.ExecuteNonQuery() | Out-Null
        Write-Host "    MULTI_USER restored." -ForegroundColor Green
    }
    $fixConn.Close()
} catch {
    Write-Host ("Could not check/repair database access mode: {0}" -f $_.Exception.Message) -ForegroundColor DarkYellow
}

$connectionString = "Server=$($chosen.Server);Database=$DatabaseName;Trusted_Connection=True;TrustServerCertificate=True;"
Write-Host "    Connection string: $connectionString"

if ($WriteConfig) {
    Write-Host ""
    $currentUser = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
    $accounts = @($currentUser)
    if ($ServiceAccount -and $ServiceAccount -ne $currentUser) {
        $accounts += $ServiceAccount
    }

    $granted = $false
    foreach ($acct in $accounts) {
        Write-Host ("==> Granting {0} access to database [{1}] (db_owner only)..." -f $acct, $DatabaseName) -ForegroundColor Cyan
        try {
            Grant-ServiceAccountAccess -Server $chosen.Server -Database $DatabaseName -Account $acct
            Write-Host "    Done." -ForegroundColor Green
            if ($acct -eq $currentUser) { $granted = $true }
        } catch {
            Write-Host ("Could not grant {0}: {1}" -f $acct, $_.Exception.Message) -ForegroundColor Yellow
            if ($acct -eq $currentUser) { throw }
        }
    }
    if (-not $granted) { throw "Could not grant this Windows user access to [$DatabaseName]." }

    if (-not $ConfigRoot) { $ConfigRoot = $env:HYMOTION_DATA_DIR }
    if (-not $ConfigRoot) { $ConfigRoot = Join-Path $env:LOCALAPPDATA "HyMotion" }
    $configDir = Join-Path $ConfigRoot "config"
    New-Item -ItemType Directory -Path $configDir -Force | Out-Null
    $configFile = Join-Path $configDir "appsettings.json"

    $existing = New-Object PSObject
    if (Test-Path $configFile) {
        try { $existing = Get-Content $configFile -Raw | ConvertFrom-Json } catch { $existing = New-Object PSObject }
    }
    if (-not (Get-Member -InputObject $existing -Name "ConnectionStrings" -MemberType NoteProperty)) {
        $existing | Add-Member -MemberType NoteProperty -Name "ConnectionStrings" -Value (New-Object PSObject)
    }
    if (-not (Get-Member -InputObject $existing.ConnectionStrings -Name "DefaultConnection" -MemberType NoteProperty)) {
        $existing.ConnectionStrings | Add-Member -MemberType NoteProperty -Name "DefaultConnection" -Value $connectionString
    } else {
        $existing.ConnectionStrings.DefaultConnection = $connectionString
    }

    ($existing | ConvertTo-Json -Depth 5) | Set-Content -Path $configFile -Encoding utf8
    Write-Host ""
    Write-Host "Wrote connection string to $configFile" -ForegroundColor Green
    Write-Host "(never committed to source control - machine-specific, loaded automatically by GMS.Api when Deployment:Edition=Local)"
}
