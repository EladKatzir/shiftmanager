@echo off
REM ================================================================================
REM                    QUICK FIX - Emergency Deployment Repair
REM ================================================================================
REM
REM This script performs a complete deployment fix in one click:
REM   1. Verifies all critical files are present
REM   2. Unblocks any Windows-blocked DLLs
REM   3. Validates configuration
REM   4. Attempts to start the application
REM
REM USE THIS IF: Application won't start or shows "Could not load assembly" errors
REM ================================================================================

cls
echo.
echo ================================================================================
echo                    QUICK FIX - Emergency Deployment Repair
echo ================================================================================
echo.
echo This script will fix common deployment issues automatically.
echo.
echo What it does:
echo   [1/5] Check if application is already running
echo   [2/5] Verify all critical files are present
echo   [3/5] Unblock DLL files (fixes "Could not load assembly" errors)
echo   [4/5] Validate configuration
echo   [5/5] Start the application
echo.
echo Press any key to begin, or close this window to cancel...
pause >nul

echo.
echo ================================================================================
echo [STEP 1/5] Checking for running instances...
echo ================================================================================
echo.

REM Check if ShiftManager is already running
tasklist /FI "IMAGENAME eq ShiftManager.exe" 2>NUL | find /I /N "ShiftManager.exe">NUL
if "%ERRORLEVEL%"=="0" (
    echo [WARNING] ShiftManager.exe is already running!
    echo.
    echo Options:
    echo   [S] Stop the running process and continue
    echo   [C] Cancel this fix
    echo.
    choice /C SC /N /M "Your choice: "

    if errorlevel 2 (
        echo.
        echo Operation cancelled.
        pause
        exit /b 1
    )

    if errorlevel 1 (
        echo.
        echo Stopping ShiftManager.exe...
        taskkill /F /IM ShiftManager.exe >nul 2>&1
        timeout /t 3 /nobreak >nul
        echo [OK] Process stopped
    )
) else (
    echo [OK] No running instances found
)

echo.
echo ================================================================================
echo [STEP 2/5] Verifying critical files...
echo ================================================================================
echo.

set ERROR_COUNT=0

REM Check critical executables
if not exist "ShiftManager.exe" (
    echo [ERROR] ShiftManager.exe NOT FOUND!
    set /a ERROR_COUNT+=1
) else (
    echo [OK] ShiftManager.exe found
)

if not exist "appsettings.json" (
    echo [ERROR] appsettings.json NOT FOUND!
    set /a ERROR_COUNT+=1
) else (
    echo [OK] appsettings.json found
)

REM Check critical DLLs (must match UNBLOCK_FILES.bat list)
set CRITICAL_DLLS=SixLabors.ImageSharp.dll ShiftManager.dll e_sqlite3.dll Microsoft.EntityFrameworkCore.dll Microsoft.EntityFrameworkCore.Sqlite.dll SQLitePCLRaw.provider.e_sqlite3.dll Microsoft.Data.Sqlite.dll

for %%D in (%CRITICAL_DLLS%) do (
    if not exist "%%D" (
        echo [ERROR] %%D NOT FOUND!
        set /a ERROR_COUNT+=1
    ) else (
        echo [OK] %%D found
    )
)

if %ERROR_COUNT% GTR 0 (
    echo.
    echo ================================================================================
    echo [CRITICAL ERROR] %ERROR_COUNT% file(s) missing!
    echo ================================================================================
    echo.
    echo The deployment appears to be corrupted or incomplete.
    echo Please recopy the entire folder from USB.
    echo.
    pause
    exit /b 1
)

echo.
echo [SUCCESS] All critical files verified

echo.
echo ================================================================================
echo [STEP 3/5] Unblocking DLL files...
echo ================================================================================
echo.

