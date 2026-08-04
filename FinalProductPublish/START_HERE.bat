@echo off
setlocal EnableExtensions EnableDelayedExpansion

REM ===============================================================================
REM                     SHIFTMANAGER STARTUP ASSISTANT
REM                               Version 5.3.1
REM ===============================================================================
REM
REM This script performs basic safety checks before starting ShiftManager.exe:
REM   1) Detect running ShiftManager.exe and optionally stop it
REM   2) Detect port 5000 conflicts (warn only)
REM   3) Validate critical files exist
REM   4) Validate SEED_ADMIN_PASSWORD is set in appsettings.json
REM   5) Start ShiftManager.exe
REM
REM Notes:
REM   - Keep this window open while the app is running.
REM   - This file is ASCII-only for compatibility in locked-down environments.
REM ===============================================================================

cls
echo.
echo ===============================================================================
echo                     SHIFTMANAGER STARTUP ASSISTANT
echo                               Version 5.3.1
echo ===============================================================================
echo.

REM ------------------------------------------------------------------------------
REM STEP 1/5: Check for running instances
REM ------------------------------------------------------------------------------
echo [STEP 1/5] Checking for running instances...

tasklist /FI "IMAGENAME eq ShiftManager.exe" 2>NUL | find /I "ShiftManager.exe" >NUL
if not errorlevel 1 (
    echo.
    echo [WARNING] ShiftManager.exe is already running.
    echo.
    echo Options:
    echo   [S] Stop running process and start fresh
    echo   [C] Cancel and exit
    echo.
    choice /C SC /N /M "Your choice: "
    if errorlevel 2 (
        echo.
        echo Cancelled.
        echo.
        pause
        exit /b 1
    )
    echo.
    echo Stopping ShiftManager.exe...
    taskkill /F /IM ShiftManager.exe >NUL 2>&1
    timeout /t 4 /nobreak >NUL
    echo Done.
) else (
    echo No running ShiftManager.exe detected.
)

REM ------------------------------------------------------------------------------
REM STEP 2/5: Check port 5000 availability (warn only)
REM ------------------------------------------------------------------------------
echo.
echo [STEP 2/5] Checking port availability (5000)...

netstat -ano | findstr /R /C:":5000 .*LISTENING" >NUL 2>&1
if not errorlevel 1 (
    echo.
    echo [WARNING] Port 5000 appears to be in use (LISTENING).
    echo To see details:
    echo   netstat -ano ^| findstr ":5000"
    echo.
    echo You may:
    echo   - Stop the process using port 5000
    echo   - Configure ShiftManager to use a different port (see deployment guide)
    echo.
    pause
) else (
    echo Port 5000 is available.
)

REM ------------------------------------------------------------------------------
REM STEP 3/5: Verify critical files exist
REM ------------------------------------------------------------------------------
echo.
echo [STEP 3/5] Verifying deployment integrity...

if not exist "ShiftManager.exe" (
    echo [ERROR] ShiftManager.exe not found. Deployment is incomplete.
    pause
    exit /b 1
)

if not exist "appsettings.json" (
    echo [ERROR] appsettings.json not found. Deployment is incomplete.
    pause
    exit /b 1
)

if not exist "SixLabors.ImageSharp.dll" (
    echo [ERROR] SixLabors.ImageSharp.dll not found.
    echo Run VERIFY_FILES.bat if available.
    pause
    exit /b 1
)

if not exist "e_sqlite3.dll" (
    echo [WARNING] e_sqlite3.dll not found. SQLite may fail.
)

REM Optional: detect Zone.Identifier on a critical DLL (best-effort)
REM If PowerShell is blocked, skip detection.
if exist "UNBLOCK_FILES.bat" (
    where powershell.exe >NUL 2>&1
    if errorlevel 1 (
        REM PowerShell not available; cannot check Zone.Identifier.
    ) else (
        powershell.exe -NoProfile -ExecutionPolicy Bypass -Command ^
            "if (Test-Path 'SixLabors.ImageSharp.dll:Zone.Identifier') { exit 1 } else { exit 0 }" ^
            >NUL 2>&1
        if not errorlevel 1 (
            REM Not blocked (exit 0)
        ) else (
            echo.
            echo [CRITICAL] Files appear to be BLOCKED by Windows (Zone.Identifier present).
            echo This is common after copying via USB.
            echo.
            echo Options:
            echo   [U] Run UNBLOCK_FILES.bat now (recommended)
            echo   [C] Continue anyway (may fail)
            echo.
            choice /C UC /N /M "Your choice: "
            if errorlevel 2 (
                echo Continuing without unblocking...
            ) else (
                echo.
                echo Running UNBLOCK_FILES.bat...
                call UNBLOCK_FILES.bat
                if errorlevel 1 (
                    echo [ERROR] Unblock failed. See AIR_GAPPED_DEPLOYMENT_GUIDE.txt.
                    pause
                    exit /b 1
                )
                echo Files unblocked.
            )
        )
    )
)

echo Deployment file checks passed.

REM ------------------------------------------------------------------------------
REM STEP 4/5: Validate configuration (SEED_ADMIN_PASSWORD must not be empty)
REM ------------------------------------------------------------------------------
echo.
echo [STEP 4/5] Validating configuration...

findstr /C:"\"SEED_ADMIN_PASSWORD\": \"\"" appsettings.json >NUL 2>&1
if not errorlevel 1 (
    echo.
    echo [ERROR] SEED_ADMIN_PASSWORD is empty in appsettings.json.
    echo You must set it before first run.
    echo Example:
    echo   "SEED_ADMIN_PASSWORD": "YourStrongPassword123!",
    echo.
    pause
    exit /b 1
)

echo Configuration looks OK.

REM ------------------------------------------------------------------------------
REM STEP 5/5: Start ShiftManager
REM ------------------------------------------------------------------------------
echo.
echo [STEP 5/5] Starting ShiftManager...
echo.
echo What happens next:
echo   - Database (app.db) is created on first run
echo   - Migrations run automatically
echo   - Web server starts on http://localhost:5000
echo.
echo Keep this window open. Closing it stops the application.
echo ===============================================================================
echo.

ShiftManager.exe

echo.
echo ShiftManager has stopped.
echo If unexpected, review errors above and consult deployment guide.
echo.
pause
