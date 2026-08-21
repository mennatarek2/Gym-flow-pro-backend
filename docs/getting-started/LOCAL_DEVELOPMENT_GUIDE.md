# 🚀 LOCAL DEVELOPMENT SETUP - Run Everywhere, Deploy Anywhere

## 📍 CURRENT STATUS: 100% LOCAL

Your application is **completely local** and ready for development. Everything runs on your machine with **zero external dependencies**.

---

## ✅ WHAT'S CONFIGURED FOR LOCAL DEVELOPMENT

### Database
- **Type**: SQL Server LocalDB (included with Visual Studio)
- **Location**: Your machine (`(localdb)\mssqllocaldb`)
- **Database Name**: `GymFlowProDb`
- **Connection**: Windows Authentication (no passwords needed)
- **Status**: ✅ **FULLY LOCAL**

### Application
- **Host**: `https://localhost:5001`
- **Environment**: Development
- **Logging**: Debug level (verbose output)
- **CORS**: Enabled (AllowAll)
- **Swagger**: Enabled
- **Status**: ✅ **FULLY LOCAL**

### Configuration Files
- `appsettings.json` - Base configuration (production defaults)
- `appsettings.Development.json` - Local development overrides
- `appsettings.Staging.json` - Staging server configuration (template)
- `appsettings.Production.json` - Production server configuration (template)

---

## 🎯 HOW TO RUN LOCALLY (RIGHT NOW)

### Prerequisites
1. **Visual Studio 2026** (already have it ✅)
2. **SQL Server LocalDB** (included with Visual Studio ✅)
3. **.NET 8 SDK** (already have it ✅)

### Step 1: Verify LocalDB is Running

```powershell
# Check LocalDB status
sqllocaldb info mssqllocaldb

# If not running, start it
sqllocaldb start mssqllocaldb
```

### Step 2: Create the Database

```powershell
cd D:\GMS\GMS

# Create migration (first time only)
dotnet ef migrations add InitialSchema `
  --project GMS.Infrastructure `
  --startup-project GMS.Api

# Apply migration and create database
dotnet ef database update `
  --project GMS.Infrastructure `
  --startup-project GMS.Api
```

### Step 3: Run the Application

**Option A: Visual Studio**
1. Open `GMS.sln` in Visual Studio
2. Set `GMS.Api` as Startup Project
3. Press `F5` or click "Run"
4. Browser opens to `https://localhost:5001`

**Option B: Command Line**
```powershell
cd D:\GMS\GMS\GMS.Api
dotnet run

# Output:
# Application started: https://localhost:5001
```

### Step 4: Access the Application

- **API Home**: https://localhost:5001
- **Swagger UI**: https://localhost:5001/swagger/ui
- **Health Check**: https://localhost:5001/health

---

## 📊 LOCAL ARCHITECTURE

```
Your Machine
│
├─ Visual Studio / VS Code
│  └─ GMS.Api (ASP.NET Core)
│     └─ Running on https://localhost:5001
│
├─ SQL Server LocalDB
│  └─ GymFlowProDb
│     ├─ tenants
│     ├─ gym_members
│     ├─ membership_plans
│     ├─ memberships
│     ├─ gym_attendance
│     ├─ member_invitations
│     └─ app_users
│
└─ Browser
   └─ https://localhost:5001 (Swagger UI)

✅ Everything local - no internet needed during development!
```

---

## 🔄 SWITCHING SERVERS LATER (When You Decide)

When you decide on a server, you **don't need to change code** - just update connection strings!

### Option 1: Azure SQL Database

**Step 1: Create Azure Resources**
```powershell
# Via Azure Portal or Azure CLI
az sql server create --resource-group myResourceGroup --name gymflowpro --admin-user sqladmin
az sql db create --resource-group myResourceGroup --server gymflowpro --name GymFlowProDb
```

**Step 2: Update Connection String**
```json
// appsettings.Production.json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=tcp:gymflowpro.database.windows.net,1433;Initial Catalog=GymFlowProDb;Persist Security Info=False;User ID=sqladmin;Password=YOUR_PASSWORD;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;"
  }
}
```

**Step 3: Deploy**
```powershell
# Build for production
dotnet build --configuration Release

# Deploy to Azure App Service
dotnet publish -c Release -o ./publish
# Then use Azure Portal or Azure CLI to deploy
```

### Option 2: SQL Server on Another Machine

**Step 1: Create Database on Server**
```sql
-- On remote SQL Server
CREATE DATABASE GymFlowProDb
```

**Step 2: Update Connection String**
```json
// appsettings.Production.json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=your-server-ip.com;Database=GymFlowProDb;User Id=sa;Password=YOUR_PASSWORD;"
  }
}
```

**Step 3: Run Migrations on Server**
```powershell
dotnet ef database update --project GMS.Infrastructure --startup-project GMS.Api
```

