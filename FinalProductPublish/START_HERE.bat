@echo off
REM ================================================================================
REM                    SHIFTMANAGER AUTOMATED STARTUP
REM                           Version 2.2.0
REM ================================================================================
REM
REM Features:
REM   - Process detection and conflict resolution
REM   - Configuration validation
REM   - Port availability checks
REM   - Blocked-file detection (for USB/air-gapped deployments)
REM   - Optional diagnostic mode (/diag)
REM   - Logging to ShiftManager_startup.log
REM
REM Designed for AIR-GAPPED environments:
REM   - No internet required
REM   - Uses PowerShell ONLY if available
REM   - All paths are relative to the script's directory
REM ================================================================================

setlocal EnableDelayedExpansion

REM ----------------------------------------------------------------------------
REM Parse command-line arguments (diagnostic mode)
REM ----------------------------------------------------------------------------
set "DIAG=0"
if /I "%~1"=="/diag" set "DIAG=1"
if /I "%~1"=="-diag" set "DIAG=1"

REM ----------------------------------------------------------------------------
REM Ensure we are running from the script directory
REM ----------------------------------------------------------------------------
set "SCRIPT_DIR=%~dp0"
cd /d "%SCRIPT_DIR%" >nul 2>&1

REM ----------------------------------------------------------------------------
REM Prepare log file
REM ----------------------------------------------------------------------------
set "LOGFILE=%SCRIPT_DIR%ShiftManager_startup.log"

REM Simple logging function
REM Usage: call :log "message"
:log
set "MSG=%~1"
REM Timestamped log line
echo [%DATE% %TIME%] %MSG%>>"%LOGFILE%"
if "%DIAG%"=="1" (
    echo %MSG%
)
goto :eof

REM ----------------------------------------------------------------------------
REM Initialize ANSI colors (with graceful fallback if not supported)
REM ----------------------------------------------------------------------------
set "ESC="
for /F "delims=" %%E in ('echo prompt $E^| cmd') do set "ESC=%%E"

if defined ESC (
    set "GREEN=!ESC![92m"
    set "YELLOW=!ESC![93m"
    set "RED=!ESC![91m"
    set "BLUE=!ESC![94m"
    set "RESET=!ESC![0m"
) else (
    REM Fallback: no color (plain text)
    set "GREEN="
    set "YELLOW="
    set "RED="
    set "BLUE="
    set "RESET="
)

cls
echo.
echo %BLUE%================================================================================%RESET%
echo                    SHIFTMANAGER STARTUP ASSISTANT
echo                           Version 2.2.0
echo %BLUE%================================================================================%RESET%
echo.

if "%DIAG%"=="1" (
    echo [DIAG] Diagnostic mode ENABLED. Detailed output and logging are active.
)
call :log "===== ShiftManager startup invoked (diag=%DIAG%) ====="

REM ================================================================================
REM Step 1: Check if ShiftManager is already running
REM ================================================================================
echo %BLUE%[STEP 1/5]%RESET% Checking for running instances...
call :log "STEP 1: Checking for running ShiftManager.exe"

tasklist /FI "IMAGENAME eq ShiftManager.exe" 2>NUL | find /I /N "ShiftManager.exe" >NUL

if "%ERRORLEVEL%"=="0" (
    call :log "ShiftManager.exe is already running."

    echo.
    echo %YELLOW%[WARNING]%RESET% ShiftManager.exe is already running!
    echo.
    echo This might be:
    echo   - A previous version still running
    echo   - The current version already started
    echo   - A service instance running
    echo.
    echo Options:
    echo   [S] Stop the running process and start fresh
    echo   [C] Cancel and exit (recommended if unsure)
    echo.

    choice /C SC /N /M "Your choice: "
    set "USER_CHOICE=!ERRORLEVEL!"
    call :log "User choice in STEP 1 (1=S, 2=C): !USER_CHOICE!"

    if "!USER_CHOICE!"=="2" (
        echo.
        echo %YELLOW%Operation cancelled.%RESET%
        echo.
        call :log "User cancelled startup because ShiftManager.exe was already running."
        pause
        exit /b 1
    )

    if "!USER_CHOICE!"=="1" (
        echo.
        echo %BLUE%Stopping ShiftManager.exe...%RESET%
        call :log "Attempting to kill ShiftManager.exe."
        taskkill /F /IM ShiftManager.exe >nul 2>&1

        REM Wait for process to fully terminate and release file locks
        echo %BLUE%Waiting for process to terminate...%RESET%
        call :log "Waiting 4 seconds for ShiftManager.exe to terminate."
        timeout /t 4 /nobreak >nul

        echo %GREEN%Process stopped successfully.%RESET%
        call :log "ShiftManager.exe stopped successfully."
    )
) else (
    echo %GREEN%No conflicts detected.%RESET%
    call :log "No running instances of ShiftManager.exe detected."
)

