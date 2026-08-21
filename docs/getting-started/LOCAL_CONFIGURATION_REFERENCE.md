# 🔧 LOCAL CONFIGURATION - Complete Reference

## 📍 CURRENT LOCAL SETUP

Your application is **100% local** and ready to use right now.

```
Your Machine
├─ SQL Server LocalDB (GymFlowProDb)
├─ ASP.NET Core 8 Application (https://localhost:5001)
└─ Everything offline - No external servers needed
```

---

## ⚡ QUICK START (2 MINUTES)

### 1. Start LocalDB
```powershell
sqllocaldb start mssqllocaldb
```

### 2. Create Database (First Time Only)
```powershell
cd D:\GMS\GMS
dotnet ef database update --project GMS.Infrastructure --startup-project GMS.Api
```

### 3. Run Application
```powershell
cd GMS.Api
dotnet run
```

### 4. Open in Browser
```
https://localhost:5001
```

**That's it! You're up and running.** ✅

---

## 📋 CONFIGURATION FILES

### Location: `D:\GMS\GMS\GMS.Api\`

```
GMS.Api/
├─ appsettings.json              (Base config - don't modify)
├─ appsettings.Development.json  (Your local settings - auto-loaded)
├─ appsettings.Staging.json      (Template for staging)
├─ appsettings.Production.json   (Template for production)
└─ launchSettings.json           (Port and profile settings)
```

---

## 🎛️ CONFIGURATION DETAILS

### appsettings.Development.json (What You Use Locally)

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=(localdb)\\mssqllocaldb;Database=GymFlowProDb;Trusted_Connection=true;Encrypt=false;"
  },
  
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Microsoft.EntityFrameworkCore": "Debug"
    }
  },
  
  "DatabaseConfig": {
    "Provider": "LocalDB",
    "Description": "Local SQL Server LocalDB"
  },
  
  "AppSettings": {
    "Environment": "Development"
  }
}
```

**This file:**
- ✅ Uses LocalDB (no password needed)
- ✅ Auto-loaded by Visual Studio when ASPNETCORE_ENVIRONMENT=Development
- ✅ Enables debug logging
- ✅ Safe to edit - local only

### appsettings.json (Base/Production)

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=(localdb)\\mssqllocaldb;Database=GymFlowProDb;Trusted_Connection=true;Encrypt=false;"
  },
  
  "AllowedHosts": "*",
  
  "DatabaseConfig": {
    "Provider": "LocalDB"
  },
  
  "AppSettings": {
    "Environment": "Production"
  }
}
```

**This file:**
- ⚠️ Don't modify - it's the base
- ✅ Overridden by Development.json when running locally
- ✅ Used in production if no environment-specific file

### appsettings.Staging.json (Template - When Deploying to Staging)

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=your-staging-server.com;Database=GymFlowProDb;User Id=sa;Password=YOUR_PASSWORD;"
  }
}
```

**To use:**
1. Update with your staging server details
2. Deploy to staging environment
3. Set `ASPNETCORE_ENVIRONMENT=Staging`

### appsettings.Production.json (Template - When Deploying to Production)

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=your-production-server.com;Database=GymFlowProDb;User Id=sa;Password=YOUR_PASSWORD;"
  }
}
```

**To use:**
1. Update with your production server details
2. Deploy to production environment
3. Set `ASPNETCORE_ENVIRONMENT=Production`

---

## 🔌 CONNECTION STRINGS FOR DIFFERENT SCENARIOS

### LocalDB (Current - Windows Authentication)
```
Server=(localdb)\mssqllocaldb;Database=GymFlowProDb;Trusted_Connection=true;Encrypt=false;
```
**Use when:** Developing on your machine
**Why:** No password needed, Windows auth automatic

### LocalDB with SSMS Connection
```
(localdb)\mssqllocaldb
```
**Use in SSMS:** Server name field
**Database:** GymFlowProDb

### SQL Server Express (On Same Machine)
```
Server=.\SQLEXPRESS;Database=GymFlowProDb;Trusted_Connection=true;Encrypt=false;
```
**Use when:** Using SQL Server Express instead of LocalDB
**Difference:** SQL Express is a full server, LocalDB is lightweight

