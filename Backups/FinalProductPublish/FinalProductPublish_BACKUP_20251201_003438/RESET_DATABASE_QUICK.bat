@echo off
color 0E
title RESET DATABASE - Quick Access

echo.
echo ============================================================
echo RESET DATABASE - EMERGENCY UNLOCK
echo ============================================================
echo.
echo This will DELETE the current database and create a fresh one
echo with the password from appsettings.json
echo.
echo WARNING: You will lose ALL data:
echo  - Users
echo  - Shifts
echo  - Time-off requests
echo  - Notifications
echo  - Everything!
echo.
echo Current password in appsettings.json:
findstr "SEED_ADMIN_PASSWORD" appsettings.json
echo.
echo After reset, you can login with:
echo   Email: admin@local
echo   Password: (see above)
echo.
echo ============================================================
set /p CONFIRM="Type YES to continue: "

if not "%CONFIRM%"=="YES" (
    echo.
    echo Cancelled. No changes made.
    pause
    exit /b 0
)

echo.
echo [1/4] Stopping any running instances of ShiftManager...
taskkill /F /IM ShiftManager.exe >nul 2>&1
timeout /t 2 /nobreak >nul
echo [OK] Stopped

echo.
echo [2/4] Deleting database files...
if exist app.db (
    del /F app.db
    echo [OK] Deleted app.db
) else (
    echo [INFO] app.db not found (already deleted or doesn't exist)
)

if exist app.db-shm (
    del /F app.db-shm >nul 2>&1
    echo [OK] Deleted app.db-shm
)

if exist app.db-wal (
    del /F app.db-wal >nul 2>&1
    echo [OK] Deleted app.db-wal
)

echo.
echo [3/4] Starting application to create fresh database...
echo Please wait 12 seconds...
start /B ShiftManager.exe >startup_temp.log 2>&1
timeout /t 12 /nobreak >nul

echo.
echo [4/4] Stopping application...
taskkill /F /IM ShiftManager.exe >nul 2>&1

echo.
echo ============================================================
echo DATABASE RESET COMPLETE!
echo ============================================================
echo.
echo The database has been recreated with default data.
echo.
echo You can now login with:
echo   Email: admin@local
echo   Password: ShareholderDemo2025!
echo.
echo (Or whatever password is in appsettings.json)
echo.
echo Default seeded data:
echo  - 1 Admin user (admin@local)
echo  - 1 Company (Default Company)
echo  - 5 Shift Types (MORNING, NOON, NIGHT, MIDDLE, OFFLINE)
echo.
echo You are NO LONGER locked out!
echo.
echo You can now:
echo  1. Run START_HERE.bat to start the application
echo  2. Login with the credentials above
echo  3. Create new users, shifts, etc.
echo.

pause
