@echo off
setlocal enabledelayedexpansion
color 0A
title IIS 500 Error Diagnostic Tool

echo.
echo ============================================================
echo IIS 500 ERROR DIAGNOSTIC TOOL
echo ============================================================
echo.
echo This tool will help diagnose why IIS is showing Error 500
echo.

REM Get the current directory
set "APP_DIR=%~dp0"
cd /d "%APP_DIR%"

echo [STEP 1] Checking AspNetCoreModuleV2 Installation...
echo ============================================================
reg query "HKLM\SOFTWARE\Microsoft\IIS Extensions\IIS AspNetCore Module V2" >nul 2>&1
if %errorlevel% equ 0 (
    echo [OK] AspNetCoreModuleV2 is installed
) else (
    echo [ERROR] AspNetCoreModuleV2 is NOT installed!
    echo.
    echo SOLUTION: Install .NET Core Hosting Bundle
    echo Download from: https://dotnet.microsoft.com/download/dotnet/8.0
    echo Look for: "Hosting Bundle" for Windows
    echo.
    echo After installation, restart IIS:
    echo    net stop was /y
    echo    net start w3svc
    echo.
    goto :END
)
echo.

echo [STEP 2] Checking for stdout log folder...
echo ============================================================
if not exist "logs" (
    echo [INFO] Creating logs folder...
    mkdir logs
    echo [OK] Logs folder created
) else (
    echo [OK] Logs folder exists
)

REM Set permissions on logs folder
echo [INFO] Setting permissions on logs folder...
icacls logs /grant "IIS_IUSRS:(OI)(CI)M" /T >nul 2>&1
icacls logs /grant "IUSR:(OI)(CI)M" /T >nul 2>&1
echo [OK] Permissions set
echo.

echo [STEP 3] Checking IIS stdout logs for errors...
echo ============================================================
if exist "logs\stdout*.log" (
    echo [INFO] Found stdout logs - showing last 30 lines of most recent:
    echo.
    for /f "delims=" %%F in ('dir /b /o-d logs\stdout*.log 2^>nul') do (
        set "LATEST_LOG=%%F"
        goto :FOUND_LOG
    )
    :FOUND_LOG
    if defined LATEST_LOG (
        powershell -Command "Get-Content 'logs\!LATEST_LOG!' -Tail 30 -ErrorAction SilentlyContinue"
    )
    echo.
) else (
    echo [WARNING] No stdout logs found yet
    echo This means IIS hasn't tried to start the app, or logging is disabled
)
echo.

echo [STEP 4] Checking for blocked DLLs...
echo ============================================================
set BLOCKED_COUNT=0
for /r %%F in (*.dll) do (
    powershell -Command "if (Test-Path '%%F:Zone.Identifier') { Write-Host '[BLOCKED] %%~nxF' -ForegroundColor Red; exit 1 }" 2>nul
    if errorlevel 1 set /a BLOCKED_COUNT+=1
)
if !BLOCKED_COUNT! gtr 0 (
    echo [ERROR] Found !BLOCKED_COUNT! blocked DLLs!
    echo.
    echo SOLUTION: Run UNBLOCK_FILES.bat
    echo.
) else (
    echo [OK] No blocked DLLs found
)
echo.

echo [STEP 5] Checking folder permissions...
echo ============================================================
echo [INFO] Current folder: %APP_DIR%
echo [INFO] Setting IIS permissions...
icacls . /grant "IIS_IUSRS:(OI)(CI)RX" /T >nul 2>&1
icacls . /grant "IUSR:(OI)(CI)RX" /T >nul 2>&1
icacls . /grant "IIS_IUSRS:(OI)(CI)M" /T /Q >nul 2>&1
echo [OK] Permissions updated
echo.

echo [STEP 6] Checking if app.db exists and has permissions...
echo ============================================================
if exist "app.db" (
    echo [OK] Database file exists
    icacls app.db /grant "IIS_IUSRS:M" >nul 2>&1
    icacls app.db /grant "IUSR:M" >nul 2>&1
    echo [OK] Database permissions set
) else (
    echo [INFO] Database doesn't exist yet (will be created on first run)
)
echo.