### Option 3: Docker Container (Local or Cloud)

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:8.0
COPY ./publish /app
WORKDIR /app
ENTRYPOINT ["dotnet", "GMS.Api.dll"]
```

**Configuration:** Same as any server - just update appsettings

---

## 📁 CONFIGURATION FILE GUIDE

### appsettings.json (Base)
- **Used by**: All environments
- **Database**: LocalDB (default)
- **Override**: By environment-specific files
- **Don't commit secrets**: Add passwords only to local files

### appsettings.Development.json (Local)
- **Used by**: `dotnet run` (local development)
- **Database**: LocalDB (your machine)
- **Safe to edit**: ✅ (local only, not committed)
- **Purpose**: Override base settings for local development

### appsettings.Staging.json (Template)
- **Used by**: Staging environment (when deployed)
- **Database**: Your staging server
- **How to use**: Rename to appsettings.Staging.json
- **Update**: Server IP, database name, credentials

### appsettings.Production.json (Template)
- **Used by**: Production environment (when deployed)
- **Database**: Your production server
- **How to use**: Rename and update for your server
- **Security**: Store passwords in Azure Key Vault, not in file

---

## 🔐 SECURITY: Local vs Production

### Development (LocalDB - Local Machine)

✅ **Secure:**
- No passwords needed (Windows Authentication)
- No network exposure
- No external access
- Localhost only (127.0.0.1)
- HTTPS disabled (self-signed cert ok)

**Commands:**
```powershell
dotnet run --configuration Development
```

### Staging (Your Server)

⚠️ **Moderate Security:**
- Use strong database passwords
- Enable HTTPS (Let's Encrypt)
- Firewall restricted access
- Monitor logs
- Test before production

**Deployment:**
```powershell
dotnet publish -c Release -o ./publish
# Copy to staging server
```

### Production (Your Server or Azure)

🔒 **High Security:**
- Strong passwords (Azure Key Vault)
- HTTPS required
- Firewall rules
- Encryption at rest (TDE)
- Encryption in transit (TLS)
- Monitor and backup
- Regular security updates

**Environment Variable:**
```powershell
# Run with ASPNETCORE_ENVIRONMENT
set ASPNETCORE_ENVIRONMENT=Production
dotnet run
```

---

## 📝 GITIGNORE CONFIGURATION

**Your `.gitignore` should include:**
```
# Don't commit local secrets
appsettings.*.local.json
*.local.*

# Don't commit LocalDB data
*.mdf
*.ldf

# Keep templates, update for your server
# appsettings.Production.json (template)
# appsettings.Staging.json (template)
```

---

## 🚀 TYPICAL WORKFLOW

### Day-to-Day Development
```
1. Run locally (LocalDB)
2. Edit code
3. Test with Swagger UI
4. Commit to git
5. No server interaction needed
```

### When Ready to Deploy
```
1. Create database on target server
2. Update appsettings.{Environment}.json
3. Run: dotnet ef database update --configuration Release
4. Build: dotnet publish -c Release
5. Deploy application
6. Verify with health check endpoint
```

### Switching to Different Server
```
1. Update connection string in appsettings file
2. No code changes needed!
3. Run migrations on new server
4. Deploy application
5. Done!
```

---

## 🛠️ TROUBLESHOOTING

### "Database connection failed"

**Solution:**
```powershell
# Check LocalDB is running
sqllocaldb info mssqllocaldb

# Start if not running
sqllocaldb start mssqllocaldb

# Create if not exists
sqllocaldb create mssqllocaldb

# Check connection string
cat GMS.Api/appsettings.Development.json | Select-String "DefaultConnection"
```

### "Cannot access health endpoint"

**Solution:**
```powershell
# Application not running?
dotnet run --project GMS.Api

# Wrong port?
# Check launchSettings.json for port configuration

# HTTPS certificate issue?
# Run: dotnet dev-certs https --trust
```

### "EF migrations not applying"

**Solution:**
```powershell
# Verify LocalDB is running
sqllocaldb info mssqllocaldb

# Check connection string
# Verify database exists or can be created

# Try again
dotnet ef database update `
  --project GMS.Infrastructure `
  --startup-project GMS.Api `
  --verbose
```

---

## 📊 ENVIRONMENT MATRIX

| Aspect | Development | Staging | Production |
|--------|-------------|---------|-----------|
| **Database** | LocalDB | Your Server | Your Server/Azure |
| **Connection** | Windows Auth | SQL Auth | Managed Identity |
| **HTTPS** | Self-signed | Valid cert | Valid cert |
| **Logging** | Debug | Info | Info |
| **CORS** | AllowAll | Restricted | Restricted |
| **Swagger** | Enabled | Disabled | Disabled |
| **Run Command** | `dotnet run` | IIS/Container | IIS/Container |

---

## ✅ YOU'RE READY!

✅ Application is **100% local**
✅ No external dependencies during development
✅ Easy to switch servers later
✅ Zero code changes needed
✅ Only configuration changes required

---

## 📚 NEXT STEPS

### To Start Right Now:
1. Run `dotnet ef database update` to create local database
2. Run `dotnet run` to start application
3. Open https://localhost:5001 in browser
4. Use Swagger UI to test endpoints

### To Deploy Later:
1. Create database on your chosen server
2. Update `appsettings.{Environment}.json`
3. Run migrations on server
4. Deploy application
5. No code changes needed!

---

## 🎯 YOUR DECISION TIMELINE

**Right Now:**
- ✅ Develop locally (no decisions needed)
- ✅ Test functionality
- ✅ Build features
- ✅ Everything works offline

**When You're Ready:**
- ⏸️ Decide: Azure SQL? SQL Server? Other?
- ⏸️ Update configuration file
- ⏸️ Deploy application
- ⏸️ No code changes!

**You have complete flexibility** - develop locally, deploy anywhere! 🚀

---

**Status: ✅ FULLY LOCAL & READY**

All features work on your machine right now!
