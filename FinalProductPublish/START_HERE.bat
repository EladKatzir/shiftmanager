@echo off
REM ================================================================================
REM                    SHIFTMANAGER AUTOMATED STARTUP
REM                           Version 2.0.0
REM ================================================================================
REM
REM This script provides intelligent startup with:
REM   - Process detection and conflict resolution
REM   - Configuration validation
REM   - Port availability checks
REM   - Automatic troubleshooting
REM
REM ================================================================================

setlocal enabledelayedexpansion

REM ANSI color codes (works on Windows 10+)
set "GREEN=[92m"
set "YELLOW=[93m"
set "RED=[91m"
set "BLUE=[94m"
set "RESET=[0m"

cls
echo.
echo %BLUE%================================================================================
echo                    SHIFTMANAGER STARTUP ASSISTANT
echo                           Version 2.0.0
echo ================================================================================%RESET%
echo.

REM ================================================================================
REM Step 1: Check if ShiftManager is already running
REM ================================================================================
echo %BLUE%[STEP 1/5]%RESET% Checking for running instances...

tasklist /FI "IMAGENAME eq ShiftManager.exe" 2>NUL | find /I /N "ShiftManager.exe">NUL
if "%ERRORLEVEL%"=="0" (
    echo.
    echo %YELLOW%[WARNING]%RESET% ShiftManager.exe is already running!
    echo.
    echo This might be:
    echo   - Previous version still running
    echo   - Current version already started
    echo   - Service instance running
    echo.
    echo Options:
    echo   [S] Stop the running process and start fresh
    echo   [C] Cancel and exit (recommended if unsure)
    echo.
    choice /C SC /N /M "Your choice: "

    if errorlevel 2 (
        echo.
        echo %YELLOW%Operation cancelled.%RESET%
        echo.
        pause
        exit /b 1
    )

    if errorlevel 1 (
        echo.
        echo %BLUE%Stopping ShiftManager.exe...%RESET%
        taskkill /F /IM ShiftManager.exe >nul 2>&1

        REM Wait for process to fully terminate and release file locks
        timeout /t 4 /nobreak >nul

        echo %GREEN%Process stopped successfully%RESET%
    )
)

echo %GREEN%No conflicts detected%RESET%

REM ================================================================================
REM Step 2: Check if port 5000 is available
REM ================================================================================
echo.
echo %BLUE%[STEP 2/5]%RESET% Checking port availability...

netstat -ano | findstr ":5000" >nul 2>&1
if %errorlevel% equ 0 (
    echo.
    echo %YELLOW%[WARNING]%RESET% Port 5000 is currently in use by another process
    echo.
    echo To find which process is using the port, run:
    echo   netstat -ano ^| findstr ":5000"
    echo.
    echo You can either:
    echo   1. Stop the process using port 5000
    echo   2. Configure ShiftManager to use a different port (see DEPLOYMENT_GUIDE.txt)
    echo.
    echo Press any key to continue anyway, or Ctrl+C to cancel...
    pause >nul

    echo %YELLOW%Continuing despite port conflict...%RESET%
) else (
    echo %GREEN%Port 5000 is available%RESET%
)

REM ================================================================================
REM Step 3: Verify critical files exist
REM ================================================================================
echo.
echo %BLUE%[STEP 3/5]%RESET% Verifying deployment integrity...

if not exist "ShiftManager.exe" (
    echo %RED%[ERROR]%RESET% ShiftManager.exe not found!
    echo.
    echo The deployment appears to be corrupted or incomplete.
    echo Please re-extract or re-deploy the package.
    echo.
    pause
    exit /b 1
)

if not exist "e_sqlite3.dll" (
    echo %YELLOW%[WARNING]%RESET% e_sqlite3.dll not found - SQLite may not work!
)

if not exist "appsettings.json" (
    echo %RED%[ERROR]%RESET% appsettings.json not found!
    echo Configuration file is missing.
    echo.
    pause
    exit /b 1
)