echo [STEP 7] Checking critical files...
echo ============================================================
set MISSING_COUNT=0
if not exist "ShiftManager.exe" (
    echo [ERROR] ShiftManager.exe is missing!
    set /a MISSING_COUNT+=1
) else (
    echo [OK] ShiftManager.exe exists
)
if not exist "ShiftManager.dll" (
    echo [ERROR] ShiftManager.dll is missing!
    set /a MISSING_COUNT+=1
) else (
    echo [OK] ShiftManager.dll exists
)
if not exist "web.config" (
    echo [ERROR] web.config is missing!
    set /a MISSING_COUNT+=1
) else (
    echo [OK] web.config exists
)
if not exist "appsettings.json" (
    echo [ERROR] appsettings.json is missing!
    set /a MISSING_COUNT+=1
) else (
    echo [OK] appsettings.json exists
)

if !MISSING_COUNT! gtr 0 (
    echo.
    echo [ERROR] !MISSING_COUNT! critical files are missing!
    goto :END
)
echo.

echo [STEP 8] Validating web.config syntax...
echo ============================================================
powershell -Command "[xml]$xml = Get-Content 'web.config'; Write-Host '[OK] web.config syntax is valid'" 2>nul
if errorlevel 1 (
    echo [ERROR] web.config has XML syntax errors!
    goto :END
)
echo.

echo [STEP 9] Checking appsettings.json...
echo ============================================================
findstr /C:"SEED_ADMIN_PASSWORD" appsettings.json >nul 2>&1
if %errorlevel% equ 0 (
    echo [OK] Admin password is configured
) else (
    echo [WARNING] Admin password might not be configured
)
echo.

echo [STEP 10] Testing application startup (standalone)...
echo ============================================================
echo [INFO] Starting application in test mode...
echo [INFO] This will run for 10 seconds to check for errors...
echo.

start /B ShiftManager.exe > iis_diagnostic_test.log 2>&1

timeout /t 10 /nobreak >nul

taskkill /F /IM ShiftManager.exe >nul 2>&1

if exist "iis_diagnostic_test.log" (
    findstr /I /C:"now listening" /C:"application started" iis_diagnostic_test.log >nul 2>&1
    if !errorlevel! equ 0 (
        echo [OK] Application starts successfully in standalone mode
        echo.
        echo Last few lines of startup:
        powershell -Command "Get-Content 'iis_diagnostic_test.log' -Tail 5"
    ) else (
        echo [ERROR] Application failed to start!
        echo.
        echo Error log:
        type iis_diagnostic_test.log
        goto :END
    )
)
echo.

echo ============================================================
echo DIAGNOSTIC SUMMARY
echo ============================================================
echo.
echo Based on the checks above, here are the most common solutions:
echo.
echo 1. If AspNetCoreModuleV2 is missing:
echo    - Install .NET 8.0 Hosting Bundle from Microsoft
echo    - Restart IIS: net stop was /y ^&^& net start w3svc
echo.
echo 2. If you see DLL loading errors in stdout logs:
echo    - Run UNBLOCK_FILES.bat
echo    - Restart IIS application pool
echo.
echo 3. If you see "Failed to start application" in logs:
echo    - Check stdout logs in logs\ folder
echo    - Verify database path and permissions
echo    - Check appsettings.json configuration
echo.
echo 4. For detailed IIS errors:
echo    - Open IIS Manager
echo    - Select your site
echo    - Click "Error Pages"
echo    - Edit 500 error to show "Detailed errors"
echo    - Refresh browser to see detailed error
echo.
echo 5. Check Windows Event Viewer:
echo    - Open Event Viewer
echo    - Windows Logs ^> Application
echo    - Look for errors from "IIS AspNetCore Module V2"
echo.
echo ============================================================
echo NEXT STEPS
echo ============================================================
echo.
echo 1. Review the diagnostic output above
echo 2. Check logs\stdout*.log for detailed errors
echo 3. Check Windows Event Viewer for IIS errors
echo 4. If still stuck, send the following files:
echo    - logs\stdout*.log (most recent)
echo    - iis_diagnostic_test.log (created just now)
echo    - Windows Event Viewer errors (screenshot)
echo.

:END
echo.
echo Press any key to exit...
pause >nul
