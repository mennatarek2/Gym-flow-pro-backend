# 🔧 DATABASE SETUP - USING DOTNET EF CLI

## Problem
`Update-Database` not recognized - PowerShell tools not installed

## Solution
Use **dotnet ef** command line tool (more reliable, no installation needed)

---

## ⚡ Quick Setup (3 Steps)

### Step 1: Navigate to API Project
```powershell
cd D:\GMS\GMS\GMS.Api
```

### Step 2: Apply Database Migrations
```powershell
dotnet ef database update --context GymFlowProDbContext
```

### Step 3: Verify Database Created
```powershell
# Check if database exists (LocalDB)
sqlcmd -S "(localdb)\mssqllocaldb" -Q "SELECT name FROM sys.databases WHERE name='GymFlowProDb';"
```

**Expected Output**:
```
name
GymFlowProDb
```

---

## ✅ If That Works - You're Done!

Your database is now:
- ✅ Created
- ✅ Migrations applied
- ✅ Seeded with test data

Skip to **Testing** section below.

---

## ❌ If You Get "dotnet ef not found"

### Install dotnet-ef globally (one-time)

```powershell
dotnet tool install --global dotnet-ef
```

Then retry:
```powershell
cd D:\GMS\GMS\GMS.Api
dotnet ef database update --context GymFlowProDbContext
```

---

## 🧹 If Database Exists & You Want Fresh Reset

### Option 1: Drop & Recreate (Recommended for Development)

```powershell
cd D:\GMS\GMS\GMS.Api

# Drop database
dotnet ef database drop --context GymFlowProDbContext --force

# Recreate from migrations
dotnet ef database update --context GymFlowProDbContext
```

### Option 2: Using SQL (Alternative)

```powershell
# Open SQL Server
sqlcmd -S "(localdb)\mssqllocaldb"

# In SQL prompt:
> DROP DATABASE [GymFlowProDb];
> EXIT

# Then apply migrations:
cd D:\GMS\GMS\GMS.Api
dotnet ef database update --context GymFlowProDbContext
```

---

## 📊 Verify Database Setup

### Check Database Exists
```powershell
sqlcmd -S "(localdb)\mssqllocaldb" -Q "SELECT name FROM sys.databases WHERE name='GymFlowProDb';"
```

### Check Tables Exist
```powershell
sqlcmd -S "(localdb)\mssqllocaldb" -d GymFlowProDb -Q "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES;"
```

**Expected Tables**:
```
Tenants
AspNetUsers
AspNetRoles
AspNetUserRoles
GymMembers
Memberships
MembershipPlans
GymAttendances
MemberInvitations
AnalyticsSnapshots
```

### Check Test Data Seeded
```powershell
sqlcmd -S "(localdb)\mssqllocaldb" -d GymFlowProDb -Q "SELECT Email, FirstName, LastName FROM AspNetUsers;"
```

**Expected Users**:
```
Email                      FirstName  LastName
owner@gymflow.test        Ahmed      Owner
manager@gymflow.test      Sara       Manager
trainer@gymflow.test      Omar       Trainer
```

---

## 🚀 Next: Start Application

After database is created:

```powershell
cd D:\GMS\GMS

# Build
dotnet build

# Run API
dotnet run --project GMS.Api
```

**Expected Output**:
```
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: http://localhost:5000
```

---

## 🧪 Test Manual Check-in Works

```powershell
# Get token
$response = Invoke-RestMethod -Uri "http://localhost:5000/api/auth/login" `
  -Method Post `
  -ContentType "application/json" `
  -Body @'
{
  "email": "owner@gymflow.test",
  "password": "YOUR_PASSWORD"
}
'@

$token = $response.accessToken

# Test manual check-in
Invoke-RestMethod -Uri "http://localhost:5000/api/attendance/manual-checkin" `
  -Method Post `
  -ContentType "application/json" `
  -Headers @{ Authorization = "Bearer $token" } `
  -Body @'
{
  "memberId": "8e4e7838-3715-48d2-842f-ee2df786669f",
  "reason": 1,
  "notes": "Test"
}
'@
```

**Expected**: 200 OK ✅

---

## 📋 Troubleshooting

### Issue: "No database provider configured"
**Solution**: Check `appsettings.json` has connection string:
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=(localdb)\\mssqllocaldb;Database=GymFlowProDb;Integrated Security=true;"
  }
}
```

### Issue: "Migration not found"
**Solution**: Ensure you're in correct directory:
```powershell
cd D:\GMS\GMS\GMS.Api
# Then run:
dotnet ef database update --context GymFlowProDbContext
```

### Issue: "Unable to create the service provider"
**Solution**: Check GMS.Api.csproj has correct frameworks:
```xml
<TargetFramework>net8.0</TargetFramework>
```

---

## ✅ Success Checklist

- [ ] `cd` to D:\GMS\GMS\GMS.Api
- [ ] Run `dotnet ef database update --context GymFlowProDbContext`
- [ ] See success message
- [ ] Run `dotnet build`
- [ ] Run `dotnet run --project GMS.Api`
- [ ] Test login endpoint
- [ ] Test manual check-in endpoint
- [ ] See 200 OK ✅

---

## 🎯 Quick Commands Reference

```powershell
# Navigate
cd D:\GMS\GMS\GMS.Api

# Create/Update database
dotnet ef database update --context GymFlowProDbContext

# Drop database (fresh start)
dotnet ef database drop --context GymFlowProDbContext --force

# List migrations
dotnet ef migrations list --context GymFlowProDbContext

# Check database
sqlcmd -S "(localdb)\mssqllocaldb" -Q "SELECT name FROM sys.databases WHERE name='GymFlowProDb';"
```

---

## 📞 Still Having Issues?

1. **dotnet not found?** → Reinstall .NET 8 SDK
2. **Connection string wrong?** → Check appsettings.json
3. **Migrations failed?** → Delete Migrations folder and regenerate

See `COMPLETE_DELIVERY_PACKAGE.md` for detailed help.

---

**Next**: Run the database update command above, then start application!