REM Check for critical DLL (SixLabors.ImageSharp.dll)
if not exist "SixLabors.ImageSharp.dll" (
    echo %RED%[ERROR]%RESET% SixLabors.ImageSharp.dll not found!
    echo This DLL is required for image processing.
    echo Please run VERIFY_FILES.bat to check which files are missing.
    echo.
    pause
    exit /b 1
)

REM Check if files might be blocked (common on air-gapped deployments)
if exist "UNBLOCK_FILES.bat" (
    REM Check if DLL is blocked
    powershell.exe -ExecutionPolicy Bypass -Command "if (Test-Path 'SixLabors.ImageSharp.dll:Zone.Identifier') { exit 1 } else { exit 0 }" 2>nul
    if errorlevel 1 (
        echo.
        echo %RED%[CRITICAL]%RESET% Files are BLOCKED by Windows!
        echo.
        echo This is common after copying files via USB.
        echo You MUST run UNBLOCK_FILES.bat first, or you will get errors like:
        echo   "Could not load file or assembly 'SixLabors.ImageSharp'"
        echo.
        echo Options:
        echo   [U] Run UNBLOCK_FILES.bat now (recommended)
        echo   [C] Continue anyway (not recommended - will likely fail)
        echo.
        choice /C UC /N /M "Your choice: "

        if errorlevel 2 (
            echo.
            echo %YELLOW%Continuing without unblocking...%RESET%
            echo %YELLOW%WARNING: Application startup may fail!%RESET%
        )

        if errorlevel 1 (
            echo.
            echo %BLUE%Running UNBLOCK_FILES.bat...%RESET%
            call UNBLOCK_FILES.bat
            if errorlevel 1 (
                echo.
                echo %RED%Unblock failed!%RESET%
                echo Please see AIR_GAPPED_DEPLOYMENT_GUIDE.txt for manual instructions.
                echo.
                pause
                exit /b 1
            )
            echo.
            echo %GREEN%Files unblocked successfully%RESET%
        )
    )
)

echo %GREEN%All critical files present%RESET%

REM ================================================================================
REM Step 4: Validate configuration
REM ================================================================================
echo.
echo %BLUE%[STEP 4/5]%RESET% Validating configuration...

REM Check if SEED_ADMIN_PASSWORD is set
findstr /C:"\"SEED_ADMIN_PASSWORD\": \"\"" appsettings.json >nul 2>&1
if %errorlevel% equ 0 (
    echo.
    echo %RED%[ERROR]%RESET% SEED_ADMIN_PASSWORD is not configured!
    echo.
    echo You MUST set the admin password before running ShiftManager.
    echo.
    echo Quick fix:
    echo   1. Open appsettings.json in Notepad
    echo   2. Find: "SEED_ADMIN_PASSWORD": "",
    echo   3. Change to: "SEED_ADMIN_PASSWORD": "YourPassword123!",
    echo   4. Save the file
    echo   5. Run this script again
    echo.
    echo See QUICK_START.txt for detailed instructions.
    echo.
    pause
    exit /b 1
)

echo %GREEN%Configuration validated%RESET%

REM ================================================================================
REM Step 5: Start ShiftManager
REM ================================================================================
echo.
echo %BLUE%[STEP 5/5]%RESET% Starting ShiftManager...
echo.
echo %GREEN%ShiftManager is starting up...%RESET%
echo.
echo What happens next:
echo   1. Database will be created (app.db) if first run
echo   2. Migrations will run automatically
echo   3. Demo data will be seeded
echo   4. Web server will start on http://localhost:5000
echo.
echo Estimated startup time: 5-10 seconds
echo.
echo %YELLOW%Keep this window open!%RESET% Closing it will stop the application.
echo.
echo ================================================================================
echo.

REM Start the application
ShiftManager.exe

REM If we get here, the application has stopped
echo.
echo.
echo %YELLOW%ShiftManager has stopped.%RESET%
echo.
echo If this was unexpected, check for error messages above.
echo See DEPLOYMENT_GUIDE.txt for troubleshooting help.
echo.
pause
