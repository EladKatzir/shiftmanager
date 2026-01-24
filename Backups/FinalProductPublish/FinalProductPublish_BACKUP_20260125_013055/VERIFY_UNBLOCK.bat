@echo off
setlocal EnableExtensions EnableDelayedExpansion

REM ===============================================================================
REM                    ZONE.IDENTIFIER UNBLOCK VERIFIER
REM                               Version 1.0.0
REM ===============================================================================
REM
REM This script verifies that critical DLL files have been unblocked after USB
REM transfer to air-gapped Windows environments. Files copied via USB are often
REM marked as "blocked" by Windows security, causing cryptic DLL load errors.
REM
REM This script checks 5 critical DLLs for the Zone.Identifier alternate data
REM stream, which indicates the file is still blocked.
REM
REM Exit codes:
REM   0 = All files are unblocked (safe to start)
REM   1 = One or more files are still blocked (must unblock first)
REM
REM ===============================================================================

echo.
echo ===============================================================================
echo            ZONE.IDENTIFIER UNBLOCK VERIFICATION
echo ===============================================================================
echo.
echo Checking if critical DLL files are unblocked...
echo.

REM Check if PowerShell is available
where powershell.exe >NUL 2>&1
if errorlevel 1 (
    echo [ERROR] PowerShell not found. Cannot verify file unblock status.
    echo.
    echo This verification requires PowerShell 5.1 or higher.
    echo Please ensure PowerShell is installed on this system.
    echo.
    pause
    exit /b 1
)

REM Define the 5 critical DLLs to check
set "DLL_COUNT=0"
set "BLOCKED_COUNT=0"

set "DLL[1]=SixLabors.ImageSharp.dll"
set "DLL[2]=ShiftManager.dll"
set "DLL[3]=e_sqlite3.dll"
set "DLL[4]=Microsoft.EntityFrameworkCore.Sqlite.dll"
set "DLL[5]=System.Text.Json.dll"

echo Verifying 5 critical DLLs:
echo.

REM Check each DLL for Zone.Identifier
for /L %%i in (1,1,5) do (
    set "CURRENT_DLL=!DLL[%%i]!"

    REM Check if file exists
    if not exist "!CURRENT_DLL!" (
        echo [%%i/5] !CURRENT_DLL!
        echo       Status: FILE NOT FOUND
        echo.
        set /A BLOCKED_COUNT+=1
    ) else (
        REM Check for Zone.Identifier stream using PowerShell
        powershell.exe -NoProfile -ExecutionPolicy Bypass -Command ^
            "Get-Item '!CURRENT_DLL!' -Stream Zone.Identifier -ErrorAction SilentlyContinue 2>$null" ^
            >NUL 2>&1

        if errorlevel 1 (
            REM No Zone.Identifier found = file is unblocked
            echo [%%i/5] !CURRENT_DLL!
            echo       Status: OK - UNBLOCKED
            echo.
        ) else (
            REM Zone.Identifier exists = file is blocked
            echo [%%i/5] !CURRENT_DLL!
            echo       Status: BLOCKED - MUST UNBLOCK
            echo.
            set /A BLOCKED_COUNT+=1
        )
    )
)

REM Report results
echo ===============================================================================
echo.

if !BLOCKED_COUNT! EQU 0 (
    echo [SUCCESS] All critical DLLs are unblocked.
    echo.
    echo Your deployment is ready to start.
    echo You may now run ShiftManager.exe.
    echo.
    echo ===============================================================================
    exit /b 0
) else (
    echo [FAILED] !BLOCKED_COUNT! file(s) are still BLOCKED or missing.
    echo.
    echo WHAT THIS MEANS:
    echo   Windows has marked these files as potentially unsafe because they were
    echo   copied from another computer (USB drive, network share, etc.).
    echo.
    echo HOW TO FIX:
    echo.
    echo   Option 1 - Run the unblock script (RECOMMENDED):
    echo     1. Run: UNBLOCK_FILES.bat
    echo     2. Then run this verification again
    echo.
    echo   Option 2 - Manual unblock via PowerShell:
    echo     1. Open PowerShell as Administrator
    echo     2. Navigate to this directory
    echo     3. Run: Get-ChildItem *.dll ^| Unblock-File
    echo     4. Then run this verification again
    echo.
    echo   Option 3 - Manual unblock via File Properties:
    echo     1. Right-click each blocked DLL file
    echo     2. Select "Properties"
    echo     3. Check "Unblock" at bottom of General tab
    echo     4. Click OK
    echo     5. Repeat for all blocked files
    echo.
    echo DO NOT START SHIFTMANAGER UNTIL ALL FILES ARE UNBLOCKED.
    echo Starting with blocked files will cause cryptic DLL load errors.
    echo.
    echo ===============================================================================
    pause
    exit /b 1
)