REM ================================================================================
REM Step 2: Check if port 5000 is available
REM ================================================================================
echo.
echo %BLUE%[STEP 2/5]%RESET% Checking port 5000 availability...
echo   (this can take a few seconds on some systems)
call :log "STEP 2: Checking port 5000."

netstat -ano | findstr ":5000" >nul 2>&1
if %ERRORLEVEL% EQU 0 (
    call :log "Port 5000 is IN USE."
    echo.
    echo %YELLOW%[WARNING]%RESET% Port 5000 is currently in use by another process.
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
    call :log "User chose to continue despite port 5000 being in use."
) else (
    echo %GREEN%Port 5000 is available.%RESET%
    call :log "Port 5000 is available."
)

REM ================================================================================
REM Step 3: Verify critical files exist
REM ================================================================================
echo.
echo %BLUE%[STEP 3/5]%RESET% Verifying deployment integrity...
call :log "STEP 3: Verifying critical files."

if not exist "ShiftManager.exe" (
    echo %RED%[ERROR]%RESET% ShiftManager.exe not found!
    echo.
    echo The deployment appears to be corrupted or incomplete.
    echo Please re-extract or re-deploy the package.
    echo.
    call :log "ERROR: ShiftManager.exe not found."
    pause
    exit /b 1
)

if not exist "e_sqlite3.dll" (
    echo %YELLOW%[WARNING]%RESET% e_sqlite3.dll not found - SQLite features may not work!
    call :log "WARNING: e_sqlite3.dll not found."
)

if not exist "appsettings.json" (
    echo %RED%[ERROR]%RESET% appsettings.json not found!
    echo Configuration file is missing.
    echo.
    call :log "ERROR: appsettings.json not found."
    pause
    exit /b 1
)

REM Check for critical DLL (SixLabors.ImageSharp.dll)
if not exist "SixLabors.ImageSharp.dll" (
    echo %RED%[ERROR]%RESET% SixLabors.ImageSharp.dll not found!
    echo This DLL is required for image processing.
    echo Please run VERIFY_FILES.bat to check which files are missing.
    echo.
    call :log "ERROR: SixLabors.ImageSharp.dll not found."
    pause
    exit /b 1
)

