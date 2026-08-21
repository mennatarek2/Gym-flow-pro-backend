# 🚀 QUICK START - LOCAL DEVELOPMENT

## ⚡ ONE-MINUTE SETUP

```powershell
# 1. Start LocalDB (30 seconds)
sqllocaldb start mssqllocaldb

# 2. Create database (30 seconds)
cd D:\GMS\GMS
dotnet ef database update --project GMS.Infrastructure --startup-project GMS.Api

# 3. Run application (automatic)
dotnet run --project GMS.Api

# DONE! Open browser to: https://localhost:5001
```

---

## 🎯 WHAT YOU GET

| What | Where |
|------|-------|
| **API** | https://localhost:5001 |
| **Swagger UI** | https://localhost:5001/swagger/ui |
| **Health Check** | https://localhost:5001/health |
| **Database** | LocalDB on your machine |
| **Logs** | Console output |

---

## 🔧 TROUBLESHOOTING (30 SECONDS)

**Database doesn't exist?**
```powershell
dotnet ef database update --project GMS.Infrastructure --startup-project GMS.Api
```

**LocalDB not running?**
```powershell
sqllocaldb start mssqllocaldb
```

**Port 5001 already in use?**
```powershell
netstat -ano | findstr :5001
taskkill /PID [PID] /F
```

**Build errors?**
```powershell
dotnet clean
dotnet build
```

---

## ✅ VERIFY IT WORKS

1. **Application starts**: `https://localhost:5001` loads
2. **Swagger UI loads**: `https://localhost:5001/swagger/ui`
3. **Health check works**: `https://localhost:5001/health` returns 200
4. **Database connected**: No connection errors in console

---

## 📝 WHEN YOU'RE READY TO DEPLOY

**Zero code changes needed!** Just:

1. Create database on your server
2. Update `appsettings.{Environment}.json`
3. Run migrations
4. Deploy application

**That's it!**

---

## 📚 NEED MORE DETAILS?

- **Local Development**: Read `LOCAL_DEVELOPMENT_GUIDE.md`
- **Configuration**: Read `LOCAL_CONFIGURATION_REFERENCE.md`
- **Summary**: Read `LOCAL_SETUP_COMPLETE.md`

---

## 🎉 YOU'RE ALL SET!

Your application is **100% local and ready to use right now**! 

No decisions needed. No external servers required. 

**Just start coding!** 🚀
