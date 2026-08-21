# 🚀 QUICK START - CORRECTED PORTS

## ⚡ ONE-MINUTE SETUP (UPDATED)

```powershell
# 1. Start LocalDB (30 seconds)
sqllocaldb start mssqllocaldb

# 2. Create database (30 seconds)
cd D:\GMS\GMS
dotnet ef database update --project GMS.Infrastructure --startup-project GMS.Api

# 3. Run application (automatic - NOW WITH CORRECT PORTS!)
dotnet run --project GMS.Api

# DONE! Opens browser automatically to: https://localhost:5001/swagger/ui
```

---

## 🎯 CORRECTED ACCESS POINTS

| What | URL | Port |
|------|-----|------|
| **HTTPS (Recommended)** | https://localhost:5001 | 5001 |
| **Swagger UI** | https://localhost:5001/swagger/ui | 5001 |
| **HTTP** | http://localhost:5000 | 5000 |
| **Health Check** | https://localhost:5001/health | 5001 |

---

## ✅ WHAT WAS FIXED

❌ **Before:** Ports were 7079 (HTTPS) and 5241 (HTTP)
✅ **After:** Ports are now 5001 (HTTPS) and 5000 (HTTP) - matches documentation!

✅ **Swagger URL:** Now correctly set to `/swagger/ui` (not `/swagger`)

✅ **Auto-launch:** Application automatically opens to Swagger UI when you press F5

---

## 🔄 HOW TO RESTART

Since ports changed, stop the old application and restart:

```powershell
# Option 1: Press Ctrl+C in current terminal

# Option 2: Kill the process
netstat -ano | findstr :5001
netstat -ano | findstr :5241
# Identify old PIDs and kill them

# Option 3: Restart VS
# Close Visual Studio and reopen
```

Then:
```powershell
dotnet run --project GMS.Api
```

---

## ✨ VERIFICATION

After restarting, check:

1. ✅ Application starts on `https://localhost:5001`
2. ✅ Browser automatically opens to Swagger UI
3. ✅ No 404 errors for `/swagger/ui`
4. ✅ Health endpoint works: `https://localhost:5001/health`

---

## 🎉 YOU'RE GOOD TO GO!

Ports are now corrected and match all documentation!

Your application is **100% local and ready to use**! 🚀