REM ----------------------------------------------------------------------------
REM Blocked-file check (only if UNBLOCK_FILES.bat exists AND PowerShell is available)
REM ----------------------------------------------------------------------------
if exist "UNBLOCK_FILES.bat" (
    echo.
    echo %BLUE%Checking for blocked files (common after USB transfers)...%RESET%
    call :log "UNBLOCK_FILES.bat found. Checking for blocked files."

    where powershell.exe >nul 2>&1
    if %ERRORLEVEL% NEQ 0 (
        echo %YELLOW%[INFO]%RESET% PowerShell not available - skipping blocked-file check.
        call :log "PowerShell.exe not found. Skipping blocked-file detection."
    ) else (
        echo %BLUE%Running blocked-file detection using PowerShell...%RESET%
        call :log "Running blocked-file detection using PowerShell on SixLabors.ImageSharp.dll."

        powershell.exe -ExecutionPolicy Bypass -Command ^
            "if (Test-Path 'SixLabors.ImageSharp.dll:Zone.Identifier') { exit 1 } else { exit 0 }" 2>nul

        if %ERRORLEVEL% EQU 1 (
            call :log "SixLabors.ImageSharp.dll appears to be BLOCKED (Zone.Identifier present)."
            echo.
            echo %RED%[CRITICAL]%RESET% Files are BLOCKED by Windows!
            echo.
            echo This is common after copying files via USB on an air-gapped machine.
            echo You SHOULD run UNBLOCK_FILES.bat first, or you may get errors like:
            echo   "Could not load file or assembly 'SixLabors.ImageSharp'"
            echo.
            echo Options:
            echo   [U] Run UNBLOCK_FILES.bat now (recommended)
            echo   [C] Continue anyway (NOT recommended - startup may fail)
            echo.

            choice /C UC /N /M "Your choice: "
            set "UNBLOCK_CHOICE=!ERRORLEVEL!"
            call :log "User choice in blocked-file handling (1=U, 2=C): !UNBLOCK_CHOICE!"

            if "!UNBLOCK_CHOICE!"=="2" (
                echo.
                echo %YELLOW%Continuing without unblocking...%RESET%
                echo %YELLOW%WARNING: Application startup may fail!%RESET%
                call :log "User chose to continue WITHOUT running UNBLOCK_FILES.bat."
            )

            if "!UNBLOCK_CHOICE!"=="1" (
                echo.
                echo %BLUE%Running UNBLOCK_FILES.bat...%RESET%
                call :log "Running UNBLOCK_FILES.bat."

                call UNBLOCK_FILES.bat

                if %ERRORLEVEL% NEQ 0 (
                    echo.
                    echo %RED%Unblock failed!%RESET%
                    echo Please see AIR_GAPPED_DEPLOYMENT_GUIDE.txt for manual instructions.
                    echo.
                    call :log "ERROR: UNBLOCK_FILES.bat returned non-zero exit code."
                    pause
                    exit /b 1
                )

                echo.
                echo %GREEN%Files unblocked successfully.%RESET%
                call :log "UNBLOCK_FILES.bat completed successfully."
            )
        ) else (
            echo %GREEN%No blocked files detected for SixLabors.ImageSharp.dll.%RESET%
            call :log "No Zone.Identifier found on SixLabors.ImageSharp.dll."
        )
    )
) else (
    echo.
    echo %YELLOW%[INFO]%RESET% UNBLOCK_FILES.bat not found - skipping blocked-file check.
    call :log "UNBLOCK_FILES.bat not found. Skipping blocked-file detection."
)

echo %GREEN%All critical files present (basic integrity checks passed).%RESET%
call :log "All critical files present. STEP 3 completed."

REM ================================================================================
REM Step 4: Validate configuration
REM ================================================================================
echo.
echo %BLUE%[STEP 4/5]%RESET% Validating configuration (appsettings.json)...
call :log "STEP 4: Validating appsettings.json."

REM Check if SEED_ADMIN_PASSWORD is set to an empty string
findstr /C:"\"SEED_ADMIN_PASSWORD\": \"\"" appsettings.json >nul 2>&1
if %ERRORLEVEL% EQU 0 (
    echo.
    echo %RED%[ERROR]%RESET% SEED_ADMIN_PASSWORD is not configured!
    echo.
    echo You MUST set the initial admin password before running ShiftManager.
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
    call :log "ERROR: SEED_ADMIN_PASSWORD is configured as empty."
    pause
    exit /b 1
)

echo %GREEN%Configuration validated (SEED_ADMIN_PASSWORD is set).%RESET%
call :log "Configuration validated: SEED_ADMIN_PASSWORD is not empty. STEP 4 completed."

REM ================================================================================
REM Step 5: Start ShiftManager
REM ================================================================================
echo.
echo %BLUE%[STEP 5/5]%RESET% Starting ShiftManager...
echo.
echo %GREEN%ShiftManager is starting up...%RESET%
echo.
echo What happens next:
echo   1. Database (app.db) will be created if this is the first run
echo   2. Migrations will run automatically
echo   3. Demo data may be seeded (depending on configuration)
echo   4. Web server will start on http://localhost:5000
echo.
echo On an air-gapped machine, the first startup can take longer than usual
echo (database creation, migrations, and seeding may take several seconds).
echo.
echo %YELLOW%Keep this window open!%RESET% Closing it will stop the application.
echo.
echo ================================================================================
echo.

call :log "STEP 5: Launching ShiftManager.exe."

REM Start the application in the current window and wait for it to exit
ShiftManager.exe
set "APP_EXIT=%ERRORLEVEL%"
call :log "ShiftManager.exe exited with code %APP_EXIT%."

REM If we get here, the application has stopped
echo.
echo.
echo %YELLOW%ShiftManager has stopped.%RESET%
echo.
echo If this was unexpected, check for error messages above.
echo Also check the log file:
echo   %LOGFILE%
echo.
echo See DEPLOYMENT_GUIDE.txt for troubleshooting help.
echo.

call :log "ShiftManager startup script completed. App exit code: %APP_EXIT%."
pause

endlocal
exit /b 0