REM Try PowerShell unblock (most reliable method)
echo Attempting to unblock all files using PowerShell...
powershell.exe -ExecutionPolicy Bypass -Command "Get-ChildItem -Path . -Recurse -File | ForEach-Object { try { Unblock-File -Path $_.FullName -ErrorAction SilentlyContinue } catch {} }" 2>nul

if %ERRORLEVEL% EQU 0 (
    echo [OK] Files unblocked successfully
) else (
    echo [WARNING] PowerShell unblock method failed
    echo.
    echo You may need to unblock files manually:
    echo   1. Right-click on this folder
    echo   2. Properties -^> General tab
    echo   3. Check "Unblock" -^> Apply to all files
    echo.
    echo Press any key to continue anyway...
    pause >nul
)

REM Verify critical DLL is unblocked
powershell.exe -ExecutionPolicy Bypass -Command "if (Test-Path 'SixLabors.ImageSharp.dll:Zone.Identifier') { exit 1 } else { exit 0 }" 2>nul
if %ERRORLEVEL% EQU 1 (
    echo.
    echo [WARNING] SixLabors.ImageSharp.dll is still BLOCKED!
    echo This may cause "Could not load file or assembly" errors.
    echo.
    echo Press any key to continue anyway...
    pause >nul
) else (
    echo [OK] Critical DLLs appear to be unblocked
)

echo.
echo ================================================================================
echo [STEP 4/5] Validating configuration...
echo ================================================================================
echo.

REM Check if SEED_ADMIN_PASSWORD is set
findstr /C:"\"SEED_ADMIN_PASSWORD\": \"\"" appsettings.json >nul 2>&1
if %errorlevel% equ 0 (
    echo [ERROR] SEED_ADMIN_PASSWORD is NOT configured!
    echo.
    echo You MUST set an admin password before running.
    echo.
    echo Quick fix:
    echo   1. The next screen will open appsettings.json
    echo   2. Find: "SEED_ADMIN_PASSWORD": "",
    echo   3. Change to: "SEED_ADMIN_PASSWORD": "YourPassword123!",
    echo   4. Save and close Notepad
    echo.
    pause

    notepad appsettings.json

    echo.
    echo Checking if you set the password...
    findstr /C:"\"SEED_ADMIN_PASSWORD\": \"\"" appsettings.json >nul 2>&1
    if %errorlevel% equ 0 (
        echo [ERROR] Password still empty! Cannot continue.
        pause
        exit /b 1
    )
    echo [OK] Password configured
) else (
    echo [OK] Admin password is configured
)

REM Check if port 5000 is available
echo.
echo Checking port availability...
netstat -ano | findstr ":5000" >nul 2>&1
if %errorlevel% equ 0 (
    echo [WARNING] Port 5000 is currently in use
    echo.
    echo The application may fail to start.
    echo You can either:
    echo   1. Stop the application using port 5000
    echo   2. Change the port in appsettings.json
    echo.
    echo Press any key to continue anyway...
    pause >nul
) else (
    echo [OK] Port 5000 is available
)

echo.
echo ================================================================================
echo [STEP 5/5] Starting ShiftManager...
echo ================================================================================
echo.

echo All checks passed! Starting application...
echo.
echo IMPORTANT: Keep this window open! Closing it will stop the application.
echo.
echo Once you see "Now listening on: http://localhost:5000"
echo Open your browser to: http://localhost:5000
echo.
echo Login credentials:
echo   Email: admin@local
echo   Password: [whatever you set in appsettings.json]
echo.
echo ================================================================================
echo.

REM Start the application
ShiftManager.exe

REM If we get here, the application has stopped
echo.
echo.
echo ================================================================================
echo                    Application has stopped
echo ================================================================================
echo.
echo If this was unexpected, check the error messages above.
echo.
echo Common issues:
echo   - "Could not load assembly" - Run this QUICK_FIX.bat again
echo   - "Port in use" - Change port in appsettings.json or stop other app
echo   - Database errors - Delete app.db and restart
echo.
pause
