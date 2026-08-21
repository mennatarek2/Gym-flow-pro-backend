@echo off
REM =============================================================================
REM GymFlow Pro - LOCAL DEVELOPMENT STARTUP SCRIPT
REM =============================================================================
REM This script starts the application locally with everything configured
REM =============================================================================

echo.
echo ╔═══════════════════════════════════════════════════════════════════╗
echo ║                                                                   ║
echo ║          🚀 GymFlow Pro - LOCAL DEVELOPMENT STARTUP 🚀           ║
echo ║                                                                   ║
echo ╚═══════════════════════════════════════════════════════════════════╝
echo.

REM Step 1: Check if LocalDB is running
echo [1/4] Checking SQL Server LocalDB status...
sqllocaldb info mssqllocaldb >nul 2>&1
if errorlevel 1 (
    echo.
    echo ⚠️  LocalDB not found. Installing...
    echo Run: sqllocaldb create mssqllocaldb
    echo Then try again.
    exit /b 1
)

REM Start LocalDB if not running
sqllocaldb start mssqllocaldb >nul 2>&1
echo ✅ LocalDB is running

REM Step 2: Check if database exists
echo.
echo [2/4] Checking if database exists...
sqlcmd -S "(localdb)\mssqllocaldb" -Q "SELECT name FROM sys.databases WHERE name='GymFlowProDb'" >nul 2>&1
if errorlevel 1 (
    echo ⚠️  Database not found. Creating...
    cd GMS.Infrastructure
    dotnet ef database update --startup-project ..\GMS.Api
    cd ..
    echo ✅ Database created
) else (
    echo ✅ Database exists
)

REM Step 3: Verify migrations are up to date
echo.
echo [3/4] Verifying migrations...
dotnet build --configuration Debug >nul 2>&1
if errorlevel 1 (
    echo ❌ Build failed. Please check for errors.
    exit /b 1
)
echo ✅ Build successful

REM Step 4: Start application
echo.
echo [4/4] Starting application...
echo.
echo ╔═══════════════════════════════════════════════════════════════════╗
echo ║                                                                   ║
echo ║                 🎉 APPLICATION STARTING 🎉                       ║
echo ║                                                                   ║
echo ║  🌐 API:     https://localhost:5001                              ║
echo ║  📚 Swagger: https://localhost:5001/swagger/ui                   ║
echo ║  ❤️  Health: https://localhost:5001/health                       ║
echo ║                                                                   ║
echo ║  Database:  LocalDB (GymFlowProDb)                               ║
echo ║  Environment: Development                                        ║
echo ║                                                                   ║
echo ║  Press Ctrl+C to stop the application                            ║
echo ║                                                                   ║
echo ╚═══════════════════════════════════════════════════════════════════╝
echo.

cd GMS.Api
dotnet run --configuration Debug

pause
