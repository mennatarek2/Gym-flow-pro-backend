@echo off
REM Database Setup Script for GymFlowPro
REM This script handles the database setup using dotnet ef CLI

cls
echo.
echo =========================================
echo  GymFlowPro - Database Setup
echo =========================================
echo.

REM Check if we're in the right directory
if not exist "GMS.Api\GMS.Api.csproj" (
    echo ERROR: Must run from D:\GMS\GMS directory
    echo Current: %cd%
    echo.
    echo Please navigate to D:\GMS\GMS and run this script again.
    pause
    exit /b 1
)

echo [1/5] Checking .NET installation...
dotnet --version >nul 2>&1
if %errorlevel% neq 0 (
    echo ERROR: .NET SDK not found. Please install .NET 8 SDK.
    pause
    exit /b 1
)
echo ✓ .NET found
echo.

echo [2/5] Navigating to API project...
cd GMS.Api
if %errorlevel% neq 0 (
    echo ERROR: Failed to navigate to GMS.Api
    pause
    exit /b 1
)
echo ✓ In GMS.Api directory
echo.

echo [3/5] Checking for existing database...
sqlcmd -S "(localdb)\mssqllocaldb" -Q "SELECT name FROM sys.databases WHERE name='GymFlowProDb';" >nul 2>&1
if %errorlevel% equ 0 (
    echo ! Database already exists
    echo.
    set /p drop_db="Do you want to drop and recreate? (Y/N): "
    if /i "!drop_db!"=="Y" (
        echo Dropping database...
        dotnet ef database drop --context GymFlowProDbContext --force
        if %errorlevel% neq 0 (
            echo ERROR: Failed to drop database
            pause
            exit /b 1
        )
        echo ✓ Database dropped
    )
)
echo.

echo [4/5] Applying database migrations...
dotnet ef database update --context GymFlowProDbContext
if %errorlevel% neq 0 (
    echo ERROR: Failed to apply migrations
    echo.
    echo Trying to install dotnet-ef tool...
    dotnet tool install --global dotnet-ef
    echo.
    echo Retrying migrations...
    dotnet ef database update --context GymFlowProDbContext
    if %errorlevel% neq 0 (
        echo ERROR: Still failed. See DATABASE_SETUP_DOTNET_EF.md
        pause
        exit /b 1
    )
)
echo ✓ Migrations applied
echo.

echo [5/5] Verifying database setup...
sqlcmd -S "(localdb)\mssqllocaldb" -d GymFlowProDb -Q "SELECT COUNT(*) as TableCount FROM INFORMATION_SCHEMA.TABLES;" >nul 2>&1
if %errorlevel% neq 0 (
    echo ERROR: Database verification failed
    pause
    exit /b 1
)
echo ✓ Database verified
echo.

echo =========================================
echo ✓ Database setup complete!
echo =========================================
echo.
echo Next steps:
echo   1. Build: dotnet build
echo   2. Run: dotnet run --project GMS.Api
echo   3. Test: http://localhost:5000/health
echo.
pause