### SQL Server on Different Machine
```
Server=192.168.1.100;Database=GymFlowProDb;User Id=sa;Password=YOUR_PASSWORD;Encrypt=false;
```
**Use when:** Database on another machine on your network
**Update:** IP address and credentials

### Azure SQL Database
```
Server=tcp:gymflowpro.database.windows.net,1433;Initial Catalog=GymFlowProDb;Persist Security Info=False;User ID=sqladmin;Password=YOUR_PASSWORD;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;
```
**Use when:** Deploying to Azure
**Update:** Server name and password

### Docker SQL Server (Linux Container)
```
Server=localhost,1433;Database=GymFlowProDb;User Id=sa;Password=YOUR_PASSWORD;Encrypt=false;
```
**Use when:** Running SQL Server in Docker

---

## 🌍 ENVIRONMENT VARIABLES

### Windows PowerShell
```powershell
# Set for current session
$env:ASPNETCORE_ENVIRONMENT = "Development"

# Run application
dotnet run

# Clear after
$env:ASPNETCORE_ENVIRONMENT = ""
```

### Windows Command Prompt
```cmd
# Set for current session
set ASPNETCORE_ENVIRONMENT=Development

# Run application
dotnet run

# Clear after
set ASPNETCORE_ENVIRONMENT=
```

### macOS/Linux
```bash
# Set for current session
export ASPNETCORE_ENVIRONMENT=Development

# Run application
dotnet run

# Clear after
unset ASPNETCORE_ENVIRONMENT
```

### Visual Studio (Automatic)
- Automatically sets `ASPNETCORE_ENVIRONMENT=Development` when running with F5
- Automatically loads `appsettings.Development.json`
- No manual setup needed

---

## 📊 DATABASE STATUS COMMANDS

### Check LocalDB Status
```powershell
# List all LocalDB instances
sqllocaldb info

# Start specific instance
sqllocaldb start mssqllocaldb

# Stop instance
sqllocaldb stop mssqllocaldb

# Delete instance (careful!)
sqllocaldb delete mssqllocaldb
```

### Connect with SSMS
1. Open SQL Server Management Studio
2. Server Name: `(localdb)\mssqllocaldb`
3. Authentication: Windows Authentication
4. Database: GymFlowProDb

### Query Database via CLI
```powershell
# Check if database exists
sqlcmd -S "(localdb)\mssqllocaldb" -Q "SELECT name FROM sys.databases WHERE name='GymFlowProDb'"

# Count members
sqlcmd -S "(localdb)\mssqllocaldb" -d GymFlowProDb -Q "SELECT COUNT(*) FROM gym_members"

# List all tables
sqlcmd -S "(localdb)\mssqllocaldb" -d GymFlowProDb -Q "SELECT name FROM sys.tables WHERE type='U'"
```

---

## 🚀 SWITCHING SERVERS (When You Decide)

### Scenario 1: Move to SQL Server on Another Machine

1. **Update connection string** in `appsettings.Staging.json` or `appsettings.Production.json`
2. **Run migrations on new server:**
   ```powershell
   dotnet ef database update --project GMS.Infrastructure --startup-project GMS.Api
   ```
3. **Deploy application** to server
4. **Done!** No code changes needed

### Scenario 2: Move to Azure SQL

1. **Create Azure SQL instance** in Azure Portal
2. **Update connection string** in `appsettings.Production.json`
3. **Create firewall rule** to allow your IP
4. **Run migrations:**
   ```powershell
   dotnet ef database update --project GMS.Infrastructure --startup-project GMS.Api
   ```
5. **Deploy to Azure App Service**

### Scenario 3: Use Docker Container

1. **Start SQL Server in Docker:**
   ```powershell
   docker run -e "ACCEPT_EULA=Y" -e "MSSQL_SA_Password=YOUR_PASSWORD" -p 1433:1433 -d mcr.microsoft.com/mssql/server
   ```
2. **Update connection string:**
   ```
   Server=localhost,1433;User Id=sa;Password=YOUR_PASSWORD;
   ```
3. **Run migrations**
4. **Deploy application**

