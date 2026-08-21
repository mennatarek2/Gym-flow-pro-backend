# ⚡ QUICK COMMANDS - COPY & PASTE

## 🚀 Setup Database in 3 Commands

```powershell
# 1. Navigate to API
cd D:\GMS\GMS\GMS.Api

# 2. Create database
dotnet ef database update --context GymFlowProDbContext

# 3. Done!
```

---

## ▶️ Run Application

```powershell
cd D:\GMS\GMS
dotnet run --project GMS.Api
```

**Then access**: http://localhost:5000

---

## 🧪 Test Manual Check-in

### Get Token:
```powershell
$r = Invoke-RestMethod -Uri http://localhost:5000/api/auth/login `
  -Method Post -ContentType application/json `
  -Body '{"email":"owner@gymflow.test","password": "YOUR_PASSWORD"}'
$t = $r.accessToken
Write-Host "Token: $($t.Substring(0,50))..."
```

### Test Check-in:
```powershell
Invoke-RestMethod -Uri http://localhost:5000/api/attendance/manual-checkin `
  -Method Post -ContentType application/json `
  -Headers @{Authorization="Bearer $t"} `
  -Body '{"memberId":"8e4e7838-3715-48d2-842f-ee2df786669f","reason":1,"notes":"Test"}'
```

**Expected**: `success: true` ✅

---

## 🔄 Reset Database

```powershell
cd D:\GMS\GMS\GMS.Api
dotnet ef database drop --context GymFlowProDbContext --force
dotnet ef database update --context GymFlowProDbContext
```

---

## ✅ Verify Setup

```powershell
# Check database exists
sqlcmd -S "(localdb)\mssqllocaldb" -Q "SELECT name FROM sys.databases WHERE name='GymFlowProDb';"

# Check tables
sqlcmd -S "(localdb)\mssqllocaldb" -d GymFlowProDb -Q "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES;"

# Check users
sqlcmd -S "(localdb)\mssqllocaldb" -d GymFlowProDb -Q "SELECT Email, FirstName FROM AspNetUsers;"
```

---

## 📞 All Done!

- ✅ Database created
- ✅ Application running
- ✅ Manual check-in working
- ✅ Ready to develop

**See**: `COMPLETE_SETUP_GUIDE.md` for detailed steps