---

## ✅ VERIFICATION CHECKLIST

### Before Starting Development

```powershell
# 1. Verify .NET 8
dotnet --version
# Expected: 8.0.x or higher

# 2. Verify Visual Studio
# Launch Visual Studio 2026

# 3. Verify LocalDB exists
sqllocaldb info mssqllocaldb

# 4. Start LocalDB
sqllocaldb start mssqllocaldb

# 5. Build solution
dotnet build

# 6. Create database
dotnet ef database update --project GMS.Infrastructure --startup-project GMS.Api

# 7. Run application
dotnet run --project GMS.Api

# 8. Open browser
# https://localhost:5001
```

### If Something Fails

**Database doesn't exist:**
```powershell
dotnet ef database update --project GMS.Infrastructure --startup-project GMS.Api
```

**LocalDB not running:**
```powershell
sqllocaldb start mssqllocaldb
```

**Port 5001 in use:**
```powershell
# Find what's using port 5001
netstat -ano | findstr :5001

# Kill process (if needed)
taskkill /PID [PID] /F

# Or change port in launchSettings.json
```

**Build errors:**
```powershell
# Clean and rebuild
dotnet clean
dotnet build
```

---

## 📱 ACCESSING YOUR APPLICATION

### From Your Machine
- **API**: https://localhost:5001
- **Swagger**: https://localhost:5001/swagger/ui
- **Health**: https://localhost:5001/health

### From Another Machine on Your Network
```
https://[YOUR_IP]:5001
```
Where `[YOUR_IP]` is your machine's local IP (e.g., 192.168.1.100)

**Note:** Requires firewall exception for port 5001

---

## 🔐 SECURITY SETTINGS (LOCAL vs PRODUCTION)

### Development (LocalDB)
- ✅ No password (Windows auth)
- ✅ Localhost only
- ✅ Self-signed HTTPS certificate ok
- ✅ Debug logging enabled
- ✅ CORS AllowAll

### Production (Your Server)
- 🔒 Strong password required
- 🔒 Firewall restricted access
- 🔒 Valid HTTPS certificate
- 🔒 Info-level logging only
- 🔒 CORS restricted

---

## 📚 USEFUL COMMANDS

```powershell
# Start LocalDB
sqllocaldb start mssqllocaldb

# Build solution
dotnet build

# Create migration
dotnet ef migrations add MigrationName --project GMS.Infrastructure --startup-project GMS.Api

# Apply migrations
dotnet ef database update --project GMS.Infrastructure --startup-project GMS.Api

# Run application (Development)
dotnet run --project GMS.Api

# Run application (Production - local)
$env:ASPNETCORE_ENVIRONMENT = "Production"
dotnet run --project GMS.Api --configuration Release

# Run startup script (Windows)
.\start-local.bat

# View database tables
sqlcmd -S "(localdb)\mssqllocaldb" -d GymFlowProDb -Q "SELECT name FROM sys.tables WHERE type='U'"

# Count records
sqlcmd -S "(localdb)\mssqllocaldb" -d GymFlowProDb -Q "SELECT COUNT(*) FROM gym_members"
```

---

## 🎯 DEVELOPMENT WORKFLOW

### Day 1: Initial Setup
```powershell
1. sqllocaldb start mssqllocaldb
2. dotnet ef database update
3. dotnet run
4. Browser: https://localhost:5001
```

### Days 2+: Regular Development
```powershell
1. Open Visual Studio
2. Press F5
3. Make code changes
4. Test with Swagger
5. Commit to git
```

### When Ready to Deploy (3+ Weeks Later)
```powershell
1. Create database on target server
2. Update appsettings.{Environment}.json
3. dotnet ef database update (on target server)
4. dotnet publish -c Release
5. Deploy to server
```

---

## ✨ YOU'RE ALL SET!

✅ **Local Development**: Ready to go
✅ **Configuration**: Multi-environment setup
✅ **No Decisions Needed Now**: Develop locally
✅ **Easy to Switch Later**: Just update connection strings

**Start now** with:
```powershell
dotnet run --project GMS.Api
```

**Your application is fully functional locally!** 🚀

